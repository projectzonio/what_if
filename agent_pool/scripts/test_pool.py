"""State-machine and corruption tests; operate only in a disposable temp pool."""
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch
import pool


class PoolTests(unittest.TestCase):
    def setUp(self):
        self.payload = pool.load()
        for t in self.payload['tasks']:
            t['status'] = 'todo'
            for key in ('dispatch', 'evidence', 'reviewer', 'reviewed_at', 'history', 'block_reason'):
                t.pop(key, None)
        self.original = (pool.WORKSPACE, pool.POOL, pool.PLAN, pool.DATA)
        self.temp = TemporaryDirectory(prefix='whatif-pool-test-')
        pool.WORKSPACE = Path(self.temp.name).resolve()
        pool.POOL = pool.WORKSPACE / 'agent_pool'
        pool.PLAN = pool.WORKSPACE / '.planning' / 'what-if'
        pool.DATA = pool.PLAN / 'task_pool.json'
        pool.POOL.mkdir()
        (pool.PLAN / 'tasks').mkdir(parents=True)
        for t in self.payload['tasks']:
            (pool.PLAN / 'tasks' / f"{t['id']}.md").write_text('fixture', encoding='utf-8')
        self.write()
        (pool.WORKSPACE / 'result.md').write_text('Concrete fixture evidence', encoding='utf-8')
        (pool.WORKSPACE / 'review.md').write_text('Reviewer confirmed acceptance in fixture', encoding='utf-8')

    def tearDown(self):
        pool.WORKSPACE, pool.POOL, pool.PLAN, pool.DATA = self.original
        self.temp.cleanup()

    def write(self):
        pool.DATA.write_text(json.dumps(self.payload), encoding='utf-8')

    def run_cli(self, *args):
        with patch.object(sys, 'argv', ['pool.py', *args]), redirect_stdout(io.StringIO()):
            pool.main()

    def task(self, task_id='WI-001'):
        return next(t for t in pool.load()['tasks'] if t['id'] == task_id)

    def queue_claim(self):
        self.run_cli('queue', 'WI-001')
        self.run_cli('claim', 'WI-001')

    def test_full_handoff_does_not_auto_approve(self):
        self.queue_claim()
        self.run_cli('review', 'WI-001', '--evidence', 'result.md')
        self.assertEqual(self.task()['status'], 'review')
        self.assertEqual(self.task()['dispatch']['status'], 'complete')
        self.run_cli('approve', 'WI-001', '--reviewer', 'fixture-reviewer', '--evidence', 'review.md')
        self.assertEqual(self.task()['status'], 'done')
        self.assertIn('review.md', self.task()['evidence'])
        self.run_cli('queue', 'WI-003')
        self.assertEqual(self.task('WI-003')['dispatch']['status'], 'pending')

    def test_dependency_and_double_reservation_guards(self):
        with self.assertRaises(ValueError):
            self.run_cli('queue', 'WI-002')
        self.run_cli('queue', 'WI-001')
        with self.assertRaises(ValueError):
            self.run_cli('queue', 'WI-001')
        with self.assertRaises(ValueError):
            self.run_cli('approve', 'WI-001', '--reviewer', 'r', '--evidence', 'review.md')
        self.assertEqual(self.task()['status'], 'todo')

    def test_review_requires_real_local_evidence(self):
        self.queue_claim()
        for refs in ([], ['--evidence', 'missing.md'], ['--evidence', '../outside.md']):
            with self.assertRaises(ValueError):
                self.run_cli('review', 'WI-001', *refs)
        self.assertEqual(self.task()['status'], 'in_progress')

    def test_approval_requires_explicit_reviewer_and_review(self):
        self.queue_claim()
        self.run_cli('review', 'WI-001', '--evidence', 'result.md')
        for args in ([], ['--reviewer', 'r'], ['--reviewer', 'r', '--evidence', 'missing.md']):
            with self.assertRaises(ValueError):
                self.run_cli('approve', 'WI-001', *args)
        self.assertEqual(self.task()['status'], 'review')

    def test_block_retry_and_cancel(self):
        self.queue_claim()
        self.run_cli('block', 'WI-001', '--reason', 'device unavailable')
        self.assertEqual(self.task()['status'], 'blocked')
        self.run_cli('retry', 'WI-001', '--reason', 'device available')
        old = self.task()['dispatch']['job_id']
        self.run_cli('queue', 'WI-001')
        self.assertNotEqual(old, self.task()['dispatch']['job_id'])
        self.run_cli('cancel', 'WI-001', '--reason', 'deferred')
        self.assertTrue(pool.available(self.task(), {t['id']: t for t in pool.load()['tasks']}))
        self.assertTrue((pool.POOL / 'jobs' / f'{old}.md').is_file())

    def test_lock_rejects_another_writer(self):
        (pool.POOL / '.task_pool.lock').write_text('other writer', encoding='utf-8')
        with self.assertRaises(ValueError):
            self.run_cli('queue', 'WI-001')
        self.assertFalse(self.task().get('dispatch'))

    def test_cycle_missing_dependency_and_fake_done(self):
        self.payload['tasks'][0]['dependencies'] = ['WI-002']
        with self.assertRaises(ValueError):
            pool.check(self.payload)
        self.payload['tasks'][0]['dependencies'] = ['WI-999']
        with self.assertRaises(ValueError):
            pool.check(self.payload)
        self.payload['tasks'][0]['dependencies'] = []
        self.payload['tasks'][0]['status'] = 'done'
        with self.assertRaises(ValueError):
            pool.check(self.payload)

    def test_retries_cannot_reopen_done_and_gemini_is_bounded(self):
        self.queue_claim()
        self.run_cli('review', 'WI-001', '--evidence', 'result.md')
        self.run_cli('approve', 'WI-001', '--reviewer', 'r', '--evidence', 'review.md')
        with self.assertRaises(ValueError):
            self.run_cli('retry', 'WI-001', '--reason', 'oops')
        with self.assertRaises(ValueError):
            self.run_cli('queue', 'WI-005', '--agent', 'gemini')
        with self.assertRaises(ValueError):
            self.run_cli('queue', 'WI-003', '--agent', 'gemini')


class PoolEventsAndNextTests(PoolTests):
    """Events log, rework/amend, `next` and `handoff` in the plain (main checkout) layout."""

    def test_events_log_one_line_per_transition(self):
        self.queue_claim()
        self.run_cli('review', 'WI-001', '--evidence', 'result.md')
        self.run_cli('review', 'WI-001', '--amend', '--evidence', 'review.md')
        self.run_cli('rework', 'WI-001', '--reason', 'fix findings')
        lines = [json.loads(l) for l in (pool.POOL / 'events.jsonl').read_text(encoding='utf-8').splitlines()]
        self.assertEqual([(e['from'], e['to'], e['cmd']) for e in lines],
                         [('todo', 'queued', 'queue'), ('queued', 'in_progress', 'claim'), ('in_progress', 'review', 'review'),
                          ('review', 'review', 'review --amend'), ('review', 'in_progress', 'rework')])
        self.assertTrue(all(e['task'] == 'WI-001' and e['agent'] == 'claude' and e['job_id'] and e['ts'] for e in lines))

    def test_amend_and_rework_rules(self):
        self.queue_claim()
        with self.assertRaises(ValueError):
            self.run_cli('review', 'WI-001', '--amend', '--evidence', 'result.md')   # still in_progress
        with self.assertRaises(ValueError):
            self.run_cli('rework', 'WI-001', '--reason', 'x')
        self.run_cli('review', 'WI-001', '--evidence', 'result.md')
        self.run_cli('review', 'WI-001', '--amend', '--evidence', 'review.md')
        self.assertEqual(self.task()['evidence'], ['result.md', 'review.md'])
        with self.assertRaises(ValueError):
            self.run_cli('rework', 'WI-001')   # reason required
        job = self.task()['dispatch']['job_id']
        self.run_cli('rework', 'WI-001', '--reason', 'finding 2')
        t = self.task()
        self.assertEqual((t['status'], t['dispatch']['status'], t['dispatch']['job_id']), ('in_progress', 'running', job))
        self.assertNotIn('evidence', t)
        self.assertEqual(t['history'][-1]['evidence'], ['result.md', 'review.md'])
        self.run_cli('review', 'WI-001', '--evidence', 'result.md')   # second round
        self.assertEqual(self.task()['status'], 'review')

    def make_ready(self, ids, briefs):
        for t in self.payload['tasks']:
            if t['id'] in ids:
                t['dependencies'] = []
                t['role'] = 'Implementation'   # the live pool mixes Research (1 unit) and implementation (3 units)
        self.write()
        (pool.POOL / 'shared' / 'briefs').mkdir(parents=True)
        for task_id, scope in briefs.items():
            text = f'# {task_id}\n\n### Exact write ownership\n- {scope}\n- Read-only: `AGENTS.md`\n\n## Next\n'
            (pool.POOL / 'shared' / 'briefs' / f'{task_id}.md').write_text(text, encoding='utf-8')

    def next_output(self):
        out = io.StringIO()
        with patch.object(sys, 'argv', ['pool.py', 'next']), redirect_stdout(out):
            pool.main()
        return out.getvalue().splitlines()

    def test_next_ranks_and_applies_scope_and_worker_limit(self):
        self.make_ready(['WI-001', 'WI-002', 'WI-003', 'WI-004', 'WI-005'], {
            'WI-001': 'Owned: `tools/a/**`', 'WI-002': 'Owned: `tools/a/x.py`', 'WI-003': 'Owned: `docs/c.md`', 'WI-004': 'Owned: `docs/d.md`'})
        rows = self.next_output()
        row = {r.split()[1]: r for r in rows}
        self.assertIn('START:', row['WI-001'])
        self.assertIn('WAIT:', row['WI-002'])
        self.assertIn('write scope overlaps WI-001', row['WI-002'])
        self.assertIn('START:', row['WI-003'])
        self.assertIn('slots full', row['WI-004'])
        self.assertIn('no brief', row['WI-005'])
        self.assertTrue(rows[0].startswith('1. WI-001 '))

    def test_next_allows_one_gpu_bound_ticket_at_a_time(self):
        self.make_ready(['WI-001', 'WI-002'], {'WI-001': 'Owned: `engine-unity/Assets/Scripts/A.cs`', 'WI-002': 'Owned: `engine-unity/Assets/Art/**`'})
        self.queue_claim()   # WI-001 running and GPU-bound
        row = {r.split()[1]: r for r in self.next_output()}
        self.assertIn('GPU-bound like WI-001', row['WI-002'])

    def test_handoff_refuses_brief_without_prepared_handoff(self):
        self.make_ready(['WI-001'], {'WI-001': 'Owned: `tools/a/**`'})
        with patch.object(pool, 'run_external', side_effect=AssertionError('must not run')):
            with self.assertRaises(ValueError):
                self.run_cli('handoff', 'WI-001')
        self.assertFalse(self.task().get('dispatch'))

    def test_handoff_queues_and_prepares_without_launching(self):
        self.make_ready(['WI-001'], {'WI-001': 'Owned: `tools/a/**`'})
        brief = pool.POOL / 'shared' / 'briefs' / 'WI-001.md'
        brief.write_text(brief.read_text(encoding='utf-8') + '\n## Prepared handoff (orchestrator)\n- Worktree claude-WI-001.\n', encoding='utf-8')
        self.run_cli('handoff', 'WI-001', '--dry-run')
        self.assertFalse(self.task().get('dispatch'))
        calls = []
        with patch.object(pool, 'run_external', side_effect=lambda cmd, cwd: calls.append(cmd) or ''):
            self.run_cli('handoff', 'WI-001')
        t = self.task()
        self.assertEqual((t['status'], t['dispatch']['status']), ('todo', 'pending'))   # queued, never claimed
        self.assertEqual(len(calls), 2)
        self.assertIn('scripts/new-worktree.ps1', calls[0])
        self.assertIn('tools/review/make-prompt.py', calls[1])


def git_run(cwd, *args):
    subprocess.run(['git', '-C', str(cwd), '-c', 'user.name=t', '-c', 'user.email=t@example.com', *args], check=True, capture_output=True)


class PoolFromWorktreeTests(unittest.TestCase):
    """pool.py started inside a linked worktree reads and writes the main checkout; evidence is a committed blob on the ticket branch."""

    def setUp(self):
        self.addCleanup(lambda: setattr(pool, 'WORKTREE', None))
        PoolTests.setUp(self)
        self.main = pool.WORKSPACE
        git_run(self.main, 'init', '-b', 'main')
        git_run(self.main, 'add', '-A')
        git_run(self.main, 'commit', '-m', 'fixture')
        self.wt_temp = TemporaryDirectory(prefix='nightracer-pool-wt-')
        self.wt = Path(self.wt_temp.name).resolve() / 'claude-WI-001'
        git_run(self.main, 'worktree', 'add', '-b', 'agent/claude/WI-001', str(self.wt))
        with redirect_stdout(io.StringIO()):
            pool.bind(self.wt)

    def tearDown(self):
        pool.WORKTREE = None
        PoolTests.tearDown(self)
        self.wt_temp.cleanup()

    run_cli = PoolTests.run_cli
    write = PoolTests.write
    task = PoolTests.task
    queue_claim = PoolTests.queue_claim

    def commit(self, rel, text):
        path = self.wt / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')
        git_run(self.wt, 'add', rel)
        git_run(self.wt, 'commit', '-m', rel)

    def test_bind_detects_worktree_and_main(self):
        self.assertEqual((pool.WORKSPACE, pool.WORKTREE), (self.main, self.wt))
        pool.bind(self.main)
        self.assertEqual((pool.WORKSPACE, pool.WORKTREE), (self.main, None))

    def test_commands_from_worktree_write_main_not_the_worktree_copy(self):
        self.queue_claim()
        self.assertEqual(self.task()['status'], 'in_progress')
        self.assertTrue((pool.POOL / 'events.jsonl').is_file())
        self.assertFalse((self.wt / 'agent_pool' / 'events.jsonl').exists())
        copy = json.loads((self.wt / '.planning' / 'what-if' / 'task_pool.json').read_text(encoding='utf-8'))
        self.assertEqual(next(t for t in copy['tasks'] if t['id'] == 'WI-001')['status'], 'todo')   # worktree copy untouched

    def test_branch_blob_evidence_passes_and_missing_blob_fails(self):
        self.queue_claim()
        self.commit('agent_pool/out/report.md', 'committed evidence')
        self.commit('agent_pool/out/empty.md', '')
        (self.wt / 'agent_pool/out/dirty.md').write_text('never committed', encoding='utf-8')
        for ref in ('agent_pool/out/dirty.md', 'agent_pool/out/none.md', 'agent_pool/out/empty.md', '../escape.md'):
            with self.assertRaises(ValueError):
                self.run_cli('review', 'WI-001', '--evidence', ref)
        self.assertEqual(self.task()['status'], 'in_progress')
        self.run_cli('review', 'WI-001', '--evidence', 'agent_pool/out/report.md')
        t = self.task()
        self.assertEqual((t['status'], t['evidence'], t['evidence_branch']), ('review', ['agent_pool/out/report.md'], 'agent/claude/WI-001'))
        self.assertEqual(pool.check(pool.load())['WI-001']['status'], 'review')   # validate accepts the blob for review
        self.assertFalse((self.main / 'agent_pool/out/report.md').exists())

    def test_done_still_requires_the_file_in_main(self):
        self.queue_claim()
        self.commit('agent_pool/out/report.md', 'committed evidence')
        self.run_cli('review', 'WI-001', '--evidence', 'agent_pool/out/report.md')
        self.commit('agent_pool/out/review.md', 'acceptance review')
        with self.assertRaises(ValueError):
            self.run_cli('approve', 'WI-001', '--reviewer', 'r', '--evidence', 'agent_pool/out/review.md')
        self.assertEqual(self.task()['status'], 'review')
        (self.main / 'agent_pool' / 'out').mkdir()   # simulate the merge
        for rel in ('report.md', 'review.md'):
            (self.main / 'agent_pool' / 'out' / rel).write_text('merged', encoding='utf-8')
        self.run_cli('approve', 'WI-001', '--reviewer', 'r', '--evidence', 'agent_pool/out/review.md')
        self.assertEqual(self.task()['status'], 'done')
        self.assertNotIn('evidence_branch', self.task())

    def test_amend_and_rework_from_worktree(self):
        self.queue_claim()
        self.commit('agent_pool/out/a.md', 'first')
        self.run_cli('review', 'WI-001', '--evidence', 'agent_pool/out/a.md')
        self.commit('agent_pool/out/b.md', 'second')
        self.run_cli('review', 'WI-001', '--amend', '--evidence', 'agent_pool/out/b.md')
        self.assertEqual(self.task()['evidence'], ['agent_pool/out/a.md', 'agent_pool/out/b.md'])
        self.run_cli('rework', 'WI-001', '--reason', 'finding')
        self.assertEqual((self.task()['status'], self.task()['dispatch']['status']), ('in_progress', 'running'))
        self.assertEqual(pool.check(pool.load())['WI-001']['status'], 'in_progress')


if __name__ == '__main__':
    unittest.main(verbosity=2)
