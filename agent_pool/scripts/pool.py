#!/usr/bin/env python3
"""What If filesystem pool. Python stdlib; explicit handoffs, no auto-launch.

Ported from race_game agent_pool/scripts/pool.py (2026-11-16): ticket prefix WI-, plan folder .planning/what-if."""
import argparse
from collections import Counter
from contextlib import contextmanager
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parents[2]
WORKSPACE = HERE   # the checkout holding task_pool.json: the main checkout, also when run from a linked worktree (see bind)
WORKTREE = None    # the linked worktree this script was started from, else None
POOL = WORKSPACE / 'agent_pool'
PLAN = WORKSPACE / '.planning' / 'what-if'
DATA = PLAN / 'task_pool.json'
AGENTS = ('codex', 'claude', 'gemini')
PRIORITY = {'P0': 0, 'P1': 1, 'P2': 2}
SLOTS = 2         # PD-005: two worker slots
SLOT_UNITS = 3    # PD-005: one slot = 1 implementation worker or 3 research workers (role "Research")
CAPACITY = SLOTS * SLOT_UNITS


def load_units(task):
    """Slot units a ticket occupies while queued or in progress: research 1, anything else a whole slot (PD-005)."""
    return 1 if task.get('role') == 'Research' else SLOT_UNITS


def now():
    return datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')


def main_checkout(root):
    """Main checkout of a linked worktree (its .git is a file `gitdir: <main>/.git/worktrees/<name>`), else None."""
    dotgit = Path(root) / '.git'
    if not dotgit.is_file():
        return None
    m = re.match(r'gitdir:\s*(.+)', dotgit.read_text(encoding='utf-8').strip())
    if not m:
        raise ValueError(f'Unreadable .git file: {dotgit}')
    gitdir = Path(m.group(1).strip())
    if not gitdir.is_absolute():
        gitdir = Path(root) / gitdir
    commondir = gitdir / 'commondir'
    common = (gitdir / commondir.read_text(encoding='utf-8').strip() if commondir.is_file() else gitdir).resolve()
    if common.name != '.git':
        raise ValueError(f'Cannot find the main checkout from {common}')
    return common.parent


def bind(root):
    """Point the pool at the main checkout of `root` (`root` itself unless it is a linked worktree)."""
    global WORKSPACE, WORKTREE, POOL, PLAN, DATA
    root = Path(root).resolve()
    main = main_checkout(root)
    WORKSPACE, WORKTREE = (main, root) if main else (root, None)
    POOL, PLAN = WORKSPACE / 'agent_pool', WORKSPACE / '.planning' / 'what-if'
    DATA = PLAN / 'task_pool.json'
    if WORKTREE:
        print(f'pool: main checkout {WORKSPACE} (called from worktree {WORKTREE})')


def git(*args, cwd=None):
    result = subprocess.run(['git', '-C', str(cwd or WORKSPACE), *args], capture_output=True, text=True)
    return result.returncode, result.stdout.strip()


def current_branch():
    code, out = git('symbolic-ref', '--short', 'HEAD', cwd=WORKTREE)
    if code or not out:
        raise ValueError('The worktree has no checked-out branch (detached HEAD)')
    return out


def blob_ok(branch, rel):
    """True when `rel` is a committed, nonempty blob on `branch` (git cat-file -e / -s)."""
    if git('cat-file', '-e', f'{branch}:{rel}')[0]:
        return False
    code, size = git('cat-file', '-s', f'{branch}:{rel}')
    return code == 0 and size.isdigit() and int(size) > 0


def load():
    return json.loads(DATA.read_text(encoding='utf-8'))


def check(payload):
    tasks = payload['tasks']
    byid = {t['id']: t for t in tasks}
    if len(byid) != len(tasks):
        raise ValueError('Duplicate task IDs')
    job_ids = [t['dispatch']['job_id'] for t in tasks if t.get('dispatch')]
    if len(set(job_ids)) != len(job_ids):
        raise ValueError('Duplicate dispatch IDs')
    for phase in payload['phases']:
        if any(t not in byid for t in phase.get('required_tasks', [])):
            raise ValueError(f"Unknown milestone requirement: {phase['id']}")
    seen = {}

    def visit(task_id):
        if seen.get(task_id) == 1:
            raise ValueError(f'Dependency cycle: {task_id}')
        if seen.get(task_id) == 2:
            return
        seen[task_id] = 1
        task = byid[task_id]
        if not re.fullmatch(r'WI-\d{3}', task_id):
            raise ValueError(f'Invalid task ID: {task_id}')
        if task['status'] not in payload['task_statuses']:
            raise ValueError(f'Invalid status: {task_id}')
        if task['phase'] not in {p['id'] for p in payload['phases']}:
            raise ValueError(f'Invalid phase: {task_id}')
        if task['priority'] not in ('P0', 'P1', 'P2'):
            raise ValueError(f'Invalid priority: {task_id}')
        for dep in task['dependencies']:
            if dep not in byid:
                raise ValueError(f'Unknown dependency: {task_id} -> {dep}')
            visit(dep)
        if len(set(task['dependencies'])) != len(task['dependencies']):
            raise ValueError(f'Duplicate dependency: {task_id}')
        if not (PLAN / 'tasks' / f'{task_id}.md').is_file():
            raise ValueError(f'Missing ticket: {task_id}')
        if task['status'] in ('in_progress', 'review', 'done'):
            if any(byid[d]['status'] != 'done' for d in task['dependencies']):
                raise ValueError(f'Unfinished dependency: {task_id}')
        if task['status'] in ('review', 'done'):
            if not task.get('evidence'):
                raise ValueError(f'Missing evidence: {task_id}')
            for ref in task['evidence']:   # in review the evidence may still be only a blob on the ticket branch; done is merged
                evidence_file(ref, task.get('evidence_branch') if task['status'] == 'review' else None)
        if task['status'] == 'done' and not task.get('reviewer'):
            raise ValueError(f'Missing reviewer: {task_id}')
        dispatch = task.get('dispatch')
        if dispatch:
            if dispatch['agent'] not in AGENTS:
                raise ValueError(f'Invalid agent: {task_id}')
            if dispatch['status'] not in ('pending', 'running', 'complete', 'blocked', 'cancelled'):
                raise ValueError(f'Invalid dispatch state: {task_id}')
            pairs = {'pending': 'todo', 'running': 'in_progress', 'complete': ('review', 'done'), 'blocked': 'blocked', 'cancelled': 'todo'}
            expected = pairs[dispatch['status']]
            if task['status'] not in ((expected,) if isinstance(expected, str) else expected):
                raise ValueError(f'Task/dispatch state mismatch: {task_id}')
        seen[task_id] = 2

    for task_id in byid:
        visit(task_id)
    return byid


def available(task, byid):
    dispatch = task.get('dispatch', {})
    return (task['status'] == 'todo' and dispatch.get('status') not in ('pending', 'running')
            and all(byid[d]['status'] == 'done' for d in task['dependencies']))


def evidence_file(value, branch=None):
    """Repo-relative evidence path: a nonempty file in the main checkout, or (with `branch`) a nonempty committed blob on that branch."""
    given = Path(value)
    for root in dict.fromkeys((WORKSPACE, WORKTREE)):   # main checkout first: older evidence lives only there
        if root is None:
            continue
        path = (given if given.is_absolute() else root / value).resolve()
        if path.is_relative_to(WORKSPACE) and path.is_file() and path.stat().st_size:
            return path.relative_to(WORKSPACE).as_posix()
    if branch:   # also from the main checkout: `validate`/`status` there must accept review evidence that is only on the ticket branch
        tree = WORKTREE or WORKSPACE
        path = (given if given.is_absolute() else tree / value).resolve()
        if path.is_relative_to(tree):
            rel = path.relative_to(tree).as_posix()
            if blob_ok(branch, rel):
                return rel
        raise ValueError(f'Evidence is not a committed nonempty file on branch {branch}: {value}')
    raise ValueError(f'Evidence must be a nonempty workspace file: {value}')


def pstate(task):
    d = task.get('dispatch') or {}
    return 'queued' if task['status'] == 'todo' and d.get('status') == 'pending' else task['status']


def log_event(task, before, cmd):
    """One appended JSON line per transition; no consumer yet (the classifier is phase 4)."""
    d = task.get('dispatch') or {}
    line = {'ts': now(), 'task': task['id'], 'from': before, 'to': pstate(task), 'agent': d.get('agent'), 'job_id': d.get('job_id'), 'cmd': cmd}
    with (POOL / 'events.jsonl').open('a', encoding='utf-8', newline='\n') as handle:
        handle.write(json.dumps(line, ensure_ascii=False) + '\n')


def ownership(brief):
    """Owned path globs from the brief's `### Exact write ownership` (same reading as tools/review/branch-scope.py); None without the section."""
    m = re.search(r'^### Exact write ownership\s*$(.*?)(?=^#{1,3} |^\*\*[A-Z][^*]*:\*\*|\Z)', brief, re.M | re.S)
    if not m:
        return None
    owned, mode = [], None
    for line in m.group(1).splitlines():
        head = line.lstrip('- ').lower()
        if not line.startswith(' '):
            mode = 'ro' if head.startswith('read-only') else 'own' if head.startswith(('new', 'owned', 'own', 'also allowed')) else mode
        owned += [tok for tok in re.findall(r'`([^`]+)`', line) if mode == 'own' and ('/' in tok or '.' in tok)]   # the preamble (mode None) is prose
    return owned


def overlaps(a, b):
    """Conservative overlap of two path globs: one static prefix contains the other; a bare file name only meets the same file name."""
    def norm(g):
        g = g.strip().rstrip(';,.')
        return g
    a, b = norm(a), norm(b)
    for x, y in ((a, b), (b, a)):
        if '/' not in x and '*' not in x:
            return y.rsplit('/', 1)[-1] == x
    pa, pb = re.split(r'[*]', a)[0], re.split(r'[*]', b)[0]
    return pa.startswith(pb) or pb.startswith(pa)


def brief_info(task_id):
    path = POOL / 'shared' / 'briefs' / f'{task_id}.md'
    if not path.is_file():
        return None
    text = path.read_text(encoding='utf-8')
    scope = ownership(text)
    gpu = bool(scope and any(g.startswith('engine-unity/') for g in scope)) or bool(re.search(r'(?<!No )Unity (build|player)', text))
    return {'scope': scope, 'gpu': gpu}


def next_rows(payload, byid):
    """Ready tickets ranked by priority then ID, each with START or WAIT and the reason. Only informs; nothing is queued."""
    active = [t for t in payload['tasks'] if t['status'] in ('in_progress', 'review') or pstate(t) == 'queued']
    def info_of(t):
        info = brief_info(t['id'])
        if info is not None and t.get('role') == 'Research':
            info['gpu'] = False   # PD-005: research never runs Unity or takes the notebook slot
        return info
    busy = [(t['id'], info_of(t)) for t in active]
    load = sum(load_units(t) for t in active if t['status'] == 'in_progress' or pstate(t) == 'queued')
    rows = []
    ready = sorted((t for t in payload['tasks'] if available(t, byid)), key=lambda t: (PRIORITY[t['priority']], t['id']))
    for t in ready:
        info = info_of(t)
        why = None
        if info is None:
            why = 'no brief in agent_pool/shared/briefs'
        elif load + load_units(t) > CAPACITY:
            kind = 'research' if t.get('role') == 'Research' else 'implementation'
            why = f'worker slots full for {kind}: {load}/{CAPACITY} units used (implementation {SLOT_UNITS}, research 1; PD-005)'
        elif info['gpu'] and any(b and b['gpu'] and byid[i]['status'] != 'review' for i, b in busy):
            who = [i for i, b in busy if b and b['gpu'] and byid[i]['status'] != 'review']
            why = f"GPU-bound like {', '.join(who)}; the notebook runs one GPU-bound job at a time"
        elif busy and info['scope'] is None:
            why = 'brief has no "### Exact write ownership"; disjointness cannot be shown'
        else:
            for i, b in busy:
                if b is None or b['scope'] is None:
                    if info['scope'] is not None:
                        why = f'{i} has no "### Exact write ownership"; disjointness cannot be shown'
                        break
                    continue
                clash = next(((x, y) for x in info['scope'] for y in b['scope'] if overlaps(x, y)), None)
                if clash:
                    why = f'write scope overlaps {i}: {clash[0]} ~ {clash[1]}'
                    break
        if why:
            rows.append((t, 'WAIT', why))
        else:
            gpu = ', GPU-bound' if info['gpu'] else ''
            rows.append((t, 'START', f"dependencies done, scope disjoint from {', '.join(i for i, _ in busy) or 'nothing active'}{gpu}"))
            busy.append((t['id'], info))
            load += load_units(t)
    return rows


def print_next(payload, byid):
    rows = next_rows(payload, byid)
    if not rows:
        print('next: no ready ticket (all todo tickets wait for dependencies or are reserved)')
    for n, (t, verdict, why) in enumerate(rows, 1):
        print(f"{n}. {t['id']} {t['priority']} {t['preferred_agent']}/{t['preferred_model']} {verdict}: {t['title']} -- {why}")


def run_external(cmd, cwd):
    """The only place handoff starts a process (worktree script, prompt generator); never a worker. Patched in tests."""
    result = subprocess.run(cmd, cwd=str(cwd), capture_output=True, text=True)
    if result.returncode:
        raise ValueError(f"{' '.join(cmd[:3])} failed ({result.returncode}): {(result.stderr or result.stdout).strip()}")
    return result.stdout.strip()


def handoff_brief_problem(task_id):
    """Why the brief cannot back a handoff yet, or None. The worktree is cut from HEAD and the prompt points to "Prepared handoff",
    so both sections must exist and be committed first (race_game RG-028 2026-10-23: worktree created before its brief section)."""
    path = POOL / 'shared' / 'briefs' / f'{task_id}.md'
    if not path.is_file():
        return f'no brief {path.name} in agent_pool/shared/briefs'
    text = path.read_text(encoding='utf-8')
    missing = [s for s, rx in (('Prepared handoff', r'^#+ Prepared handoff'), ('### Exact write ownership', r'^### Exact write ownership\s*$')) if not re.search(rx, text, re.M)]
    if missing:
        return f"brief {path.name} has no {' / '.join(missing)} section"
    code, out = git('status', '--porcelain', '--', str(path))
    if code == 0 and out:
        return f'brief {path.name} has uncommitted changes; commit it first, the worktree is created from HEAD'
    return None


def handoff(payload, byid, t, args):
    """Queue (when needed), create the worktree and the start prompt. Prints the prompt text; claim and the worker launch stay with the user."""
    d = t.get('dispatch', {})
    if d.get('status') != 'pending' and not available(t, byid):
        raise ValueError('Handoff needs a ready (or already queued) task')
    problem = handoff_brief_problem(t['id'])
    if problem:
        raise ValueError(f'Handoff {t["id"]}: {problem}')
    agent = args.agent or (d.get('agent') if d.get('status') == 'pending' else None) or t['preferred_agent']
    worktree = WORKSPACE.parent / f'{WORKSPACE.name}-worktrees' / f"{agent}-{t['id']}"
    others = ', '.join(o['id'] for o in payload['tasks'] if o['id'] != t['id'] and (o['status'] == 'in_progress' or pstate(o) == 'queued'))
    steps = []
    if not worktree.exists():
        steps.append(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'scripts/new-worktree.ps1', '-Agent', agent, '-Ticket', t['id']])
    prompt = [sys.executable, '-X', 'utf8', '-I', 'tools/review/make-prompt.py', 'start', t['id']] + (['--parallel', others] if others else [])
    steps.append(prompt)
    if args.dry_run:
        print(f"Handoff {t['id']} (dry run): {'already queued' if d.get('status') == 'pending' else 'would queue'} for {agent}; worktree {worktree}")
        for step in steps:
            print('  would run:', ' '.join(step))
        return
    if d.get('status') != 'pending':
        before = pstate(t)
        queue_task(t, agent)
        save(payload)
        log_event(t, before, 'handoff')
    for step in steps:
        out = run_external(step, WORKSPACE)
        if out:
            print(out)
    print(f"Handoff {t['id']} ready: job {t['dispatch']['job_id']}, worktree {worktree}, prompt agent_pool/shared/prompts/{t['id']}.md; no process launched")
    prompt_file = POOL / 'shared' / 'prompts' / f"{t['id']}.md"
    if prompt_file.is_file():   # the orchestrator pastes this text to the user; the file alone is not a handed-over prompt
        print(f'--- start prompt (open the session in {worktree}) ---')
        print(prompt_file.read_text(encoding='utf-8').rstrip())
        print('--- end start prompt ---')
    print(f"Claim after the worker starts: python -X utf8 agent_pool/scripts/pool.py --agent {agent} claim {t['id']}")


def queue_task(t, agent):
    if agent == 'gemini' and t['id'] != 'WI-001':
        raise ValueError('Gemini is reserved for bounded read-only inventory; implementation requires a trusted owner')
    model = t['preferred_model'] if agent == t['preferred_agent'] else {'claude': 'sonnet', 'gemini': 'default', 'codex': 'configured'}[agent]
    job_id = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ') + '_' + t['id']
    t['dispatch'] = {'job_id': job_id, 'agent': agent, 'model': model, 'status': 'pending', 'created_at': now()}
    print(f"Queued {t['id']}: {job_id} ({agent}/{model}); no process launched")


@contextmanager
def mutation():
    lock = POOL / '.task_pool.lock'
    try:
        handle = lock.open('x', encoding='utf-8')
    except FileExistsError:
        raise ValueError('Pool is locked. Check another writer; inspect lock before recovering.')
    try:
        with handle:
            handle.write(now())
        yield
    finally:
        lock.unlink()


def atomic_write(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.tmp')
    temp.write_text(content, encoding='utf-8', newline='\n')
    temp.replace(path)


def render(payload):
    index = ['# What If — task index', '',
             f"{len(payload['tasks'])} ticketů. Stavy a dependencies: `task_pool.json`; tento index i tickety generuje `python agent_pool/scripts/pool.py render`.", '',
             '| Ticket | Stav | P | Fáze | Agent/model | Dependencies | Název |',
             '|---|---|---|---|---|---|---|']
    for t in payload['tasks']:
        deps = ', '.join(t['dependencies']) or '—'
        index.append(f"| [{t['id']}](tasks/{t['id']}.md) | {t['status']} | {t['priority']} | {t['phase']} | {t['preferred_agent']}/{t['preferred_model']} | {deps} | {t['title']} |")
        lines = [f"# {t['id']} — {t['title']}", '',
                 f"- **Stav:** {t['status']} (generováno z task_pool.json)",
                 f"- **Milestone:** {t['phase']}", f"- **Priorita:** {t['priority']}",
                 f"- **Role:** {t['role']}", f"- **Odhad:** {t['effort']} (relativní rozsah)",
                 f"- **Agent/model:** {t['preferred_agent']}/{t['preferred_model']}",
                 f"- **Závislosti:** {deps}", '', '## Cíl', '', t['goal'], '',
                 '## Rozsah', '', *('- ' + s for s in t['scope']), '',
                 '## Acceptance criteria', '',
                 *(f"- [{'x' if t['status'] == 'done' else ' '}] {s}" for s in t['acceptance']), '',
                 '## Verifikace', '', *('- ' + s for s in t['verification']), '',
                 '## Write scope / izolace', '', t['write_scope'], '',
                 '## Rizika / poznámky', '', t['risks'], '', '## Předání', '',
                 '- Shrnutí, změněné cesty, git status/branch a stav před zahájením.',
                 '- Přesné příkazy a výsledky; logy, artefakty a nesplněné podmínky.',
                 '- Další krok a checkpoint do agent_pool/agents/<agent>/memory/MEMORY.md.',
                 '- Přechod review; done nastavuje orchestrátor po ověření acceptance.']
        if t.get('evidence'):
            lines += ['', '## Evidence', '', *('- ' + e for e in t['evidence'])]
        atomic_write(PLAN / 'tasks' / f"{t['id']}.md", '\n'.join(lines) + '\n')
    atomic_write(PLAN / 'TASKS.md', '\n'.join(index) + '\n')
    byid = {t['id']: t for t in payload['tasks']}
    board = ['# What If — live pool', '', 'Generováno z task_pool.json. Queue není spuštěný OS proces.', '',
             '## Stav', '', json.dumps(dict(Counter(t['status'] for t in payload['tasks'])), ensure_ascii=False), '',
             '## Ready (volné)', '', *('- ' + t['id'] + ' — ' + t['title'] for t in payload['tasks'] if available(t, byid)), '',
             '## Rezervace / práce / review / blokery', '']
    for t in payload['tasks']:
        d = t.get('dispatch', {})
        if t['status'] in ('in_progress', 'review', 'blocked') or d.get('status') == 'pending':
            board.append(f"- {t['id']}: {t['status']} / {d.get('status', '—')} / {d.get('agent', '—')} — {t.get('block_reason', '')}".rstrip())
        if d:
            brief = (POOL / 'shared' / 'briefs' / f"{t['id']}.md").relative_to(WORKSPACE).as_posix()
            metadata = {"id": d['job_id'], "status": d['status'], "agent": d['agent'],
                        "kind": "task", "task": t['id'], "title": t['title'],
                        "created_at": d['created_at'], "timeout_minutes": 120,
                        "model": d['model']}
            job = '---\n' + '\n'.join(f'{k}: {json.dumps(v, ensure_ascii=False)}' for k, v in metadata.items()) + '\n---\n\n'
            job += f"Read `{brief}` and `AGENTS.md`. Task and job status are canonical in `.planning/what-if/task_pool.json`.\n"
            job += f"\nResult: `agent_pool/agents/{d['agent']}/outbox/{d['job_id']}.md`.\n"
            atomic_write(POOL / 'jobs' / f"{d['job_id']}.md", job)
            atomic_write(POOL / 'agents' / d['agent'] / 'inbox' / f"{d['job_id']}.md", job)
            atomic_write(POOL / 'agents' / d['agent'] / 'state' / f"{d['job_id']}.json", json.dumps(metadata, ensure_ascii=False, indent=2) + '\n')
    atomic_write(POOL / '_pool.md', '\n'.join(board).rstrip() + '\n')


def save(payload):
    check(payload)
    atomic_write(DATA, json.dumps(payload, ensure_ascii=False, indent=2) + '\n')
    render(payload)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('validate', 'status', 'ready', 'next', 'render', 'queue', 'claim', 'reassign', 'review', 'rework', 'handoff', 'approve', 'block', 'retry', 'cancel'))
    parser.add_argument('task', nargs='?')
    parser.add_argument('--agent', choices=AGENTS)
    parser.add_argument('--evidence', action='append', default=[])
    parser.add_argument('--reviewer')
    parser.add_argument('--reason')
    parser.add_argument('--amend', action='store_true', help='review: add evidence to a task that is already in review')
    parser.add_argument('--dry-run', action='store_true', help='handoff: print the steps, change and start nothing')
    args = parser.parse_args()
    if args.command in ('validate', 'status', 'ready', 'next'):
        payload = load()
        byid = check(payload)
        if args.command == 'validate':
            print(f"OK: {len(byid)} tasks; DAG, state and evidence valid.")
        elif args.command == 'status':
            print(json.dumps(dict(Counter(t['status'] for t in payload['tasks'])), ensure_ascii=False))
            for t in payload['tasks']:
                d = t.get('dispatch', {})
                if d and (d['status'] == 'pending' or t['status'] in ('in_progress', 'review', 'blocked')):
                    print(t['id'], t['status'], d['status'], d['agent'], d['job_id'])
        elif args.command == 'next':
            print_next(payload, byid)
        else:
            for t in payload['tasks']:
                if available(t, byid):
                    print(t['id'], t['preferred_agent'], t['preferred_model'], t['title'])
        return
    with mutation():
        payload = load()
        byid = check(payload)
        if args.command == 'render':
            render(payload)
            return
        if args.task not in byid:
            raise ValueError('Specify a known task ID')
        t = byid[args.task]
        d = t.get('dispatch', {})
        before = pstate(t)
        if args.command == 'handoff':
            handoff(payload, byid, t, args)
            return
        if args.command == 'queue':
            if not available(t, byid):
                raise ValueError('Task is reserved, not todo, or dependencies are unfinished')
            queue_task(t, args.agent or t['preferred_agent'])
        elif args.command == 'claim':
            if d.get('status') != 'pending' or t['status'] != 'todo':
                raise ValueError('Queue a ready task first')
            if any(byid[dep]['status'] != 'done' for dep in t['dependencies']):
                raise ValueError('Dependencies are unfinished')
            t['status'], d['status'] = 'in_progress', 'running'
            d['started_at'] = now()
        elif args.command == 'reassign':
            if d.get('status') not in ('pending', 'running') or not args.agent or not args.reason:
                raise ValueError('Reassign requires an active dispatch, --agent and --reason')
            if args.agent == 'gemini' and t['id'] != 'WI-001':
                raise ValueError('Gemini is reserved for bounded read-only inventory')
            old_agent = d['agent']
            if old_agent == args.agent:
                raise ValueError('Dispatch already belongs to this agent')
            t.setdefault('history', []).append({'at': now(), 'reason': args.reason, 'dispatch': dict(d)})
            d['agent'] = args.agent
            d['model'] = t['preferred_model'] if args.agent == t['preferred_agent'] else {'claude': 'sonnet', 'gemini': 'default', 'codex': 'configured'}[args.agent]
            d['reassigned_at'] = now()
            # Only remove this dispatch's old projections; canonical history keeps the handoff.
            for path in (POOL / 'agents' / old_agent / 'inbox' / (d['job_id'] + '.md'),
                         POOL / 'agents' / old_agent / 'state' / (d['job_id'] + '.json')):
                path.unlink(missing_ok=True)
            print(f"Reassigned {t['id']}: {old_agent} -> {args.agent}; no process launched")
        elif args.command == 'review':
            branch = current_branch() if WORKTREE else None
            if args.amend:
                if t['status'] != 'review' or not args.evidence:
                    raise ValueError('Review --amend requires a task in review and --evidence')
                t['evidence'] = list(dict.fromkeys(t['evidence'] + [evidence_file(e, branch) for e in args.evidence]))
                d['amended_at'] = now()
            else:
                if t['status'] != 'in_progress' or not args.evidence:
                    raise ValueError('Review requires in_progress and --evidence')
                t['evidence'] = [evidence_file(e, branch) for e in args.evidence]
                t['status'], d['status'] = 'review', 'complete'
                d['completed_at'] = now()
            if branch:
                t['evidence_branch'] = branch
        elif args.command == 'rework':
            if t['status'] != 'review' or not args.reason:
                raise ValueError('Rework requires a task in review and --reason')
            t.setdefault('history', []).append({'at': now(), 'reason': args.reason, 'dispatch': dict(d), 'evidence': t.get('evidence', []), 'rework': True})
            t['status'], d['status'] = 'in_progress', 'running'
            d.pop('completed_at', None)
            d['reworked_at'] = now()
            t.pop('evidence', None)
            t.pop('evidence_branch', None)
        elif args.command == 'approve':
            if t['status'] != 'review' or not args.reviewer or not args.evidence:
                raise ValueError('Approve requires review, --reviewer and --evidence (acceptance review)')
            t['evidence'] += [evidence_file(e, current_branch() if WORKTREE else None) for e in args.evidence]
            t['evidence'] = list(dict.fromkeys(t['evidence']))
            t.pop('evidence_branch', None)   # done evidence must be merged into the main checkout; check enforces it
            t['status'], t['reviewer'], t['reviewed_at'] = 'done', args.reviewer, now()
        elif args.command == 'block':
            if t['status'] not in ('todo', 'in_progress', 'review') or not args.reason:
                raise ValueError('Block requires active/todo task and --reason')
            t['status'], t['block_reason'] = 'blocked', args.reason
            if d:
                d['status'] = 'blocked'
        elif args.command == 'retry':
            if t['status'] not in ('blocked', 'review') or not args.reason:
                raise ValueError('Retry requires blocked/review task and --reason')
            if any(byid[dep]['status'] != 'done' for dep in t['dependencies']):
                raise ValueError('Dependencies are unfinished')
            t['status'] = 'todo'
            if d:
                d['status'] = 'cancelled'
            t.setdefault('history', []).append({'at': now(), 'reason': args.reason, 'dispatch': dict(d), 'evidence': t.get('evidence', [])})
            t.pop('block_reason', None)
            t.pop('evidence', None)
            t.pop('evidence_branch', None)
        elif args.command == 'cancel':
            if d.get('status') != 'pending' or not args.reason:
                raise ValueError('Only pending jobs can be cancelled; --reason required')
            d['status'] = 'cancelled'
            d['cancel_reason'] = args.reason
        save(payload)
        log_event(t, before, args.command + (' --amend' if args.amend else ''))


if __name__ == '__main__':
    bind(HERE)
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    try:
        main()
    except (ValueError, KeyError, OSError, json.JSONDecodeError) as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        sys.exit(1)
