"""Auto-cleanup of the orchestrator handover (agent_pool/shared/orchestrator-state.md).

The handover is read at every orchestrator takeover, so it must stay small.
This tool moves done/closed text out of it, verbatim, and fails when the
remaining file is over budget.

Moves:
  * `## Resume here`: everything from "Previous text" on (older sessions).
  * any bullet or numbered item that starts with "(done", "(closed" or
    "(superseded" (with its indented continuation lines).
  * a `## Follow-ups...` section, if one was written into the handover:
    its lines go to the top of agent_pool/shared/follow-ups.md.
Moved text is appended to agent_pool/shared/orchestrator-state-archive.md.

Usage (from the main checkout):
  python -X utf8 -I tools/review/trim-handover.py            # trim + size check
  python -X utf8 -I tools/review/trim-handover.py --check    # report only
Exit 0 = within budget, 1 = over budget after trimming (shorten by hand).
"""
import argparse
import datetime
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
STATE = os.path.join(ROOT, 'agent_pool', 'shared', 'orchestrator-state.md')
ARCHIVE = os.path.join(ROOT, 'agent_pool', 'shared', 'orchestrator-state-archive.md')
FOLLOW = os.path.join(ROOT, 'agent_pool', 'shared', 'follow-ups.md')
BUDGET = 12 * 1024
DONE = re.compile(r'^(\s*)(?:[-*]|\d+[a-z]?\.)\s+\**\((?:done|closed|superseded)\b', re.I)
ITEM = re.compile(r'^(\s*)(?:[-*]|\d+[a-z]?\.)\s+')


def read(p):
    with io.open(p, encoding='utf-8') as f:
        return f.read().replace('\r\n', '\n')


def write(p, text):
    with io.open(p, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


def split_sections(text):
    parts = re.split(r'(?m)^(?=## )', text)
    return parts[0], parts[1:]


def trim_done(lines):
    keep, moved, i = [], [], 0
    while i < len(lines):
        m = DONE.match(lines[i])
        if not m:
            keep.append(lines[i])
            i += 1
            continue
        indent = len(m.group(1))
        block = [lines[i]]
        i += 1
        while i < len(lines) and lines[i].strip():
            nxt = ITEM.match(lines[i])
            lead = len(lines[i]) - len(lines[i].lstrip())
            if nxt and len(nxt.group(1)) <= indent:
                break
            if not nxt and lead <= indent and not lines[i].startswith(' '):
                break
            block.append(lines[i])
            i += 1
        moved.extend(block)
    return keep, moved


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--check', action='store_true', help='report only, write nothing')
    ap.add_argument('--budget', type=int, default=BUDGET)
    a = ap.parse_args()

    text = read(STATE)
    head, sections = split_sections(text)
    archived, follow, out = [], [], [head]
    for sec in sections:
        title = sec.split('\n', 1)[0]
        if title.lower().startswith('## follow-ups'):
            follow += [l for l in sec.split('\n')[1:] if l.strip()]
            continue
        if title.startswith('## Resume here'):
            k = sec.find('Previous text')
            if k >= 0:
                end = sec.find('\n\n', k)
                end = len(sec.rstrip('\n')) if end < 0 else end
                cut = sec.rfind(' ', 0, k) if sec.rfind(' ', 0, k) > sec.rfind('\n', 0, k) else k
                archived.append(title + ' (older sessions)\n' + sec[cut:end].strip())
                sec = sec[:cut].rstrip() + sec[end:]
                if not sec.endswith('\n'):
                    sec += '\n'
        keep, moved = trim_done(sec.split('\n'))
        if moved:
            archived.append(title + '\n' + '\n'.join(moved))
        out.append('\n'.join(keep))
    new = ''.join(out)
    size_before, size_after = len(text.encode('utf-8')), len(new.encode('utf-8'))
    print('handover: %d -> %d bytes (budget %d); archived blocks %d; follow-up lines %d'
          % (size_before, size_after, a.budget, len(archived), len(follow)))
    if not a.check and (archived or follow):
        stamp = datetime.datetime.now().strftime('%Y-%m-%d %H:%M')
        if archived:
            arc = read(ARCHIVE) if os.path.exists(ARCHIVE) else '# Orchestrator handover archive (append-only, not read at takeover)\n'
            arc = arc.rstrip('\n') + '\n\n## Archived %s (trim-handover.py)\n\n' % stamp + '\n\n'.join(
                b.replace('\n## ', '\n### ').replace('## ', '### ', 1) for b in archived) + '\n'
            write(ARCHIVE, arc)
        if follow:
            fu = read(FOLLOW)
            first = fu.find('\n- ')
            fu = (fu.rstrip('\n') + '\n' + '\n'.join(follow) + '\n') if first < 0 else (
                fu[:first + 1] + '\n'.join(follow) + '\n' + fu[first + 1:])
            write(FOLLOW, fu)
        write(STATE, new)
    if size_after > a.budget:
        print('OVER BUDGET: shorten `## Resume here` / `## Next step` by hand '
              '(history belongs in git and the archive).', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
