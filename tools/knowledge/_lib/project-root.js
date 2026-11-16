'use strict';

// What If repository root: the directory that holds WhatIf.sln next to
// .planning/what-if. Works from the main checkout and from every worktree
// (each worktree is a full checkout). Throws instead of guessing - a knowledge
// tool that silently picks the wrong root indexes the wrong tree and still
// reports success (see .claude/skills/test-the-test).

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

function findProjectRoot(startDir = __dirname) {
  let dir = path.resolve(startDir);
  while (true) {
    if (fs.existsSync(path.join(dir, 'WhatIf.sln'))
      && fs.existsSync(path.join(dir, '.planning', 'what-if'))) {
      return dir;
    }
    const parent = path.dirname(dir);
    if (parent === dir) throw new Error(`What If root not found from ${startDir}`);
    dir = parent;
  }
}

// Generated knowledge outputs (index, code graph, lint report). Under
// artifacts/, which .gitignore and the hygiene check already keep out of Git,
// so every worktree has its own index and nothing generated is committed.
function reportsDir(root = findProjectRoot()) {
  return path.join(root, 'artifacts', 'knowledge');
}

// The main checkout, where pool.py mutates task_pool.json and renders TASKS.md.
// A worktree's own copy of TASKS.md is frozen at its branch point, so ticket
// status must be read from here (2026-10-19: the second-brain worktree answered
// "RG-078 todo" eight days after it was done). Null outside Git.
function mainCheckoutRoot(root = findProjectRoot()) {
  const r = spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], { cwd: root, encoding: 'utf8' });
  if (r.status !== 0 || !r.stdout.trim()) return null;
  const main = path.dirname(r.stdout.trim());
  return fs.existsSync(path.join(main, '.planning', 'what-if')) ? main : null;
}

function gitHead(root = findProjectRoot()) {
  const r = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' });
  return r.status === 0 ? r.stdout.trim() : null;
}

module.exports = { findProjectRoot, reportsDir, mainCheckoutRoot, gitHead };
