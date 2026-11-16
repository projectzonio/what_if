#!/usr/bin/env node
'use strict';

// Code half of GraphRAG-lite for What If: a lexical C# graph.
//
// Pattern ported from zonio_app tools/rag/code-graph.js (C/C++ + JS, 2026-10-07)
// on 2026-10-11; the scanner itself is new because What If is C#. Why it
// exists is the same measured Zonio reason: the RAG index cuts code into text
// chunks with no function name and no edges, so "who calls Step" returns prose
// and file-level neighbours, never the definition or a caller.
//
// What it extracts: namespaces, types (class/struct/interface/enum/record) with
// base lists, methods and constructors with line ranges, and edges:
//   call      Foo(...) / x.Foo(...) inside a method body -> every method named Foo
//   new       new Bar(...)                               -> type Bar
//   inherits  class A : B, IC                            -> B, IC
//   type-ref  any other use of a project type name (fields, GetComponent<T>,
//             typeof(T), static calls T.Foo()) -> that type; one edge per user
//
// Known limits (lexical, by design): no overload or receiver-type resolution
// (x.Step() links to EVERY method named Step - the output says how many),
// properties/events/lambdas/local functions are not symbols, generics are
// stripped. Three caller classes are invisible to a lexical scan and the CLI
// says so instead of printing "no callers":
//   - Unity/Godot engine callbacks (Update, FixedUpdate, LateUpdate, OnGUI, ...)
//   - reflection / plugin loading (AssemblyLoadContext, Activator, Assembly.Load)
//   - delegates and events passed as values.
// Every edge carries file:line - treat an edge as navigation, not proof.

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { findProjectRoot, reportsDir } = require('./_lib/project-root');

const ROOT = findProjectRoot(__dirname);
const DEFAULT_OUT = path.join(reportsDir(ROOT), 'latest-code-graph.json');
const SCHEMA = 'whatif-code-graph-cs.v1';

// Source roots. engine-*/ projects are walked whole; their caches are skipped
// by SKIP_DIRS (same list the hygiene check forbids in Git).
const DEFAULT_ROOTS = ['src', 'tests', 'engine-unity', 'engine-godot', 'tools'];
const SKIP_DIRS = new Set([
  '.git', 'node_modules', 'bin', 'obj', 'library', 'temp', 'logs', 'usersettings',
  'builds', 'build_out', 'artifacts', '.godot', '.import', '.cache', 'packages',
]);
// Test fixtures of this tool: synthetic C# that mimics real shapes would be
// bait for real queries (the same reason Zonio excludes tools/test/fixtures/).
const SKIP_REL_PREFIX = ['tools/knowledge/test/fixtures/'];

const ENGINE_CALLBACKS = new Set([
  'Awake', 'Start', 'Update', 'FixedUpdate', 'LateUpdate', 'OnEnable', 'OnDisable',
  'OnDestroy', 'OnGUI', 'OnApplicationQuit', 'OnValidate', 'Reset', 'OnCollisionEnter',
  'OnCollisionStay', 'OnCollisionExit', 'OnTriggerEnter', 'OnTriggerStay', 'OnTriggerExit',
  'OnRenderObject', 'OnPostRender', 'OnPreRender', 'OnDrawGizmos', 'OnApplicationFocus',
  '_Ready', '_Process', '_PhysicsProcess', '_Input', '_EnterTree', '_ExitTree',
  'Main',
]);
const NOT_CALLS = new Set([
  'if', 'for', 'foreach', 'while', 'switch', 'catch', 'using', 'lock', 'return', 'new',
  'typeof', 'nameof', 'sizeof', 'default', 'checked', 'unchecked', 'fixed', 'when',
  'base', 'this', 'throw', 'await', 'stackalloc', 'is', 'as', 'in', 'out', 'ref',
]);
const MODIFIERS = new Set([
  'public', 'private', 'protected', 'internal', 'static', 'virtual', 'override',
  'abstract', 'sealed', 'async', 'extern', 'unsafe', 'new', 'partial', 'readonly',
]);
const TYPE_KEYWORDS = /\b(class|struct|interface|enum|record)\s+([A-Za-z_]\w*)/g;

function rel(abs) {
  return path.relative(ROOT, abs).replace(/\\/g, '/');
}

function sha256(text) {
  return crypto.createHash('sha256').update(text).digest('hex');
}

// Replace comments and string/char literals with spaces, keeping every '\n'
// so offsets and line numbers stay true to the file.
function maskCode(src) {
  const out = src.split('');
  const blank = (from, to) => {
    for (let k = from; k < to; k += 1) if (out[k] !== '\n') out[k] = ' ';
  };
  let i = 0;
  while (i < src.length) {
    const c = src[i];
    const n = src[i + 1];
    if (c === '/' && n === '/') {
      const end = src.indexOf('\n', i);
      const stop = end === -1 ? src.length : end;
      blank(i, stop);
      i = stop;
    } else if (c === '/' && n === '*') {
      const end = src.indexOf('*/', i + 2);
      const stop = end === -1 ? src.length : end + 2;
      blank(i, stop);
      i = stop;
    } else if (c === '"' || ((c === '@' || c === '$') && (n === '"' || ((n === '@' || n === '$') && src[i + 2] === '"')))) {
      let j = i;
      let verbatim = false;
      while (src[j] !== '"') {
        if (src[j] === '@') verbatim = true;
        j += 1;
      }
      if (src.startsWith('"""', j)) {
        const end = src.indexOf('"""', j + 3);
        const stop = end === -1 ? src.length : end + 3;
        blank(i, stop);
        i = stop;
        continue;
      }
      j += 1;
      while (j < src.length) {
        if (verbatim && src[j] === '"' && src[j + 1] === '"') { j += 2; continue; }
        if (!verbatim && src[j] === '\\') { j += 2; continue; }
        if (src[j] === '"') { j += 1; break; }
        if (!verbatim && src[j] === '\n') break;
        j += 1;
      }
      blank(i, j);
      i = j;
    } else if (c === '\'') {
      let j = i + 1;
      while (j < src.length && src[j] !== '\'' && src[j] !== '\n') j += src[j] === '\\' ? 2 : 1;
      blank(i, Math.min(j + 1, src.length));
      i = j + 1;
    } else {
      i += 1;
    }
  }
  return out.join('');
}

function lineStarts(text) {
  const starts = [0];
  for (let i = 0; i < text.length; i += 1) if (text[i] === '\n') starts.push(i + 1);
  return starts;
}

function lineAt(starts, offset) {
  let lo = 0;
  let hi = starts.length - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (starts[mid] <= offset) lo = mid; else hi = mid - 1;
  }
  return lo + 1;
}

function matchBrace(text, open) {
  let depth = 0;
  for (let i = open; i < text.length; i += 1) {
    if (text[i] === '{') depth += 1;
    else if (text[i] === '}') {
      depth -= 1;
      if (depth === 0) return i;
    }
  }
  return text.length - 1;
}

// The statement head that precedes `pos`: text back to the previous ; { } or
// attribute close at the same nesting level.
function headBefore(text, pos) {
  let i = pos - 1;
  let paren = 0;
  let angle = 0;
  while (i >= 0) {
    const c = text[i];
    if (c === ')') paren += 1;
    else if (c === '(') paren -= 1;
    else if (c === '>') angle += 1;
    else if (c === '<') angle -= 1;
    else if (paren <= 0 && angle <= 0 && (c === ';' || c === '{' || c === '}' || c === ']')) break;
    i -= 1;
  }
  return { start: i + 1, text: text.slice(i + 1, pos) };
}

function stripGenerics(s) {
  let prev;
  let cur = s;
  do {
    prev = cur;
    cur = cur.replace(/<[^<>]*>/g, '');
  } while (cur !== prev);
  return cur;
}

// Method / constructor head: `[mods] Type Name(params) [: base(..)] [where ..]`.
function parseMethodHead(head, typeName) {
  const flat = stripGenerics(head.replace(/\s+/g, ' ')).trim();
  const m = flat.match(/^(.*?)([A-Za-z_]\w*)\s*\(([^()]*(?:\([^()]*\)[^()]*)*)\)\s*(?::\s*(?:base|this)\s*\(.*\))?\s*(?:where\s+.*)?$/);
  if (!m) return null;
  const before = m[1].trim().split(/\s+/).filter(Boolean);
  const name = m[2];
  if (NOT_CALLS.has(name) || /^(get|set|init|add|remove)$/.test(name)) return null;
  const words = before.filter((w) => !MODIFIERS.has(w));
  if (words.some((w) => /^(class|struct|interface|enum|record|namespace|delegate|event|operator)$/.test(w))) return null;
  if (words.includes('=') || /[=+\-*/%!&|?]/.test(before.join(' '))) return null;
  const isCtor = name === typeName && words.length === 0;
  if (!isCtor && words.length < 1) return null;
  return { name, kind: isCtor ? 'constructor' : 'method', params: m[3].trim() };
}

function scanFile(abs) {
  const raw = fs.readFileSync(abs, 'utf8').replace(/^﻿/, '');
  const text = maskCode(raw.replace(/\r\n/g, '\n'));
  const starts = lineStarts(text);
  const file = rel(abs);
  const symbols = [];
  const edges = [];
  const typeRefs = [];

  let namespace = '';
  const fileNs = text.match(/^\s*namespace\s+([\w.]+)\s*;/m);
  if (fileNs) namespace = fileNs[1];

  // Namespaces with blocks, then types; each type body is scanned for members.
  const nsBlocks = [];
  for (const m of text.matchAll(/\bnamespace\s+([\w.]+)\s*\{/g)) {
    const open = m.index + m[0].length - 1;
    nsBlocks.push({ name: m[1], open, close: matchBrace(text, open) });
  }
  const nsAt = (offset) => {
    const hit = nsBlocks.filter((b) => b.open < offset && offset < b.close).pop();
    return hit ? hit.name : namespace;
  };

  const types = [];
  for (const m of text.matchAll(TYPE_KEYWORDS)) {
    const after = text.slice(m.index + m[0].length);
    const bodyRel = after.search(/[{;]/);
    if (bodyRel === -1 || after[bodyRel] === ';') continue;
    const open = m.index + m[0].length + bodyRel;
    const header = stripGenerics(after.slice(0, bodyRel));
    const bases = (header.match(/:\s*([^{]+?)(?:\bwhere\b|$)/) || [, ''])[1]
      .split(',').map((b) => b.trim().split(/[\s(]/)[0]).filter((b) => /^[A-Za-z_][\w.]*$/.test(b));
    types.push({ kind: m[1], name: m[2], start: m.index, open, close: matchBrace(text, open), bases });
  }
  const outerTypes = (t) => types.filter((o) => o !== t && o.open < t.start && t.start < o.close);

  for (const t of types) {
    const ns = nsAt(t.start);
    const qualifier = [...outerTypes(t).map((o) => o.name), t.name].join('.');
    const typeQname = ns ? `${ns}.${qualifier}` : qualifier;
    const typeId = `${file}#type:${typeQname}`;
    symbols.push({
      id: typeId, kind: t.kind, name: t.name, qname: typeQname, file,
      line: lineAt(starts, t.start), endLine: lineAt(starts, t.close),
    });
    for (const b of t.bases) edges.push({ from: typeId, toName: b.split('.').pop(), type: 'inherits', file, line: lineAt(starts, t.start) });
    typeRefs.push({ from: typeId, self: t.name, start: t.open, end: t.close });
    if (t.kind === 'enum') continue;

    // Members directly in this type's body (depth 1 relative to it).
    let depth = 0;
    for (let i = t.open + 1; i < t.close; i += 1) {
      const c = text[i];
      if (c === '{') {
        if (depth === 0) {
          const head = headBefore(text, i);
          const parsed = head.text.includes('(') ? parseMethodHead(head.text, t.name) : null;
          const nested = types.find((n) => n.open === i);
          if (parsed && !nested) {
            const close = matchBrace(text, i);
            addMethod(parsed, head.start, i, close);
            i = close;
            continue;
          }
        }
        depth += 1;
      } else if (c === '}') {
        depth -= 1;
      } else if (depth === 0 && c === '=' && text[i + 1] === '>') {
        const head = headBefore(text, i);
        const parsed = head.text.includes('(') ? parseMethodHead(head.text, t.name) : null;
        if (parsed) {
          const end = text.indexOf(';', i);
          addMethod(parsed, head.start, i, end === -1 ? t.close : end);
          i = end === -1 ? t.close : end;
        }
      }
    }

    function addMethod(parsed, headStart, bodyStart, bodyEnd) {
      const firstWord = text.slice(headStart).search(/\S/);
      const declAt = headStart + Math.max(0, firstWord);
      const qname = `${typeQname}.${parsed.name}`;
      const id = `${file}#${parsed.kind}:${qname}@${lineAt(starts, declAt)}`;
      symbols.push({
        id, kind: parsed.kind, name: parsed.name, qname, file,
        line: lineAt(starts, declAt), endLine: lineAt(starts, bodyEnd),
        params: parsed.params.replace(/\s+/g, ' ').slice(0, 160),
        ...(ENGINE_CALLBACKS.has(parsed.name) ? { engineEntry: true } : {}),
      });
      const body = text.slice(bodyStart, bodyEnd + 1);
      typeRefs.push({ from: id, self: t.name, start: bodyStart, end: bodyEnd });
      for (const call of body.matchAll(/\bnew\s+([A-Za-z_][\w.]*)\s*(?:<[^;{}()]*>)?\s*[({]/g)) {
        edges.push({ from: id, toName: call[1].split('.').pop(), type: 'new', file, line: lineAt(starts, bodyStart + call.index) });
      }
      for (const call of body.matchAll(/(?:^|[^\w.])((?:[A-Za-z_]\w*\s*\.\s*)*)([A-Za-z_]\w*)\s*(?:<[\w\s,.<>[\]?]*>)?\s*\(/g)) {
        const name = call[2];
        if (NOT_CALLS.has(name)) continue;
        const prefix = body.slice(Math.max(0, call.index - 4), call.index + 1);
        if (/new\s*$/.test(prefix)) continue;
        edges.push({
          from: id, toName: name, type: 'call', file,
          line: lineAt(starts, bodyStart + call.index + call[0].indexOf(name)),
          ...(call[1] ? { receiver: call[1].replace(/\s+/g, '').replace(/\.$/, '') } : {}),
        });
      }
    }
  }
  // Candidate type references: capitalised identifiers per range, first line
  // only. Resolved against the project's type names in buildCodeGraph().
  const refs = typeRefs.map((r) => {
    const seen = new Map();
    const body = text.slice(r.start, r.end + 1);
    for (const m of body.matchAll(/(?<![\w.])([A-Z][A-Za-z0-9_]*)/g)) {
      if (m[1] !== r.self && !seen.has(m[1])) seen.set(m[1], lineAt(starts, r.start + m.index));
    }
    return { from: r.from, names: [...seen.entries()] };
  });
  return { file, sha256: sha256(raw), symbols, edges, refs };
}

function walkCs(dirAbs, out) {
  if (!fs.existsSync(dirAbs)) return out;
  for (const e of fs.readdirSync(dirAbs, { withFileTypes: true })) {
    const abs = path.join(dirAbs, e.name);
    const r = rel(abs);
    if (SKIP_REL_PREFIX.some((p) => r.startsWith(p))) continue;
    if (e.isDirectory()) {
      if (!SKIP_DIRS.has(e.name.toLowerCase())) walkCs(abs, out);
    } else if (e.isFile() && e.name.endsWith('.cs')) out.push(abs);
  }
  return out;
}

function buildCodeGraph({ roots = DEFAULT_ROOTS } = {}) {
  const files = [...new Set(roots.flatMap((r) => walkCs(path.join(ROOT, r), [])))].sort();
  const graph = { schema: SCHEMA, generatedAt: new Date().toISOString(), roots, files: {}, symbols: [], edges: [] };
  const pendingRefs = [];
  for (const abs of files) {
    const scanned = scanFile(abs);
    graph.files[scanned.file] = { sha256: scanned.sha256, symbols: scanned.symbols.length };
    graph.symbols.push(...scanned.symbols);
    graph.edges.push(...scanned.edges);
    pendingRefs.push(...scanned.refs.map((r) => ({ ...r, file: scanned.file })));
  }
  const TYPE_KINDS = new Set(['class', 'struct', 'interface', 'enum', 'record']);
  const typeNames = new Set(graph.symbols.filter((s) => TYPE_KINDS.has(s.kind)).map((s) => s.name));
  for (const r of pendingRefs) {
    for (const [name, line] of r.names) {
      if (typeNames.has(name)) graph.edges.push({ from: r.from, toName: name, type: 'type-ref', file: r.file, line });
    }
  }
  // Resolve name edges against defined symbols; unresolved names are library
  // or engine calls (Debug.Log, Mathf.Clamp) and stay as toName only.
  const byName = new Map();  // Map, not {}: 'constructor' is a real symbol name here
  for (const s of graph.symbols) {
    if (!byName.has(s.name)) byName.set(s.name, []);
    byName.get(s.name).push(s);
  }
  for (const e of graph.edges) {
    const wantTypes = e.type !== 'call';
    const hits = (byName.get(e.toName) || []).filter((s) => (wantTypes
      ? !['method', 'constructor'].includes(s.kind) : ['method', 'constructor'].includes(s.kind)));
    if (hits.length) e.to = hits.map((s) => s.id);
  }
  return graph;
}

function writeGraph(graph, outPath = DEFAULT_OUT) {
  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  fs.writeFileSync(outPath, JSON.stringify(graph) + '\n', 'utf8');
  return outPath;
}

function loadGraph(graphPath = DEFAULT_OUT) {
  try {
    return JSON.parse(fs.readFileSync(graphPath, 'utf8'));
  } catch {
    return null;
  }
}

function isStale(graph) {
  if (!graph) return true;
  for (const [file, meta] of Object.entries(graph.files)) {
    const abs = path.join(ROOT, file);
    if (!fs.existsSync(abs)) return true;
    if (sha256(fs.readFileSync(abs, 'utf8').replace(/^﻿/, '')) !== meta.sha256) return true;
  }
  return false;
}

function findSymbols(graph, query) {
  const q = query.trim();
  const exact = graph.symbols.filter((s) => s.qname === q || s.name === q);
  if (exact.length) return exact;
  const lower = q.toLowerCase();
  return graph.symbols.filter((s) => s.qname.toLowerCase().endsWith(`.${lower}`) || s.name.toLowerCase() === lower);
}

const INVISIBLE_CALLERS = 'lexical scan: engine callbacks, reflection/plugin loading (AssemblyLoadContext) and delegates/events are not visible as callers';

function describe(graph, sym) {
  const callers = graph.edges.filter((e) => e.to && e.to.includes(sym.id));
  const callees = graph.edges.filter((e) => e.from === sym.id);
  const homonyms = graph.symbols.filter((s) => s.name === sym.name && s.id !== sym.id && s.kind === sym.kind).length;
  return {
    symbol: sym,
    callers: callers.map((e) => ({ type: e.type, from: e.from, at: `${e.file}:${e.line}`, ...(e.receiver ? { receiver: e.receiver } : {}) })),
    callees: callees.map((e) => ({ type: e.type, to: e.toName, resolved: e.to ? e.to.length : 0, at: `${e.file}:${e.line}` })),
    notes: [
      ...(sym.engineEntry ? ['engine callback: called by Unity/Godot, not by project code'] : []),
      ...(homonyms ? [`${homonyms} other ${sym.kind}(s) share the name '${sym.name}' - name-based callers may belong to them`] : []),
      ...(callers.length === 0 && !sym.engineEntry ? [`no lexical callers found (${INVISIBLE_CALLERS})`] : []),
    ],
  };
}

function functionsInRange(graph, file, startLine, endLine) {
  return graph.symbols.filter((s) => s.file === file && (s.kind === 'method' || s.kind === 'constructor')
    && s.line <= endLine && s.endLine >= startLine);
}

// Symbols named in free text (a RAG query). Only names that are defined in
// the graph count, and very common words are ignored.
function symbolsInText(graph, text, max = 3) {
  const names = new Set(graph.symbols.map((s) => s.name));
  const out = [];
  for (const word of String(text).match(/[A-Za-z_][\w.]*/g) || []) {
    const last = word.split('.').pop();
    if (last.length >= 4 && names.has(last) && !out.includes(last)) out.push(last);
    if (out.length >= max) break;
  }
  return out;
}

function codeCard(graph, name, max = 6) {
  const defs = findSymbols(graph, name).slice(0, max);
  return defs.map((d) => {
    const info = describe(graph, d);
    return {
      qname: d.qname, kind: d.kind, at: `${d.file}:${d.line}-${d.endLine}`,
      callers: info.callers.length, callees: info.callees.length, notes: info.notes,
      topCallers: [...new Set(info.callers.map((c) => c.at))].slice(0, 4),
    };
  });
}

const HELP = `Usage: node tools/knowledge/code-graph-cs.js <symbol> [options]

Lexical C# graph of What If (src, tests, engine-unity, engine-godot, tools).

Options:
  --build          Rebuild the graph first (build-rag-index.js also does this).
  --json           Machine-readable output.
  --file <path>    List the symbols of one file instead of a symbol lookup.
  --stats          Print graph statistics.

Examples:
  node tools/knowledge/code-graph-cs.js Step
  node tools/knowledge/code-graph-cs.js VehiclePhysicsAdapter --json
  node tools/knowledge/code-graph-cs.js --file engine-unity/What If/Assets/Scripts/Hud/HudController.cs
`;

function main() {
  const argv = process.argv.slice(2);
  if (argv.includes('--help') || argv.includes('-h')) { console.log(HELP); return; }
  const json = argv.includes('--json');
  let graph = loadGraph();
  if (argv.includes('--build') || isStale(graph)) {
    if (graph && !argv.includes('--build')) console.error('code graph stale against sources - rebuilding');
    graph = buildCodeGraph();
    writeGraph(graph);
  }
  const fileIdx = argv.indexOf('--file');
  if (argv.includes('--stats')) {
    const kinds = Object.create(null);
    for (const s of graph.symbols) kinds[s.kind] = (kinds[s.kind] || 0) + 1;
    const resolved = graph.edges.filter((e) => e.to).length;
    const stats = { files: Object.keys(graph.files).length, symbols: kinds, edges: graph.edges.length, resolvedEdges: resolved };
    console.log(json ? JSON.stringify(stats, null, 2) : Object.entries(stats).map(([k, v]) => `${k}: ${JSON.stringify(v)}`).join('\n'));
    return;
  }
  if (fileIdx !== -1) {
    const file = String(argv[fileIdx + 1] || '').replace(/\\/g, '/');
    const syms = graph.symbols.filter((s) => s.file === file);
    if (!graph.files[file]) {
      console.error(`not in graph: ${file} (graph roots: ${graph.roots.join(', ')})`);
      process.exit(2);
    }
    console.log(json ? JSON.stringify(syms, null, 2) : syms.map((s) => `${s.kind.padEnd(11)} ${s.qname}  ${s.file}:${s.line}-${s.endLine}`).join('\n'));
    return;
  }
  const query = argv.filter((a, i) => !a.startsWith('--') && argv[i - 1] !== '--file').join(' ');
  if (!query) { console.log(HELP); process.exit(2); }
  const defs = findSymbols(graph, query);
  if (!defs.length) {
    // Absence of a match is not absence of the code: say what was searched.
    console.error(`no symbol '${query}' among ${graph.symbols.length} symbols in ${Object.keys(graph.files).length} files (roots: ${graph.roots.join(', ')}); try a RAG query instead`);
    process.exit(1);
  }
  const results = defs.slice(0, 10).map((d) => describe(graph, d));
  if (json) { console.log(JSON.stringify(results, null, 2)); return; }
  for (const r of results) {
    const s = r.symbol;
    console.log(`\n${s.kind} ${s.qname}  ${s.file}:${s.line}-${s.endLine}${s.params !== undefined ? `  (${s.params})` : ''}`);
    for (const n of r.notes) console.log(`  note: ${n}`);
    if (r.callers.length) console.log(`  callers (${r.callers.length}):`);
    for (const c of r.callers.slice(0, 20)) console.log(`    ${c.type.padEnd(8)} ${c.at}  <- ${c.from.split('#').pop()}`);
    const known = r.callees.filter((c) => c.resolved);
    if (known.length) console.log(`  calls into project code (${known.length} of ${r.callees.length} call sites):`);
    for (const c of known.slice(0, 20)) console.log(`    ${c.type.padEnd(8)} ${c.to}${c.resolved > 1 ? ` (${c.resolved} candidates)` : ''}  @ ${c.at}`);
  }
  if (defs.length > 10) console.log(`\n... ${defs.length - 10} more definitions; narrow with a qualified name`);
}

if (require.main === module) main();

module.exports = {
  buildCodeGraph, writeGraph, loadGraph, isStale, findSymbols, describe,
  functionsInRange, symbolsInText, codeCard, maskCode, scanFile, DEFAULT_OUT,
};
