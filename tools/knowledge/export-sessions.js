#!/usr/bin/env node
'use strict';

// Export What If agent transcripts (Claude Code + Codex) into readable
// per-session Markdown/JSONL, for distilling skills and wiki pages.
//
// The exporter itself is NOT copied: zonio_app/tools/training-data/
// claude-session-exporter.js (2 500 lines, Zonio-maintained) is called
// read-only. This wrapper only supplies What If's inputs, because the
// exporter's defaults look for Zonio projects:
//   - Claude: ~/.claude/projects/D--workspace-what-if* (main + every worktree)
//   - Codex:  rollouts whose session_meta cwd is the what_if checkout. They
//     must be passed explicitly - discovery by name does not find them - and
//     the exporter then files them under source `v1.11` (its Zonio fallback
//     label). That label is cosmetic; the session content is What If's.
//
// Output: artifacts/session-export/<UTC stamp>/ (gitignored). Transcripts are
// private working material - never commit them; the exporter redacts
// secret-like values but that is a best effort, not a guarantee.

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const { findProjectRoot } = require('./_lib/project-root');

const ROOT = findProjectRoot(__dirname);

function exporterPath() {
  const candidates = [
    process.env.WHATIF_SESSION_EXPORTER,
    path.resolve(ROOT, '..', 'zonio_app', 'tools', 'training-data', 'claude-session-exporter.js'),
    path.resolve(ROOT, '..', '..', 'zonio_app', 'tools', 'training-data', 'claude-session-exporter.js'),
  ].filter(Boolean);
  const hit = candidates.find((p) => fs.existsSync(p));
  if (!hit) {
    throw new Error(`session exporter not found; set WHATIF_SESSION_EXPORTER. Tried:\n  ${candidates.join('\n  ')}`);
  }
  return hit;
}

function claudeInputs() {
  const dir = path.join(os.homedir(), '.claude', 'projects');
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true })
    .filter((e) => e.isDirectory() && /^D--workspace-what-if($|-)/i.test(e.name))
    .map((e) => path.join(dir, e.name));
}

function walkJsonl(dir, out) {
  if (!fs.existsSync(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const abs = path.join(dir, e.name);
    if (e.isDirectory()) walkJsonl(abs, out);
    else if (e.isFile() && e.name.endsWith('.jsonl')) out.push(abs);
  }
  return out;
}

// Codex rollouts name no project; the first record carries the cwd.
function codexInputs() {
  const base = path.join(os.homedir(), '.codex');
  const files = [...walkJsonl(path.join(base, 'sessions'), []), ...walkJsonl(path.join(base, 'archived_sessions'), [])];
  return files.filter((file) => {
    const fd = fs.openSync(file, 'r');
    try {
      const buf = Buffer.alloc(8192);
      const n = fs.readSync(fd, buf, 0, buf.length, 0);
      const head = buf.subarray(0, n).toString('utf8');
      const cwd = head.match(/"cwd":"([^"]*)"/);
      return Boolean(cwd && /what_if/i.test(cwd[1]));
    } finally {
      fs.closeSync(fd);
    }
  });
}

function main() {
  const argv = process.argv.slice(2);
  if (argv.includes('--help') || argv.includes('-h')) {
    console.log(`Usage: node tools/knowledge/export-sessions.js [--session <id or prefix>] [--since <ISO>] [--dry-run]

Exports What If Claude Code + Codex transcripts to artifacts/session-export/<stamp>/.
--session exports one Claude session ("pokračuj v session 9ec35e67...") in seconds
instead of every what_if transcript. Extra exporter options (e.g. --since,
--max-text-chars) are passed through.`);
    return;
  }
  const dryRun = argv.includes('--dry-run');
  const sessionAt = argv.indexOf('--session');
  const session = sessionAt >= 0 ? argv[sessionAt + 1] : null;
  if (sessionAt >= 0 && !session) {
    console.error('--session needs a session id or prefix');
    process.exit(2);
  }
  const pass = argv.filter((a, i) => a !== '--dry-run' && (sessionAt < 0 || (i !== sessionAt && i !== sessionAt + 1)));
  let claude = claudeInputs();
  let codex = codexInputs();
  if (session) {
    const hits = claude.flatMap((dir) => fs.readdirSync(dir)
      .filter((name) => name.endsWith('.jsonl') && name.startsWith(session))
      .map((name) => path.join(dir, name)));
    if (hits.length !== 1) {
      // Zero or several matches must not silently export the wrong session.
      console.error(`--session ${session}: ${hits.length} matching Claude transcripts${hits.length ? `:\n  ${hits.join('\n  ')}` : ''}`);
      process.exit(1);
    }
    claude = hits;
    codex = [];
  }
  if (!claude.length && !codex.length) {
    // An empty export must not look like a successful one.
    console.error('no What If transcripts found (looked in ~/.claude/projects/D--workspace-what-if* and ~/.codex rollouts with a what_if cwd)');
    process.exit(1);
  }
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  const outDir = path.join(ROOT, 'artifacts', 'session-export', stamp);
  const args = [exporterPath(), '--only-project-match', 'what-if', '--out-dir', outDir, '--no-clean-output',
    ...claude.flatMap((p) => ['--input', p]), ...codex.flatMap((p) => ['--input', p]), ...pass];
  console.log(`claude project dirs: ${claude.length}, codex rollouts: ${codex.length}`);
  if (dryRun) {
    console.log(['node', ...args].join(' '));
    return;
  }
  fs.mkdirSync(outDir, { recursive: true });
  const r = spawnSync(process.execPath, args, { stdio: 'inherit' });
  if (r.status !== 0) process.exit(r.status || 1);
  console.log(`\nreadable sessions: ${path.relative(ROOT, path.join(outDir, '_all', 'sessions')).replace(/\\/g, '/')}/*.md`);
}

if (require.main === module) main();

module.exports = { claudeInputs, codexInputs, exporterPath };
