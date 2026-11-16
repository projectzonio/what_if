#!/usr/bin/env node
'use strict';

// Headless integrity audit for the What If second-brain wiki.
// The wiki remains the source of truth; this tool only validates it and derives
// a machine-readable graph from existing links. Default mode is read-only.
//
// Ported from zonio_app tools/maintenance/lint-wiki.js on 2026-10-11. Added for
// What If: an empty wiki is a fatal finding (a lint that checked nothing must
// not report 0 errors), and Zonio work-pool references in wiki/skills are fatal
// (the user's explicit separation rule, see
// wiki/decision/knowledge-layer-separate-from-zonio.md).

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { findProjectRoot, reportsDir } = require('./_lib/project-root');
const { parseFrontmatter } = require('./_lib/simple-frontmatter');

const DEFAULT_ROOT = findProjectRoot(__dirname);
const PAGE_TYPES = new Set(['entity', 'concept', 'decision']);
const KNOWLEDGE_STATUSES = new Set([
  'implemented', 'historical', 'planned', 'provisional', 'superseded',
]);
const LIST_FIELDS = new Set(['sources', 'aliases', 'supersedes', 'superseded_by']);
const KNOWN_FIELDS = new Set([
  'type', 'slug', 'sources', 'updated', 'status', 'aliases', 'last_verified',
  'source_fingerprint', 'supersedes', 'superseded_by',
]);

function parseArgs(argv) {
  const args = {
    root: DEFAULT_ROOT,
    wikiDir: null,
    reportDir: null,
    json: false,
    writeReport: false,
    strict: false,
    stale: true,
  };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--root') args.root = path.resolve(argv[++i]);
    else if (arg === '--wiki-dir') args.wikiDir = path.resolve(argv[++i]);
    else if (arg === '--report-dir') args.reportDir = path.resolve(argv[++i]);
    else if (arg === '--json') args.json = true;
    else if (arg === '--write-report') args.writeReport = true;
    else if (arg === '--strict') args.strict = true;
    else if (arg === '--no-stale') args.stale = false;
    else if (arg === '--help' || arg === '-h') {
      printHelp();
      process.exit(0);
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }
  args.wikiDir ||= path.join(args.root, 'wiki');
  args.reportDir ||= reportsDir(args.root);
  return args;
}

function printHelp() {
  console.log(`Usage:
  node tools/knowledge/lint-wiki.js [options]

Options:
  --json                 Emit the complete machine-readable audit.
  --write-report         Write latest-wiki-integrity.{json,md} to artifacts/knowledge.
  --strict               Treat orphan pages and schema warnings as failures.
  --no-stale             Skip filesystem mtime freshness checks.
  --root <path>          Override project root (used by fixtures/tests).
  --wiki-dir <path>      Override wiki directory.
  --report-dir <path>    Override report directory.
`);
}

function slash(value) {
  return value.replace(/\\/g, '/');
}

function normalizeHeading(value) {
  return value
    .trim()
    .replace(/[*_`~]/g, '')
    .replace(/\s+#+\s*$/, '')
    .normalize('NFKC')
    .toLocaleLowerCase('en-US')
    .replace(/[^\p{L}\p{N}\s-]/gu, '')
    .replace(/\s+/g, '-');
}

function extractHeadings(text) {
  const headings = new Set();
  for (const match of text.matchAll(/^#{1,6}\s+(.+?)\s*$/gm)) {
    headings.add(normalizeHeading(match[1]));
  }
  return headings;
}

function extractBlocks(text) {
  return new Set([...text.matchAll(/(?:^|\s)\^([A-Za-z0-9-]+)\s*$/gm)].map((m) => m[1]));
}

function withoutCode(text) {
  return text
    .replace(/```[\s\S]*?```/g, '')
    .replace(/~~~[\s\S]*?~~~/g, '')
    .replace(/`[^`\r\n]*`/g, '');
}

function validIsoDate(value) {
  return typeof value === 'string'
    && /^\d{4}-\d{2}-\d{2}$/.test(value)
    && !Number.isNaN(Date.parse(`${value}T00:00:00Z`));
}

function isInsideRoot(root, candidate) {
  const rel = path.relative(root, candidate);
  return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
}

function newestMtime(target, seen = new Set()) {
  if (!fs.existsSync(target)) return 0;
  const real = fs.realpathSync(target);
  if (seen.has(real)) return 0;
  seen.add(real);
  const stat = fs.statSync(target);
  if (stat.isFile()) return stat.mtimeMs;
  if (!stat.isDirectory()) return 0;
  let newest = stat.mtimeMs;
  for (const entry of fs.readdirSync(target, { withFileTypes: true })) {
    if (entry.name === '.git' || entry.name === 'node_modules') continue;
    newest = Math.max(newest, newestMtime(path.join(target, entry.name), seen));
  }
  return newest;
}

// Nad tímhle prahem soubor nečteme celý do paměti. `readFileSync` nad 2 GiB
// vyhodí ERR_FS_FILE_TOO_LARGE a protože se fingerprint počítá pro KAŽDOU
// stránku, jediný velký soubor v `sources:` shodí lint pro celou wiki — což se
// 2026-09-02 stalo: `vanilla_v1.11/src/database/mqtt_data.db` má 2,0 GB
// (retention je záměrně vypnutý, `DATABASE_RETENTION_DAYS=0`, takže DB dál
// roste) a je uvedená v `sources:` u weather-adaptive-sampling-cadence-lag.
// Proudový hash zůstává obsahový a deterministický, jen se nedrží v RAM.
const STREAM_HASH_ABOVE_BYTES = 8 * 1024 * 1024;

function hashFileStreaming(target) {
  const hash = crypto.createHash('sha256');
  const fd = fs.openSync(target, 'r');
  try {
    const buffer = Buffer.allocUnsafe(1024 * 1024);
    let bytesRead;
    while ((bytesRead = fs.readSync(fd, buffer, 0, buffer.length, null)) > 0) {
      hash.update(buffer.subarray(0, bytesRead));
    }
  } finally {
    fs.closeSync(fd);
  }
  return hash.digest('hex');
}

function collectSourceEntries(target, base = target, entries = []) {
  const stat = fs.lstatSync(target);
  const relative = slash(path.relative(base, target)) || '.';
  if (stat.isSymbolicLink()) {
    entries.push({ path: relative, kind: 'symlink', value: fs.readlinkSync(target) });
    return entries;
  }
  if (stat.isFile()) {
    if (stat.size > STREAM_HASH_ABOVE_BYTES) {
      entries.push({ path: relative, kind: 'file-streamed', value: Buffer.from(`sha256:${hashFileStreaming(target)}`) });
    } else {
      entries.push({ path: relative, kind: 'file', value: fs.readFileSync(target) });
    }
    return entries;
  }
  if (!stat.isDirectory()) return entries;
  for (const entry of fs.readdirSync(target, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    if (entry.name === '.git' || entry.name === 'node_modules') continue;
    collectSourceEntries(path.join(target, entry.name), base, entries);
  }
  return entries;
}

function fingerprintSources(root, sources) {
  const hash = crypto.createHash('sha256');
  for (const source of [...sources].sort()) {
    const abs = path.resolve(root, source);
    if (path.isAbsolute(source) || !isInsideRoot(root, abs) || !fs.existsSync(abs)) continue;
    hash.update(`source:${slash(source)}\0`);
    for (const entry of collectSourceEntries(abs)) {
      hash.update(`${entry.kind}:${entry.path}\0`);
      hash.update(entry.value);
      hash.update('\0');
    }
  }
  return `sha256:${hash.digest('hex')}`;
}

function loadPages(root, wikiDir) {
  const pages = [];
  for (const type of PAGE_TYPES) {
    const dir = path.join(wikiDir, type);
    if (!fs.existsSync(dir)) continue;
    for (const filename of fs.readdirSync(dir).sort()) {
      if (!filename.endsWith('.md')) continue;
      const absPath = path.join(dir, filename);
      const text = fs.readFileSync(absPath, 'utf8').replace(/^\uFEFF/, '');
      const slug = filename.slice(0, -3);
      const parsed = parseFrontmatter(text);
      pages.push({
        type,
        slug,
        id: `${type}/${slug}`,
        absPath,
        sourcePath: slash(path.relative(root, absPath)),
        wikiPath: slash(path.relative(wikiDir, absPath)).replace(/\.md$/i, ''),
        text,
        fm: parsed.data,
        frontmatterErrors: parsed.errors,
        headings: extractHeadings(withoutCode(text)),
        blocks: extractBlocks(withoutCode(text)),
      });
    }
  }
  return pages;
}

function buildResolver(pages, findings) {
  const resolver = new Map();
  function add(key, page, kind) {
    const normalized = slash(key).replace(/^wiki\//i, '').replace(/\.md$/i, '').toLocaleLowerCase('en-US');
    if (!normalized) return;
    if (!resolver.has(normalized)) resolver.set(normalized, []);
    resolver.get(normalized).push({ page, kind });
  }
  for (const page of pages) {
    add(page.id, page, 'path');
    add(page.slug, page, 'slug');
    const aliases = Array.isArray(page.fm.aliases) ? page.fm.aliases : [];
    aliases.forEach((alias) => add(alias, page, 'alias'));
  }
  for (const [key, matches] of resolver.entries()) {
    const uniquePages = [...new Set(matches.map((m) => m.page.id))];
    if (uniquePages.length > 1) {
      findings.aliasConflicts.push({ key, pages: uniquePages });
    }
  }
  return resolver;
}

function resolveWikiTarget(rawTarget, currentPage, resolver) {
  const cleaned = slash(rawTarget.trim()).replace(/^wiki\//i, '').replace(/\.md$/i, '');
  if (!cleaned) return { page: currentPage };
  const matches = resolver.get(cleaned.toLocaleLowerCase('en-US')) || [];
  const pages = [...new Map(matches.map((m) => [m.page.id, m.page])).values()];
  if (pages.length === 1) return { page: pages[0] };
  if (pages.length > 1) return { ambiguous: pages.map((p) => p.id) };
  return { missing: cleaned };
}

function splitTarget(raw) {
  const displaySplit = raw.split('|', 1)[0].trim();
  const hash = displaySplit.indexOf('#');
  if (hash < 0) return { target: displaySplit, subpath: null };
  return { target: displaySplit.slice(0, hash), subpath: displaySplit.slice(hash + 1) };
}

function validateSubpath(sourcePage, targetPage, subpath, findings, link) {
  if (!subpath) return;
  if (subpath.startsWith('^')) {
    if (!targetPage.blocks.has(subpath.slice(1))) {
      findings.linkErrors.push({ ...link, reason: 'missing-block', target: targetPage.id, subpath });
    }
    return;
  }
  if (!targetPage.headings.has(normalizeHeading(subpath))) {
    findings.linkErrors.push({ ...link, reason: 'missing-heading', target: targetPage.id, subpath });
  }
}

function canonicalSubpath(subpath) {
  if (!subpath) return null;
  return subpath.startsWith('^') ? subpath : normalizeHeading(subpath);
}

function addEdge(edges, edge) {
  const key = JSON.stringify(edge);
  if (!edges._keys.has(key)) {
    edges._keys.add(key);
    edges.push(edge);
  }
}

function validateWikiLinks(page, resolver, findings, edges) {
  const text = withoutCode(page.text);
  for (const match of text.matchAll(/(!?)\[\[([^\]]+)\]\]/g)) {
    const { target, subpath } = splitTarget(match[2]);
    const resolved = resolveWikiTarget(target, page, resolver);
    const base = { page: page.id, syntax: 'wikilink', raw: match[0] };
    if (resolved.missing) {
      findings.linkErrors.push({ ...base, reason: 'unresolved', target: resolved.missing });
      continue;
    }
    if (resolved.ambiguous) {
      findings.linkErrors.push({ ...base, reason: 'ambiguous', targets: resolved.ambiguous });
      continue;
    }
    validateSubpath(page, resolved.page, subpath, findings, base);
    addEdge(edges, {
      from: page.id,
      to: resolved.page.id,
      relation: match[1] ? 'embeds' : 'links_to',
      ...(subpath ? { subpath: canonicalSubpath(subpath) } : {}),
    });
  }
}

function validateMarkdownLinks(root, wikiDir, page, pagesByAbs, findings, edges) {
  const text = withoutCode(page.text);
  for (const match of text.matchAll(/(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+["'][^"']*["'])?\)/g)) {
    const raw = match[1].replace(/^<|>$/g, '');
    if (/^(?:https?:|mailto:|tel:|data:)/i.test(raw)) continue;
    const hash = raw.indexOf('#');
    const rawPath = hash >= 0 ? raw.slice(0, hash) : raw;
    const subpath = hash >= 0 ? decodeURIComponent(raw.slice(hash + 1)) : null;
    let targetPage = page;
    if (rawPath) {
      let decoded;
      try { decoded = decodeURIComponent(rawPath); } catch { decoded = rawPath; }
      const abs = path.resolve(path.dirname(page.absPath), decoded);
      if (!isInsideRoot(root, abs)) {
        findings.linkErrors.push({ page: page.id, syntax: 'markdown', raw: match[0], reason: 'outside-root' });
        continue;
      }
      if (!fs.existsSync(abs)) {
        findings.linkErrors.push({ page: page.id, syntax: 'markdown', raw: match[0], reason: 'missing-file', target: slash(path.relative(root, abs)) });
        continue;
      }
      targetPage = pagesByAbs.get(path.resolve(abs).toLocaleLowerCase('en-US')) || null;
    }
    if (targetPage) {
      validateSubpath(page, targetPage, subpath, findings, { page: page.id, syntax: 'markdown', raw: match[0] });
      addEdge(edges, {
        from: page.id,
        to: targetPage.id,
        relation: 'links_to',
        ...(subpath ? { subpath: canonicalSubpath(subpath) } : {}),
      });
    } else if (subpath && path.resolve(path.dirname(page.absPath), rawPath).startsWith(wikiDir)) {
      findings.linkErrors.push({ page: page.id, syntax: 'markdown', raw: match[0], reason: 'non-page-heading-target' });
    }
  }
}

function validateSchema(root, page, findings, doStale) {
  for (const error of page.frontmatterErrors) findings.schemaErrors.push({ page: page.id, reason: error });
  if (!PAGE_TYPES.has(page.fm.type)) {
    findings.schemaErrors.push({ page: page.id, reason: `invalid or missing type '${page.fm.type || ''}'` });
  } else if (page.fm.type !== page.type) {
    findings.schemaErrors.push({ page: page.id, reason: `type '${page.fm.type}' does not match directory '${page.type}'` });
  }
  if (page.fm.slug && page.fm.slug !== page.slug) {
    findings.schemaErrors.push({ page: page.id, reason: `slug '${page.fm.slug}' does not match filename '${page.slug}'` });
  }
  if (!Array.isArray(page.fm.sources) || page.fm.sources.length === 0) {
    findings.schemaErrors.push({ page: page.id, reason: 'sources must be a non-empty list' });
  }
  if (!validIsoDate(page.fm.updated)) {
    findings.schemaErrors.push({ page: page.id, reason: `updated must be YYYY-MM-DD, got '${page.fm.updated || ''}'` });
  }
  if (page.fm.last_verified && !validIsoDate(page.fm.last_verified)) {
    findings.schemaErrors.push({ page: page.id, reason: `last_verified must be YYYY-MM-DD, got '${page.fm.last_verified}'` });
  }
  if (page.fm.source_fingerprint && !/^sha256:[0-9a-f]{64}$/.test(page.fm.source_fingerprint)) {
    findings.schemaErrors.push({ page: page.id, reason: 'source_fingerprint must be sha256:<64 lowercase hex>' });
  }
  if (page.fm.status && !KNOWLEDGE_STATUSES.has(page.fm.status)) {
    findings.schemaErrors.push({ page: page.id, reason: `unsupported status '${page.fm.status}'` });
  }
  for (const field of LIST_FIELDS) {
    if (page.fm[field] !== undefined && !Array.isArray(page.fm[field])) {
      findings.schemaErrors.push({ page: page.id, reason: `${field} must be a list` });
    }
  }
  for (const field of Object.keys(page.fm)) {
    if (!KNOWN_FIELDS.has(field)) findings.schemaWarnings.push({ page: page.id, reason: `unknown field '${field}'` });
  }
  const sources = Array.isArray(page.fm.sources) ? page.fm.sources : [];
  let newest = 0;
  for (const source of sources) {
    const abs = path.resolve(root, source);
    if (path.isAbsolute(source) || !isInsideRoot(root, abs)) {
      findings.brokenSources.push({ page: page.id, source, reason: 'source must be repo-relative' });
      continue;
    }
    if (!fs.existsSync(abs)) {
      findings.brokenSources.push({ page: page.id, source, reason: 'missing' });
      continue;
    }
    if (doStale) newest = Math.max(newest, newestMtime(abs));
  }
  page.sourceFingerprint = fingerprintSources(root, sources);
  if (page.fm.source_fingerprint && page.fm.source_fingerprint !== page.sourceFingerprint) {
    findings.sourceDrift.push({
      page: page.id,
      verifiedFingerprint: page.fm.source_fingerprint,
      currentFingerprint: page.sourceFingerprint,
    });
  }
  if (doStale && newest) {
    const verified = page.fm.last_verified || page.fm.updated;
    if (validIsoDate(verified) && newest > Date.parse(`${verified}T00:00:00Z`) + 24 * 60 * 60 * 1000) {
      findings.stale.push({
        page: page.id,
        verified,
        newestSourceChange: new Date(newest).toISOString(),
      });
    }
  }
  if (page.text.includes('\uFFFD') || page.text.includes('\u0000')) {
    findings.encodingErrors.push({ page: page.id, reason: 'replacement or NUL character present' });
  }
  const suspicious = page.text.match(/(?:\u00c3.|\u00c2.|\u00c4.|\u00c5.|\u0139.|\u0102.|\u00e2\u20ac|\u00e2\u201e)/g) || [];
  if (suspicious.length) {
    findings.encodingWarnings.push({ page: page.id, reason: 'possible mojibake tokens', count: suspicious.length });
  }
}

function validateRelationFields(page, resolver, findings, edges) {
  for (const field of ['supersedes', 'superseded_by']) {
    const targets = Array.isArray(page.fm[field]) ? page.fm[field] : [];
    for (const target of targets) {
      const resolved = resolveWikiTarget(target, page, resolver);
      if (!resolved.page) {
        findings.linkErrors.push({ page: page.id, syntax: 'frontmatter', field, target, reason: resolved.ambiguous ? 'ambiguous' : 'unresolved' });
        continue;
      }
      addEdge(edges, {
        from: page.id,
        to: resolved.page.id,
        relation: field,
      });
    }
  }
}

function parseIndexEntries(indexText) {
  const entries = [];
  for (const match of indexText.matchAll(/^- \[(entity|concept|decision)\]\s+([^\s]+)\s+(?:—|-)/gm)) {
    entries.push(`${match[1]}/${match[2]}`);
  }
  return entries;
}

function validateSkillMirrors(root, findings) {
  const agentsRoot = path.join(root, '.agents', 'skills');
  const claudeRoot = path.join(root, '.claude', 'skills');
  // Zonio mirrors every skill into .agents/skills for Codex. What If keeps
  // skills in .claude/skills only (Codex orchestrates from AGENTS.md and the
  // pool), so a mirror is checked only once a project opts in by creating both.
  if (!fs.existsSync(agentsRoot) || !fs.existsSync(claudeRoot)) return;
  const names = new Set();
  for (const dir of [agentsRoot, claudeRoot]) {
    if (!fs.existsSync(dir)) continue;
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory()) names.add(entry.name);
    }
  }
  for (const name of [...names].sort()) {
    for (const relativeFile of ['SKILL.md', path.join('agents', 'openai.yaml')]) {
      const agentFile = path.join(agentsRoot, name, relativeFile);
      const claudeFile = path.join(claudeRoot, name, relativeFile);
      const files = [agentFile, claudeFile].map((file) => slash(path.relative(root, file)));
      const agentExists = fs.existsSync(agentFile);
      const claudeExists = fs.existsSync(claudeFile);
      if (!agentExists && !claudeExists) continue;
      if (!agentExists || !claudeExists) {
        findings.skillMirror.push({ files, reason: 'project skill compatibility mirror missing' });
        continue;
      }
      if (!fs.readFileSync(agentFile).equals(fs.readFileSync(claudeFile))) {
        if (relativeFile === 'SKILL.md') {
          const agentParsed = parseFrontmatter(fs.readFileSync(agentFile, 'utf8'));
          const claudeParsed = parseFrontmatter(fs.readFileSync(claudeFile, 'utf8'));
          const compatible = agentParsed.data.name === claudeParsed.data.name
            && agentParsed.data.description === claudeParsed.data.description;
          if (!compatible) {
            findings.skillMirror.push({ files, reason: 'project skill compatibility frontmatter differs' });
          } else {
            findings.skillVariant.push({ files, reason: 'harness-specific skill bodies differ' });
          }
        } else {
          findings.skillVariant.push({ files, reason: 'harness-specific skill metadata differs' });
        }
      }
    }
  }
}

// index.md points at wiki/catalog.md#<type>-<slug> for the long form of every
// truncated entry. Neither file is a wiki *page*, so nothing above ever looked
// at those links - and on 2026-08-07 six of them pointed at anchors that did
// not exist, while this linter reported "0 link errors". A page added to the
// index in already-compacted form gets a detail link but no catalogue entry,
// and the dangling link then reads as a successful pointer.
//
// Same class as the dead `sources:` entry recorded in
// wiki/concept/retrieval-cost-and-corpus-reachability.md: a reference that
// reports success while resolving to nothing.
function validateCatalogAnchors(wikiDir, findings) {
  const indexPath = path.join(wikiDir, 'index.md');
  const catalogPath = path.join(wikiDir, 'catalog.md');
  if (!fs.existsSync(indexPath) || !fs.existsSync(catalogPath)) return;

  const catalog = fs.readFileSync(catalogPath, 'utf8');
  const anchors = new Set();
  for (const m of catalog.matchAll(/<a\s+id="([^"]+)"/g)) anchors.add(m[1]);

  const index = fs.readFileSync(indexPath, 'utf8');
  for (const m of index.matchAll(/\[detail\]\(catalog\.md#([^)]+)\)/g)) {
    if (!anchors.has(m[1])) {
      findings.linkErrors.push({
        page: 'index.md',
        syntax: 'catalog-detail',
        raw: m[0],
        target: `catalog.md#${m[1]}`,
        reason: 'missing-catalog-anchor',
      });
    }
  }
}

// Zonio work-pool artefacts that must never become instructions here. Mentioning
// zonio_app as the provenance of a ported tool is fine; pointing an agent at
// the Zonio pool, its topic folders or its pool tooling is not.
const FOREIGN_POOL_PATTERNS = [
  /v1\.11_workplan/i,
  /agent_pool[\\/]shared[\\/][a-z0-9-]+-20\d{6}/i,
  /collect-open-tasks/i,
  /OPEN_TASKS\.md/,
  /context-router\.js/i,
  /session-handoff\.js/i,
  /validate-pool-topic/i,
  /research-instead-of-asking/i,
];

function validateForeignReferences(root, pages, findings) {
  const targets = pages.map((p) => ({ id: p.id, text: p.text }));
  for (const dir of ['.claude/skills', '.agents/skills']) {
    const abs = path.join(root, dir);
    if (!fs.existsSync(abs)) continue;
    const stack = [abs];
    while (stack.length) {
      const cur = stack.pop();
      for (const e of fs.readdirSync(cur, { withFileTypes: true })) {
        const child = path.join(cur, e.name);
        if (e.isDirectory()) stack.push(child);
        else if (/\.(md|ya?ml)$/i.test(e.name)) {
          targets.push({ id: slash(path.relative(root, child)), text: fs.readFileSync(child, 'utf8') });
        }
      }
    }
  }
  for (const t of targets) {
    for (const re of FOREIGN_POOL_PATTERNS) {
      const m = t.text.match(re);
      if (m) findings.foreignReferences.push({ page: t.id, match: m[0], reason: 'Zonio pool reference (separation rule)' });
    }
  }
}

function auditWiki(options = {}) {
  const root = path.resolve(options.root || DEFAULT_ROOT);
  const wikiDir = path.resolve(options.wikiDir || path.join(root, 'wiki'));
  const findings = {
    brokenSources: [],
    schemaErrors: [],
    schemaWarnings: [],
    linkErrors: [],
    aliasConflicts: [],
    orphan: [],
    missingIndexPage: [],
    stale: [],
    sourceDrift: [],
    encodingErrors: [],
    encodingWarnings: [],
    skillMirror: [],
    skillVariant: [],
    foreignReferences: [],
    emptyWiki: [],
  };
  if (!fs.existsSync(wikiDir)) throw new Error(`Wiki directory not found: ${wikiDir}`);
  const indexPath = path.join(wikiDir, 'index.md');
  if (!fs.existsSync(indexPath)) throw new Error(`Wiki index not found: ${indexPath}`);
  const pages = loadPages(root, wikiDir);
  const resolver = buildResolver(pages, findings);
  const pagesByAbs = new Map(pages.map((p) => [path.resolve(p.absPath).toLocaleLowerCase('en-US'), p]));
  const edges = [];
  Object.defineProperty(edges, '_keys', { value: new Set(), enumerable: false });
  const indexEntries = parseIndexEntries(fs.readFileSync(indexPath, 'utf8'));
  const indexed = new Set(indexEntries);
  const pageIds = new Set(pages.map((p) => p.id));

  for (const page of pages) {
    validateSchema(root, page, findings, options.stale !== false);
    validateWikiLinks(page, resolver, findings, edges);
    validateMarkdownLinks(root, wikiDir, page, pagesByAbs, findings, edges);
    validateRelationFields(page, resolver, findings, edges);
    if (!indexed.has(page.id)) findings.orphan.push({ page: page.id });
  }
  for (const id of indexEntries) {
    if (!pageIds.has(id)) findings.missingIndexPage.push({ page: id });
  }
  validateSkillMirrors(root, findings);
  validateCatalogAnchors(wikiDir, findings);
  validateForeignReferences(root, pages, findings);
  if (pages.length === 0) {
    findings.emptyWiki.push({ wikiDir: slash(path.relative(root, wikiDir)), reason: 'no entity/concept/decision pages found - nothing was checked' });
  }

  const fatal = findings.brokenSources.length
    + findings.schemaErrors.length
    + findings.linkErrors.length
    + findings.aliasConflicts.length
    + findings.missingIndexPage.length
    + findings.encodingErrors.length
    + findings.skillMirror.length
    + findings.foreignReferences.length
    + findings.emptyWiki.length;
  const strictFailures = fatal + findings.orphan.length + findings.schemaWarnings.length + findings.sourceDrift.length;
  return {
    schema: 'whatif-wiki-integrity.v1',
    generatedAt: new Date().toISOString(),
    root: slash(path.relative(root, root)) || '.',
    wikiDir: slash(path.relative(root, wikiDir)) || '.',
    summary: {
      pages: pages.length,
      edges: edges.length,
      fatal,
      strictFailures,
      ...Object.fromEntries(Object.entries(findings).map(([key, value]) => [key, value.length])),
    },
    findings,
    graph: {
      schema: 'whatif-wiki-graph.v1',
      generatedFrom: 'wiki frontmatter and resolved Markdown/wikilinks',
      nodes: pages.map((p) => ({
        id: p.id,
        path: p.sourcePath,
        type: p.type,
        ...(p.fm.status ? { status: p.fm.status } : {}),
        ...(Array.isArray(p.fm.aliases) && p.fm.aliases.length ? { aliases: p.fm.aliases } : {}),
        sourceFingerprint: p.sourceFingerprint,
      })),
      edges,
    },
  };
}

function renderMarkdown(report) {
  const s = report.summary;
  const lines = [
    '# Wiki integrity report',
    '',
    `Generated: ${report.generatedAt}`,
    '',
    `- Pages: ${s.pages}`,
    `- Derived edges: ${s.edges}`,
    `- Fatal findings: ${s.fatal}`,
    `- Broken sources: ${s.brokenSources}`,
    `- Schema errors / warnings: ${s.schemaErrors} / ${s.schemaWarnings}`,
    `- Link errors / alias conflicts: ${s.linkErrors} / ${s.aliasConflicts}`,
    `- Orphans / missing index pages: ${s.orphan} / ${s.missingIndexPage}`,
    `- Stale candidates: ${s.stale}`,
    `- Source fingerprint drift: ${s.sourceDrift}`,
    `- Encoding errors / warnings: ${s.encodingErrors} / ${s.encodingWarnings}`,
  ];
  for (const [name, items] of Object.entries(report.findings)) {
    if (!items.length) continue;
    lines.push('', `## ${name}`, '');
    items.forEach((item) => lines.push(`- ${JSON.stringify(item)}`));
  }
  return `${lines.join('\n')}\n`;
}

function renderConsole(report) {
  const s = report.summary;
  const lines = [
    `Wiki lint: ${s.pages} pages checked, ${s.edges} relations derived.`,
    `  fatal findings:     ${s.fatal}`,
    `  broken sources:     ${s.brokenSources}`,
    `  schema errors:      ${s.schemaErrors}`,
    `  link errors:        ${s.linkErrors}`,
    `  orphan pages:       ${s.orphan}`,
    `  stale candidates:   ${s.stale}`,
    `  source drift:       ${s.sourceDrift}`,
    `  encoding warnings:  ${s.encodingWarnings}`,
    `  foreign refs:       ${s.foreignReferences}`,
  ];
  for (const [name, items] of Object.entries(report.findings)) {
    if (!items.length) continue;
    lines.push('', `${name.toUpperCase()}:`);
    items.forEach((item) => lines.push(`  ${JSON.stringify(item)}`));
  }
  return lines.join('\n');
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const report = auditWiki({ root: args.root, wikiDir: args.wikiDir, stale: args.stale });
  if (args.writeReport) {
    fs.mkdirSync(args.reportDir, { recursive: true });
    fs.writeFileSync(path.join(args.reportDir, 'latest-wiki-integrity.json'), `${JSON.stringify(report, null, 2)}\n`, 'utf8');
    fs.writeFileSync(path.join(args.reportDir, 'latest-wiki-integrity.md'), renderMarkdown(report), 'utf8');
  }
  console.log(args.json ? JSON.stringify(report, null, 2) : renderConsole(report));
  const failures = args.strict ? report.summary.strictFailures : report.summary.fatal;
  process.exitCode = failures > 0 ? 1 : 0;
}

if (require.main === module) main();

module.exports = {
  auditWiki,
  parseFrontmatter,
  normalizeHeading,
  fingerprintSources,
  renderMarkdown,
};
