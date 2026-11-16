#!/usr/bin/env node
'use strict';

// What If RAG index (GraphRAG-lite, text half): BM25 chunks over the
// project's own knowledge and code, a wiki link graph, and C# symbol labels
// from code-graph-cs.js.
//
// Ported from zonio_app tools/rag/build-rag-index.js on 2026-10-11. Kept
// verbatim (with their measured reasons): heading-trail chunking, rag-skip
// masking, BM25 postings, wiki graph, "cited sources are indexed", skill
// `description` as `purpose`. Dropped as Zonio-only: FW family pins, research
// protocols, the optional embeddings lane (needs a ~380 MB npm package; this
// repo stays dependency-free - see wiki/decision/knowledge-layer-separate-from-zonio.md).
//
// Output goes to artifacts/knowledge/ (gitignored, per worktree). Nothing here
// writes into the indexed tree.

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { findProjectRoot, reportsDir, mainCheckoutRoot, gitHead } = require('./_lib/project-root');
const { parseFrontmatter } = require('./_lib/simple-frontmatter');
const { buildCodeGraph, writeGraph, functionsInRange } = require('./code-graph-cs');

const ROOT = findProjectRoot(__dirname);
const REPORTS_DIR = reportsDir(ROOT);

// What an agent needs to orient: the canonical planning docs and pool rules,
// briefs and acceptance reviews (the measured results and pitfalls of every
// ticket), durable agent memory, the second-brain wiki, the skills, and code.
// Deliberately NOT indexed: agent inbox/state/jobs (dispatch plumbing, derived
// from task_pool.json), raw run data under benchmarks/ (indexed only when a
// wiki page cites the exact file - see isCitedArtefact), and engine caches.
const DEFAULT_INPUTS = [
  'AGENTS.md',
  'README.md',
  '.planning/what-if/README.md',
  '.planning/what-if/docs',
  '.planning/what-if/tasks',
  '.planning/what-if/project-policy.json',
  'agent_pool/README.md',
  'agent_pool/shared/briefs',
  'agent_pool/shared/reviews',
  'agent_pool/agents/claude/memory',
  'agent_pool/agents/codex/memory',
  'agent_pool/agents/gemini/memory',
  'agent_pool/shared/orchestrator-state.md',
  'wiki',
  '.claude/skills',
  'src',
  'tests',
  'engine-unity/What If/Assets/Scripts',
  'engine-unity/What If/Assets/Editor',
  'engine-unity/BenchmarkPlayer/Assets',
  'engine-godot',
  'tools',
  'scripts',
  'schemas',
  'benchmarks',
  'fixtures',
  'assets/original',
];

// What a wiki page cites as evidence is indexed (Zonio G05, 2026-10-07: a
// citation pointing outside the corpus is a dead link that reports success).
const CITED_EXCLUDE = [/^\.agents\//, /^\.claude\/settings/, /(^|\/)\.env(\.|$)(?!.*example)/];
function citedSourceInputs() {
  const out = new Set();
  for (const p of collectCitedPaths()) {
    if (!p || p.includes('..') || CITED_EXCLUDE.some((re) => re.test(p))) continue;
    if (fs.existsSync(path.join(ROOT, p))) out.add(p);
  }
  return [...out].sort();
}

const TEXT_EXT = new Set([
  '.md', '.txt', '.json', '.cs', '.rs', '.toml', '.py', '.ps1', '.js', '.sh', '.cmd', '.bat',
  '.yml', '.yaml', '.ini', '.svg', '.html',
]);

// Directory names skipped at any depth: engine caches and build output (the
// same set scripts/_hygiene.ps1 forbids in Git), plus tooling noise.
const SKIP_DIR_NAMES = new Set([
  'node_modules', '.git', '.cache', 'Library', 'Temp', 'Logs', 'logs', 'obj', 'bin',
  'UserSettings', 'Builds', 'build_out', 'artifacts', '.godot', '.import', 'Packages',
  'ProjectSettings', '__pycache__', 'target',
]);

const SKIP_REL_PREFIX = [
  // Synthetic fixtures of the knowledge tools' own tests: bait for real queries.
  'tools/knowledge/test/',
  // Archived ChatGPT planning package: superseded by .planning/what-if.
  'agent_pool/archive/',
];
const SKIP_REL_PATTERNS = [
  // Unity serialises a .meta next to every asset; none of it is prose.
  /\.meta$/,
];

// Machine-generated run data. Indexed ONLY when a wiki page cites that exact
// path (in `sources:` or in backticks) - a summary earns its place by being
// pointed at, not by sharing a folder with one that is.
const CITED_ONLY_ARTEFACT = /^benchmarks\/.*\.(json|txt)$/;

const SKIP_REL_FILES = new Set([
  // Retrieval evaluation cases must not live inside the corpus they measure
  // (Zonio measured this twice: a gold query becomes its own top hit).
  'tools/knowledge/rag-evaluation.v1.json',
  // The CS<->EN table lists every domain term in one file; indexed, it would
  // match almost any project question and win (Zonio measured 101.8 vs 39.3).
  'tools/knowledge/cs-en-domain-synonyms.v1.json',
  // The worktree's copy of the pool is frozen at the branch point; ticket status
  // comes from the main checkout's pool (poolStatusChunks). A wiki page citing
  // the pool must not drag the stale copy in (36 chunks, 2026-10-19).
  '.planning/what-if/task_pool.json',
]);

const DEFAULT_MAX_FILE_BYTES = 450000;
const DEFAULT_CHUNK_CHARS = 3600;
const DEFAULT_OVERLAP_CHARS = 350;

const KNOWLEDGE_STATUSES = new Set([
  'implemented', 'historical', 'planned', 'provisional', 'superseded',
]);
const PAGE_TYPES_FOR_METADATA = new Set(['entity', 'concept', 'decision']);

function documentMetadata(relativePath, text) {
  if (path.extname(relativePath).toLowerCase() !== '.md' || !text.startsWith('---')) return {};
  const { data, errors } = parseFrontmatter(text);
  // Wiki stays strict: its schema is enforced by lint-wiki.js and an error is
  // a real finding. Briefs and reviews have no frontmatter and never get here.
  if (errors.length) return {};
  const metadata = {};
  if (KNOWLEDGE_STATUSES.has(data.status)) metadata.status = data.status;
  if (PAGE_TYPES_FOR_METADATA.has(data.type)) metadata.knowledgeType = data.type;
  if (Array.isArray(data.aliases) && data.aliases.length) metadata.aliases = data.aliases;
  if (typeof data.last_verified === 'string') metadata.lastVerified = data.last_verified;
  if (typeof data.source_fingerprint === 'string' && /^sha256:[0-9a-f]{64}$/.test(data.source_fingerprint)) {
    metadata.sourceFingerprint = data.source_fingerprint;
  }
  // A skill's frontmatter `description` is its own statement of the intents it
  // should be selected for - index it on every chunk of that skill (Zonio
  // measured a Czech request missing an English skill without this).
  if (/(^|\/)\.claude\/skills\//.test(relativePath.replace(/\\/g, '/'))
    && typeof data.description === 'string' && data.description.trim()) {
    metadata.purpose = data.description.trim();
  }
  return metadata;
}

function parseArgs(argv) {
  const args = {
    inputs: [],
    outDir: REPORTS_DIR,
    outBase: 'latest-rag-index',
    maxFileBytes: DEFAULT_MAX_FILE_BYTES,
    chunkChars: DEFAULT_CHUNK_CHARS,
    overlapChars: DEFAULT_OVERLAP_CHARS,
    skipped: [],
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--input') args.inputs.push(argv[++i]);
    else if (arg === '--out-dir') args.outDir = path.resolve(ROOT, argv[++i]);
    else if (arg === '--out-base') args.outBase = argv[++i];
    else if (arg === '--max-file-bytes') args.maxFileBytes = Number(argv[++i]);
    else if (arg === '--chunk-chars') args.chunkChars = Number(argv[++i]);
    else if (arg === '--overlap-chars') args.overlapChars = Number(argv[++i]);
    else if (arg === '--help' || arg === '-h') {
      printHelp();
      process.exit(0);
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }

  if (args.inputs.length === 0) args.inputs.push(...DEFAULT_INPUTS, ...citedSourceInputs());
  return args;
}

function printHelp() {
  console.log(`Usage:
  node tools/knowledge/build-rag-index.js [options]

Options:
  --input <path>          Add file or directory input. Can be repeated.
  --out-dir <path>        Output directory. Default: artifacts/knowledge.
  --out-base <name>       Output basename. Default: latest-rag-index.
  --max-file-bytes <n>    Skip larger files. Default: ${DEFAULT_MAX_FILE_BYTES}.
  --chunk-chars <n>       Target max chunk size. Default: ${DEFAULT_CHUNK_CHARS}.
  --overlap-chars <n>     Overlap for forced splits. Default: ${DEFAULT_OVERLAP_CHARS}.
`);
}

function rel(abs) {
  return path.relative(ROOT, abs).replace(/\\/g, '/');
}

function sha256(text) {
  return crypto.createHash('sha256').update(text).digest('hex');
}

// Paths cited by any wiki page: `sources:` frontmatter entries and backticked
// repo-relative paths in the body. Collected once, lazily, because the walk
// needs it on every candidate file. Suffix-matched on a path boundary, because
// prose sometimes cites from the nearest meaningful root.
let citedPathsCache = null;

const CITED_SPAN_RE = /`([A-Za-z0-9_.\-]+(?:\/[A-Za-z0-9_.@\-]+)+\.[A-Za-z0-9]+)`/g;

function collectCitedPaths() {
  if (citedPathsCache) return citedPathsCache;
  const cited = new Set();
  const stack = [path.join(ROOT, 'wiki')].filter((d) => fs.existsSync(d));
  while (stack.length) {
    const dir = stack.pop();
    let entries = [];
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const entry of entries) {
      const abs = path.join(dir, entry.name);
      if (entry.isDirectory()) stack.push(abs);
      else if (entry.name.toLowerCase().endsWith('.md')) {
        let text = '';
        try {
          text = fs.readFileSync(abs, 'utf8');
        } catch {
          continue;
        }
        if (text.startsWith('---')) {
          const { data } = parseFrontmatter(text);
          for (const s of Array.isArray(data && data.sources) ? data.sources : []) {
            cited.add(String(s).replace(/\\/g, '/').replace(/\/+$/, ''));
          }
        }
        for (const m of text.matchAll(CITED_SPAN_RE)) cited.add(m[1].replace(/\\/g, '/'));
      }
    }
  }
  citedPathsCache = cited;
  return cited;
}

function isCitedArtefact(normalizedPath) {
  const cited = collectCitedPaths();
  if (cited.has(normalizedPath)) return true;
  for (const c of cited) {
    // Suffix match on a path boundary, so `.../runs/x/report.json` is not
    // satisfied by a citation of `.../other_runs/x/report.json`.
    if (normalizedPath.endsWith(c) && (normalizedPath.length === c.length
      || normalizedPath[normalizedPath.length - c.length - 1] === '/')) return true;
  }
  return false;
}

function shouldSkipRel(relativePath) {
  const normalized = relativePath.replace(/\\/g, '/');
  if (normalized.split('/').some((segment) => SKIP_DIR_NAMES.has(segment))) return true;
  if (SKIP_REL_FILES.has(normalized)) return true;
  if (SKIP_REL_PATTERNS.some((pattern) => pattern.test(normalized))) return true;
  if (SKIP_REL_PREFIX.some((prefix) => normalized === prefix.slice(0, -1) || normalized.startsWith(prefix))) return true;
  if (CITED_ONLY_ARTEFACT.test(normalized) && !isCitedArtefact(normalized)) return true;
  return false;
}

function isTextFile(abs) {
  const base = path.basename(abs).toLowerCase();
  if (base === '.env') return false;
  if (base.endsWith('.env')) return false;
  if (base === '.env.example') return true;
  return TEXT_EXT.has(path.extname(abs).toLowerCase());
}

function walk(inputPath, args) {
  const abs = path.isAbsolute(inputPath) ? inputPath : path.join(ROOT, inputPath);
  if (!fs.existsSync(abs)) return [];
  const st = fs.statSync(abs);
  const relativePath = rel(abs);
  if (shouldSkipRel(relativePath)) return [];

  if (st.isFile()) {
    if (!isTextFile(abs)) return [];
    if (st.size > args.maxFileBytes) {
      args.skipped.push({ path: relativePath, reason: 'max-file-bytes', bytes: st.size });
      return [];
    }
    return [abs];
  }

  const out = [];
  for (const entry of fs.readdirSync(abs, { withFileTypes: true })) {
    const child = path.join(abs, entry.name);
    const childRel = rel(child);
    if (entry.isDirectory()) {
      if (!SKIP_DIR_NAMES.has(entry.name) && !SKIP_DIR_NAMES.has(childRel) && !shouldSkipRel(childRel)) {
        out.push(...walk(child, args));
      }
    } else if (entry.isFile()) {
      const childSt = fs.statSync(child);
      if (isTextFile(child) && !shouldSkipRel(childRel)) {
        if (childSt.size <= args.maxFileBytes) out.push(child);
        else args.skipped.push({ path: childRel, reason: 'max-file-bytes', bytes: childSt.size });
      }
    }
  }
  return out;
}

function readUtf8(abs) {
  return fs.readFileSync(abs, 'utf8').replace(/^\uFEFF/, '');
}

function stripHtml(text) {
  return text
    .replace(/<script[\s\S]*?<\/script>/gi, ' ')
    .replace(/<style[\s\S]*?<\/style>/gi, ' ')
    .replace(/<\/(h[1-6]|p|li|tr|section|article|div)>/gi, '\n')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/\n{3,}/g, '\n\n');
}

function normalizeContent(abs, raw) {
  const ext = path.extname(abs).toLowerCase();
  if (ext === '.html' || ext === '.svg') return stripHtml(raw);
  return raw.replace(/\r\n/g, '\n');
}

function lineNumberAt(text, offset) {
  if (offset <= 0) return 1;
  return text.slice(0, offset).split('\n').length;
}

function sectionTitleAt(text, start) {
  const before = text.slice(0, start);
  const matches = [...before.matchAll(/^(#{1,6})\s+(.+)$/gm)];
  if (matches.length === 0) return null;
  return matches[matches.length - 1][2].trim();
}

// The nearest heading alone loses the context a chunk sits in: a piece under
// `### Recovery` carries "Recovery" and nothing about the page or the section
// it belongs to, so a query naming the topic and the detail together only
// matches half of it. Walk the ancestors by level to rebuild the trail
// (`# Page > ## Section > ### Recovery`) and index that instead - one line of
// context per chunk, no extra chunks, nothing to store at query time.
function headingPathAt(text, start) {
  const before = text.slice(0, start);
  const matches = [...before.matchAll(/^(#{1,6})\s+(.+)$/gm)];
  if (matches.length === 0) return null;
  const stack = [];
  for (const match of matches) {
    const level = match[1].length;
    stack.length = Math.max(0, level - 1);
    stack[level - 1] = match[2].trim();
  }
  const path_ = stack.filter(Boolean);
  return path_.length ? path_.join(' > ') : null;
}

function splitLargePiece(piece, args) {
  if (piece.text.length <= args.chunkChars) return [piece];
  const out = [];
  let cursor = 0;
  while (cursor < piece.text.length) {
    const end = Math.min(piece.text.length, cursor + args.chunkChars);
    out.push({
      start: piece.start + cursor,
      end: piece.start + end,
      text: piece.text.slice(cursor, end),
    });
    if (end === piece.text.length) break;
    cursor = Math.max(end - args.overlapChars, cursor + 1);
  }
  return out;
}

// Blocks excluded from the index: `<!-- rag-skip:start -->` ... `<!-- rag-skip:end -->`.
// For prose that quotes evaluation queries VERBATIM - such text becomes the
// query's own competitor (measured in Zonio 2026-07-30: a suite dropped 32/32 ->
// 31/32 with no retrieval change). Masked with offsets preserved, so line
// numbers stay true. Use only on bait, never on content someone should find.
const RAG_SKIP_BLOCK_RE = /<!--\s*rag-skip:start\s*-->([\s\S]*?)<!--\s*rag-skip:end\s*-->/g;

function maskSkippedBlocks(text) {
  if (!text.includes('rag-skip:start')) return text;
  return text.replace(RAG_SKIP_BLOCK_RE, (match) => match.replace(/[^\n]/g, ' '));
}

function chunkDocument(abs, rawText, args) {
  // `rawText` zůstává zdrojem pro hash a nadpisy; `text` je maskovaná varianta
  // se shodnými offsety, ze které se skládají chunky.
  const text = maskSkippedBlocks(rawText);
  const chunks = [];
  const headingMatches = [...text.matchAll(/^(#{1,6})\s+(.+)$/gm)];
  const headingStarts = headingMatches.map((match) => match.index);
  const boundaries = headingStarts.length
    ? [...new Set([0, ...headingStarts, text.length])].sort((a, b) => a - b)
    : [0, text.length];

  const pieces = [];
  if (headingMatches.length) {
    for (let i = 0; i < boundaries.length - 1; i += 1) {
      const start = boundaries[i];
      const end = boundaries[i + 1];
      const pieceText = text.slice(start, end).trim();
      if (pieceText) pieces.push({ start, end, text: pieceText });
    }
  } else {
    const blocks = text.split(/(\n{2,})/g);
    let cursor = 0;
    let current = { start: 0, end: 0, text: '' };
    for (const block of blocks) {
      const blockStart = cursor;
      const blockEnd = blockStart + block.length;
      cursor = blockEnd;
      if (/^\n{2,}$/.test(block)) continue;
      if (!block.trim()) continue;
      if (current.text && current.text.length + block.length + 2 > args.chunkChars) {
        pieces.push(current);
        current = { start: blockStart, end: blockEnd, text: block };
      } else {
        if (!current.text) current.start = blockStart;
        current.end = blockEnd;
        current.text = current.text ? `${current.text}\n\n${block}` : block;
      }
    }
    if (current.text) pieces.push(current);
  }

  const relPath = rel(abs);
  // Hash i metadata čti ze SUROVÉHO souboru — hash slouží k detekci driftu
  // zdroje, takže maskování by ho rozešlo se skutečným obsahem na disku.
  const sourceHash = sha256(rawText);
  const metadata = documentMetadata(relPath, rawText);
  let localIndex = 0;
  for (const piece of pieces.flatMap((item) => splitLargePiece(item, args))) {
    const cleanText = piece.text.trim();
    if (cleanText.length < 40) continue;
    const startLine = lineNumberAt(text, piece.start);
    const endLine = lineNumberAt(text, piece.end);
    const section = sectionTitleAt(text, piece.start) || path.basename(abs);
    const headingPath = headingPathAt(text, piece.start);
    const chunkId = `${relPath}#${startLine}-${endLine}-${localIndex}`;
    chunks.push({
      id: chunkId,
      sourcePath: relPath,
      sourceHash,
      kind: path.extname(abs).slice(1).toLowerCase() || 'text',
      section,
      ...(headingPath && headingPath !== section ? { headingPath } : {}),
      startLine,
      endLine,
      chars: cleanText.length,
      text: cleanText,
      ...metadata,
    });
    localIndex += 1;
  }
  return chunks;
}

// Lightweight knowledge-graph layer over the wiki (GraphRAG-lite): one node
// per wiki page (slug = basename), edges = [[wikilinks]] between pages and
// `sources:` frontmatter pointers into the code/doc tree. rag-query.js uses
// it for alias boosts and 1-hop neighbor expansion; older indexes without a
// `graph` section keep working (query degrades to plain BM25).
const WIKILINK_RE = /\[\[([^\][|#]+)/g;

function buildWikiGraph(files) {
  const pages = {};
  for (const abs of files) {
    const relativePath = rel(abs);
    if (!/^wiki\//.test(relativePath) || path.extname(abs).toLowerCase() !== '.md') continue;
    const text = readUtf8(abs);
    const { data } = text.startsWith('---') ? parseFrontmatter(text) : { data: {} };
    const links = [...new Set([...text.matchAll(WIKILINK_RE)].map((m) => m[1].trim()).filter(Boolean))];
    pages[path.basename(abs, '.md')] = {
      path: relativePath,
      type: typeof data?.type === 'string' ? data.type : null,
      aliases: Array.isArray(data?.aliases) ? data.aliases.map(String) : [],
      links,
      sources: Array.isArray(data?.sources)
        ? data.sources.map((s) => String(s).replace(/\\/g, '/').replace(/\/+$/, ''))
        : [],
    };
  }
  return { schema: 'whatif-rag-graph.v1', pages };
}

function asciiFold(text) {
  return text
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();
}

function tokenize(text) {
  return asciiFold(text)
    .replace(/[^a-z0-9_./:-]+/g, ' ')
    .split(/\s+/)
    .filter((token) => token.length >= 2 && token.length <= 80);
}

function buildIndex(chunks) {
  const postings = Object.create(null);
  const docLengths = [];
  const documentFrequency = Object.create(null);

  chunks.forEach((chunk, idx) => {
    // The heading trail replaces the doubled nearest-heading: it contains that
    // heading anyway, plus its ancestors, at the same weight.
    const heading = chunk.headingPath || chunk.section;
    // `purpose` (a skill's declared intents) is added ONCE, not doubled: it is
    // long, and doubling it would inflate the document length enough for BM25's
    // normalisation to punish the very chunks it is meant to help.
    const purpose = chunk.purpose ? ` ${chunk.purpose}` : '';
    const weighted = `${chunk.sourcePath} ${heading} ${heading}${purpose} ${chunk.text}`;
    const terms = tokenize(weighted);
    docLengths[idx] = terms.length;
    const tf = new Map();
    for (const term of terms) tf.set(term, (tf.get(term) || 0) + 1);
    for (const [term, count] of tf.entries()) {
      if (!postings[term]) postings[term] = [];
      postings[term].push([idx, count]);
      documentFrequency[term] = (documentFrequency[term] || 0) + 1;
    }
  });

  const avgDocLength = docLengths.length
    ? docLengths.reduce((sum, len) => sum + len, 0) / docLengths.length
    : 0;

  return {
    schema: 'whatif-rag-index.v1',
    generatedAt: new Date().toISOString(),
    root: '.',
    tokenizer: 'ascii-fold lowercase bm25-lite',
    stats: {
      chunks: chunks.length,
      sources: new Set(chunks.map((chunk) => chunk.sourcePath)).size,
      terms: Object.keys(postings).length,
      avgDocLength,
      skipped: 0,
    },
    chunks,
    index: {
      docLengths,
      documentFrequency,
      postings,
    },
  };
}

function renderMarkdown(ragIndex, outputs) {
  const topSources = [...new Set(ragIndex.chunks.map((chunk) => chunk.sourcePath))]
    .slice(0, 80)
    .map((source) => `- \`${source}\``)
    .join('\n');

  return `# What If RAG index

Generated: ${ragIndex.generatedAt}
Schema: \`${ragIndex.schema}\`

## Outputs

- Index JSON: \`${rel(outputs.jsonPath)}\`
- Chunks JSONL: \`${rel(outputs.chunksPath)}\`

## Summary

- Sources: ${ragIndex.stats.sources}
- Chunks: ${ragIndex.stats.chunks}
- Terms: ${ragIndex.stats.terms}
- Average document length: ${ragIndex.stats.avgDocLength.toFixed(1)}

## Query

\`\`\`powershell
node tools\\knowledge\\rag-query.js --query "<dotaz>" --top 8
node tools\\knowledge\\code-graph-cs.js <Symbol>
\`\`\`

## Source sample

${topSources}
`;
}

function writeOutputs(ragIndex, args) {
  fs.mkdirSync(args.outDir, { recursive: true });
  const jsonPath = path.join(args.outDir, `${args.outBase}.json`);
  const chunksPath = path.join(args.outDir, `${args.outBase}.chunks.jsonl`);
  const mdPath = path.join(args.outDir, `${args.outBase}.md`);
  fs.writeFileSync(jsonPath, JSON.stringify(ragIndex) + '\n', 'utf8');
  fs.writeFileSync(chunksPath, ragIndex.chunks.map((chunk) => JSON.stringify(chunk)).join('\n') + '\n', 'utf8');
  fs.writeFileSync(mdPath, renderMarkdown(ragIndex, { jsonPath, chunksPath }) + '\n', 'utf8');
  return { jsonPath, chunksPath, mdPath };
}

// Code half of GraphRAG-lite (code-graph-cs.js). Built here so the code graph
// can never be older than the chunks it labels. A C# chunk is cut by blank
// lines, not by method, and would otherwise carry only its file name; it now
// carries the methods it overlaps, as heading trail and `symbols`, so a query
// naming a method can match the chunk that holds its body.
function attachCodeGraph(chunks) {
  let graph;
  try {
    graph = buildCodeGraph();
    writeGraph(graph);
  } catch (error) {
    console.error(`WARNING: code graph not built (${error.message}); RAG index continues without it.`);
    return null;
  }
  let labelled = 0;
  for (const chunk of chunks) {
    if (!graph.files[chunk.sourcePath]) continue;
    const names = [...new Set(functionsInRange(graph, chunk.sourcePath, chunk.startLine, chunk.endLine)
      .map((s) => s.qname))];
    if (!names.length) continue;
    chunk.symbols = names.slice(0, 8);
    chunk.headingPath = `${chunk.section} > ${names.slice(0, 4).join(', ')}`;
    labelled += 1;
  }
  const methods = graph.symbols.filter((s) => s.kind === 'method' || s.kind === 'constructor').length;
  if (methods === 0) {
    // A graph with zero methods over a repo that has C# is a broken scanner,
    // not an empty project - say so loudly instead of indexing without labels.
    console.error('WARNING: code graph found 0 methods; C# labels are missing from this index.');
  }
  return { methods, edges: graph.edges.length, files: Object.keys(graph.files).length, labelledChunks: labelled };
}

// Live ticket status. task_pool.json is canonical and pool.py mutates it in the
// main checkout only; a worktree's tasks/*.md keep the "Stav:" line of their
// branch point. One small chunk per ticket, read from the main checkout's pool,
// under the virtual path `main:.planning/what-if/task_pool.json` so a hit says
// where the status came from. Per ticket, not the rendered TASKS.md table: as
// one 7 kB table chunk it lost "RG-076 stav" to the stale tasks/RG-076.md
// (measured 2026-10-19). The pool's hash is the freshness key rag-query.js
// rebuilds on.
const POOL_REL = '.planning/what-if/task_pool.json';

function poolStatusSource() {
  const main = mainCheckoutRoot(ROOT);
  const abs = path.join(main || ROOT, POOL_REL);
  if (!fs.existsSync(abs)) return null;
  const raw = readUtf8(abs);
  return { abs, raw, virtualPath: main ? `main:${POOL_REL}` : POOL_REL, hash: sha256(raw) };
}

function poolStatusChunks(status) {
  if (!status) return [];
  let pool;
  try {
    pool = JSON.parse(status.raw);
  } catch (error) {
    console.error(`WARNING: ${status.virtualPath} is not valid JSON (${error.message}); no live ticket status indexed.`);
    return [];
  }
  return (pool.tasks || []).map((t, i) => {
    const d = t.dispatch || {};
    const text = [
      `${t.id} — ${t.title}`,
      `Stav (status, živě z main task_pool.json): ${t.status}`,
      `Fáze ${t.phase}, priorita ${t.priority}, agent ${t.preferred_agent}/${t.preferred_model}`,
      `Závislosti: ${(t.dependencies || []).join(', ') || '—'}`,
      d.job_id ? `Dispatch: ${d.status} (${d.job_id})` : null,
      t.reviewer ? `Review: ${t.reviewer} ${t.reviewed_at || ''}` : null,
      (t.evidence || []).length ? `Evidence: ${t.evidence.join(', ')}` : null,
      `Ticket: .planning/what-if/tasks/${t.id}.md, brief: agent_pool/shared/briefs/${t.id}.md`,
    ].filter(Boolean).join('\n');
    return {
      id: `${status.virtualPath}#${t.id}-${i}`,
      sourcePath: status.virtualPath,
      sourceHash: status.hash,
      kind: 'json',
      section: `${t.id} status`,
      startLine: 1,
      endLine: 1,
      chars: text.length,
      text,
    };
  });
}

// RG-125: runtime scene graph (tools/knowledge/runtime-graph.py). One chunk per runtime pattern under the virtual path
// `runtime:<label>:<pattern>`; the edges (C# files, wiki pages, creator sites) are also kept in `ragIndex.runtime` so that
// rag-query.js can walk them. No snapshot graph under artifacts/knowledge/runtime/ = nothing added, the index is unchanged.
const RUNTIME_DIR = path.join(REPORTS_DIR, 'runtime');

function runtimeGraphSource() {
  if (!fs.existsSync(RUNTIME_DIR)) return null;
  const files = fs.readdirSync(RUNTIME_DIR).filter((f) => f.endsWith('.graph.json')).sort();
  if (!files.length) return null;
  const graphs = [];
  const parts = [];
  for (const f of files) {
    const raw = readUtf8(path.join(RUNTIME_DIR, f));
    parts.push(`${f}:${sha256(raw)}`);
    try {
      const g = JSON.parse(raw);
      if (g && g.schema === 'whatif-runtime-graph.v1' && Array.isArray(g.pattern_table)) graphs.push(g);
    } catch (error) {
      console.error(`WARNING: ${f} is not valid JSON (${error.message}); skipped.`);
    }
  }
  return { graphs, hash: sha256(parts.join('\n')) };
}

function runtimeChunks(src) {
  const chunks = [];
  if (!src) return { chunks, runtime: null };
  const runtime = { schema: 'whatif-runtime-index.v1', labels: {} };
  for (const g of src.graphs) {
    const entry = { map: g.map, nodes: g.nodes, patterns: {} };
    runtime.labels[g.label] = entry;
    for (const p of g.pattern_table) {
      const comps = p.components.map((c) => `${c.type} (${c.resolution})`).join(', ') || 'none';
      const creators = p.creators.map((c) => `${c.file}:${c.line} [${c.evidence}]`);
      const lines = [
        `Runtime scene object pattern \`${p.pattern}\` — ${p.count} instance(s) in the live snapshot \`${g.label}\` (map ${g.map}, ${g.nodes} nodes).`,
        `Examples: ${p.examples.join('; ')}`,
        `Parent pattern(s): ${p.parents.join(', ') || 'scene root'}`,
        `Components: ${comps}`,
        creators.length ? `Built by: ${creators.join('; ')}` : 'Built by: no creator call site found (lexical scan)',
        p.related && p.related.length ? `Related C# by name: ${p.related.join(', ')}` : null,
        p.pages.length ? `Documented in wiki: ${p.pages.map((x) => `[[${x}]]`).join(', ')}` : null,
        'Source: runtime snapshot of the agent port (RG-124) joined to the C# code graph; navigation, not proof.',
      ].filter(Boolean);
      const text = lines.join('\n');
      const virtualPath = `runtime:${g.label}:${p.pattern}`;
      chunks.push({
        id: `${virtualPath}#0`,
        sourcePath: virtualPath,
        sourceHash: sha256(text),
        kind: 'runtime',
        section: `runtime pattern ${p.pattern}`,
        startLine: 1,
        endLine: lines.length,
        chars: text.length,
        text,
      });
      entry.patterns[p.pattern] = {
        count: p.count,
        files: [...new Set([...p.creators.map((c) => c.file), ...(p.related || []), ...p.components.flatMap((c) => c.files || [])])],
        creators: p.creators.map((c) => ({ file: c.file, line: c.line, evidence: c.evidence })),
        related: p.related || [],
        pages: p.pages,
      };
    }
  }
  return { chunks, runtime };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const files = [...new Set(args.inputs.flatMap((input) => walk(input, args)))].sort();
  const chunks = files.flatMap((file) => chunkDocument(file, normalizeContent(file, readUtf8(file)), args));
  const poolStatus = poolStatusSource();
  chunks.push(...poolStatusChunks(poolStatus));
  const runtimeSrc = runtimeGraphSource();
  const rt = runtimeChunks(runtimeSrc);
  chunks.push(...rt.chunks);
  const codeGraphStats = attachCodeGraph(chunks);
  const ragIndex = buildIndex(chunks);
  ragIndex.graph = buildWikiGraph(files);
  ragIndex.stats.graphPages = Object.keys(ragIndex.graph.pages).length;
  if (codeGraphStats) ragIndex.stats.codeGraph = codeGraphStats;
  ragIndex.inputs = args.inputs;
  if (rt.runtime) { ragIndex.runtime = rt.runtime; ragIndex.stats.runtimePatterns = rt.chunks.length; }
  ragIndex.freshness = {
    head: gitHead(ROOT),
    poolStatusPath: poolStatus ? poolStatus.virtualPath : null,
    poolStatusHash: poolStatus ? poolStatus.hash : null,
    runtimeHash: runtimeSrc ? runtimeSrc.hash : null,
  };
  ragIndex.skipped = args.skipped;
  ragIndex.stats.skipped = args.skipped.length;
  ragIndex.limits = {
    maxFileBytes: args.maxFileBytes,
    chunkChars: args.chunkChars,
    overlapChars: args.overlapChars,
  };
  const outputs = writeOutputs(ragIndex, args);
  console.log(JSON.stringify({
    summary: ragIndex.stats,
    outputs: Object.fromEntries(Object.entries(outputs).map(([key, value]) => [key, rel(value)])),
  }, null, 2));
}

if (require.main === module) main();

module.exports = {
  documentMetadata, chunkDocument, buildWikiGraph, maskSkippedBlocks,
  shouldSkipRel, citedSourceInputs, collectCitedPaths, DEFAULT_INPUTS, poolStatusSource, runtimeGraphSource,
};
