#!/usr/bin/env node
'use strict';

// What If RAG query (GraphRAG-lite): BM25 + Czech stemming + CS<->EN
// expansion, wiki-graph alias boost and 1-hop neighbours, C# code-graph cards.
//
// Ported from zonio_app tools/rag/rag-query.js on 2026-10-11. The ranking
// constants and their comments are kept as they were tuned on the Zonio corpus
// (~18k chunks); they are a starting point here, not a What If measurement.
// Removed: the optional embedding lane (dependency-free policy, see
// wiki/decision/knowledge-layer-separate-from-zonio.md).

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const { findProjectRoot, reportsDir, gitHead } = require('./_lib/project-root');
const { loadGraph: loadCodeGraph, symbolsInText, codeCard, functionsInRange } = require('./code-graph-cs');
const { parseFrontmatter } = require('./_lib/simple-frontmatter');

const ROOT = findProjectRoot(__dirname);
const DEFAULT_INDEX = path.join(reportsDir(ROOT), 'latest-rag-index.json');

function parseArgs(argv) {
  const args = {
    index: DEFAULT_INDEX,
    query: '',
    top: 8,
    json: false,
    showText: false,
    graph: true,
    rebuild: true,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--index') args.index = path.resolve(ROOT, argv[++i]);
    else if (arg === '--query' || arg === '-q') args.query = argv[++i];
    else if (arg === '--top') args.top = Number(argv[++i]);
    else if (arg === '--json') args.json = true;
    else if (arg === '--show-text') args.showText = true;
    else if (arg === '--no-graph') args.graph = false;
    else if (arg === '--no-rebuild') args.rebuild = false;
    else if (arg === '--full') args.excerptChars = 0;
    else if (arg === '--excerpt-chars') args.excerptChars = Number(argv[++i]);
    else if (arg === '--status') args.status = argv[++i];
    else if (arg === '--type') args.knowledgeType = argv[++i];
    else if (arg === '--help' || arg === '-h') {
      printHelp();
      process.exit(0);
    } else if (arg.startsWith('--')) {
      throw new Error(`Unknown argument: ${arg}`);
    } else {
      args.query = args.query ? `${args.query} ${arg}` : arg;
    }
  }

  if (!args.query) throw new Error('Missing --query');
  return args;
}

function printHelp() {
  console.log(`Usage:
  node tools/knowledge/rag-query.js --query "<dotaz>" [options]

Options:
  --index <path>    Index JSON. Default: artifacts/knowledge/latest-rag-index.json.
  --top <n>         Number of results. Default: 8.
  --json            Emit machine-readable JSON.
  --show-text       Include chunk text in Markdown output.
  --no-graph        Disable wiki-graph alias boost, 1-hop neighbor expansion and
                    C# code-graph cards (callers/callees of named symbols).
  --excerpt-chars <n>  Body budget per hit. Default: ${DEFAULT_EXCERPT_CHARS}. Each hit
                    carries an exact \`path:start-end\` anchor; open that to read
                    the source. A chunk is navigation, not evidence.
  --full            Return whole chunks instead of excerpts (pre-2026-08-04
                    behaviour; a --top 15 query costs ~40 kB).
  --status <s>      Only chunks with this knowledge status (implemented,
                    provisional, planned, historical, superseded).
  --type <t>        Only wiki pages of this type (entity, concept, decision).
  --no-rebuild      Query the index as it is. By default a stale index (other
                    Git HEAD, changed ticket status in the main checkout, a
                    wiki page newer than the index, or older than 24h) is
                    rebuilt first (~5 s).
`);
}

function rel(abs) {
  return path.relative(ROOT, abs).replace(/\\/g, '/');
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

// --- Cross-language query expansion (rag-retrieval-upgrade phase 3) -------
//
// The corpus is bilingual page-by-page (24 of 41 wiki pages CS, 17 EN - see
// wiki/concept/agent-knowledge-entry-gate.md), so a query typed in one
// language structurally cannot reach ~40% of the wiki, even when the page
// answers it. Both pieces below are QUERY-SIDE ONLY: the index built by
// build-rag-index.js is untouched, so this is fully revertible and needs no
// rebuild.
//
// 1. czechStem(): a crude, ordered suffix-stripping table (not a real
//    analyzer). Its only job is to let an inflected query token collapse
//    onto a DIFFERENT literal token that already sits somewhere in the
//    corpus - e.g. ascii-folded "twinu" (from "twinů") -> "twin", which
//    matches the bare English loanword the corpus actually uses; "rodiny"
//    and "rodina" both -> "rodin", which happens to be the literal token in
//    a page titled "...zrfw rodin". It is deliberately conservative (short
//    tokens are left alone) because over-stemming just adds low-weight noise
//    via expandQueryTerms below, never removes a correct match.
// 2. The CS<->EN domain synonym table (tools/knowledge/cs-en-domain-synonyms.v1.json):
//    ~50 equivalence classes of terms this project's own corpus actually
//    uses (wiki titles, skill headings, eval-suite queries), not a generic
//    dictionary. Looked up by both the raw token and its stem, so
//    "namountovat" (raw, in the table) and "reaguje" (also raw, in the
//    table) both expand without needing perfect stemming.

// What If's own table (the Zonio one describes firmware); excluded from the
// corpus by build-rag-index.js for the measured reason given there.
const SYNONYMS_PATH = path.join(ROOT, 'tools', 'knowledge', 'cs-en-domain-synonyms.v1.json');

// Longest-suffix-first so e.g. "-ovat" is tried before the bare "-a" it ends
// with. STEM_MIN_TOKEN_LEN/STEM_MIN_REMAINDER exist so short words ("byt",
// "twin") are never touched - there just isn't enough token left to safely
// strip anything.
const STEM_MIN_TOKEN_LEN = 5;
const STEM_MIN_REMAINDER = 3;
const CZECH_SUFFIXES = [
  'ovava', 'ovali', 'ovala', 'ovalo', 'ovat',
  'atech', 'ejsim', 'ejsi',
  'ich', 'ech', 'ach', 'ata', 'emi', 'ymi', 'imi', 'ovi', 'umi',
  'em', 'am', 'um', 'im',
  'e', 'i', 'y', 'u', 'a', 'o',
].sort((a, b) => b.length - a.length);

function czechStem(token) {
  if (token.length < STEM_MIN_TOKEN_LEN) return token;
  for (const suffix of CZECH_SUFFIXES) {
    if (token.endsWith(suffix) && token.length - suffix.length >= STEM_MIN_REMAINDER) {
      return token.slice(0, token.length - suffix.length);
    }
  }
  return token;
}

let synonymIndexCache = null;

// term -> Set of every OTHER term in its equivalence class (both cs and en
// members), keyed for O(1) lookup from either language. Missing/broken data
// degrades to "no expansion" rather than breaking retrieval.
function loadSynonymIndex() {
  if (synonymIndexCache) return synonymIndexCache;
  const map = new Map();
  try {
    const data = JSON.parse(fs.readFileSync(SYNONYMS_PATH, 'utf8'));
    for (const entry of data.entries || []) {
      const members = [...(entry.cs || []), ...(entry.en || [])];
      for (const term of members) {
        if (!map.has(term)) map.set(term, new Set());
        for (const other of members) {
          if (other !== term) map.get(term).add(other);
        }
      }
    }
  } catch {
    // Missing file / bad JSON: fall through with an empty map.
  }
  synonymIndexCache = map;
  return map;
}

// Terms the user actually typed must outrank inferred ones - an expansion is
// a guess about intent, a typed word is a fact - so this stays under 1.0 no
// matter what. The exact value was found by sweeping tools/rag/evaluate-rag.js
// --baseline against the full eval suite (not just the 5 target cases) and
// taking the highest value with zero ranks moved down on any of the 34 cases:
//   0.5  - safe floor, only 1 of the 5 target cross-language gaps passes.
//   0.6  - still 0 regressions, but more weight bought nothing further; the
//          extra score was going to competing documents as much as the target.
//   0.65-0.79 - a rare CS/EN term's idf is large enough on this ~18k-chunk
//          corpus that pushing weight up here starts lifting the right page
//          into its top-5 window (3 of 5 pass by 0.75).
//   0.80 - 4 of 5 pass, still 0 regressions.
//   0.82 - first regression appears (an English word that our crude Czech
//          stemmer happens to truncate the same way a literal token
//          elsewhere in the corpus is spelled - see rag-retrieval-upgrade
//          phase 3 notes). That is the ceiling, not a cliff a few notches
//          below matters.
// 0.80 is kept with that headroom rather than pushed to the edge. A query
// that is already monolingual and matching on its own typed terms is barely
// touched either way, since 1.0-weighted hits keep dominating the BM25 sum.
const EXPANSION_WEIGHT = 0.8;

// Returns Map<term, weight> - typed tokens at weight 1, every stem/synonym
// addition at EXPANSION_WEIGHT (highest weight wins if a term is reachable
// both ways, e.g. typed directly AND produced by expansion).
// Function words of a typed question. They carry no subject, but BM25 still
// scores them, and a short wiki page whose doubled heading is "Proč (Why)" won
// "proč je data.acd zakázané" on `proc` + `je` alone over ADR-023, the only
// source that answers it (evaluate_rag.py data-acd-forbidden, 2026-10-19).
// Dropped from the query only, never from the index; a query made only of
// them keeps them, so it still returns something to look at.
const QUERY_STOPWORDS = new Set([
  'a', 'aby', 'ale', 'ani', 'by', 'co', 'do', 'i', 'jak', 'je', 'jsou', 'k', 'kde', 'kdy', 'ktere', 'ktery',
  'na', 'o', 'od', 'po', 'pro', 'proc', 'se', 's', 'si', 'to', 'u', 'v', 've', 'z', 'za', 'ze',
  'an', 'and', 'are', 'do', 'does', 'how', 'in', 'is', 'it', 'of', 'on', 'or', 'the', 'to', 'what', 'when',
  'where', 'which', 'why',
]);

function queryTokensOf(query) {
  const all = [...new Set(tokenize(query))];
  const content = all.filter((t) => !QUERY_STOPWORDS.has(t));
  return content.length ? content : all;
}

function expandQueryTerms(tokens) {
  const synonyms = loadSynonymIndex();
  const weights = new Map();
  const bump = (term, weight) => {
    if (!term) return;
    if ((weights.get(term) || 0) < weight) weights.set(term, weight);
  };
  for (const token of tokens) {
    bump(token, 1);
    const stem = czechStem(token);
    if (stem !== token) bump(stem, EXPANSION_WEIGHT);
    for (const base of new Set([token, stem])) {
      const related = synonyms.get(base);
      if (!related) continue;
      for (const term of related) bump(term, EXPANSION_WEIGHT);
    }
  }
  return weights;
}

// GraphRAG-lite lookups over the index's `graph` section (wiki pages, their
// [[wikilinks]] and `sources:` pointers). Returns null when the index has no
// graph (older index) so every caller degrades to plain BM25.
function buildGraphLookups(graph) {
  if (!graph || !graph.pages) return null;
  const bySlug = new Map(Object.entries(graph.pages));
  if (!bySlug.size) return null;
  const byPath = new Map();
  const nameToSlug = new Map();
  const backlinks = new Map();
  for (const [slug, page] of bySlug) {
    byPath.set(page.path, slug);
    nameToSlug.set(asciiFold(slug.replace(/-/g, ' ')), slug);
    for (const alias of page.aliases || []) nameToSlug.set(asciiFold(String(alias)), slug);
    for (const link of page.links || []) {
      if (!backlinks.has(link)) backlinks.set(link, []);
      backlinks.get(link).push(slug);
    }
  }
  return { bySlug, byPath, nameToSlug, backlinks };
}

// Entity/alias boost: when the query names a wiki page (by slug words or a
// declared alias), that page's chunks get a flat score bonus so the curated
// synthesis outranks incidental keyword hits.
function aliasMatchedPaths(lookups, query) {
  if (!lookups) return null;
  const foldedQuery = asciiFold(query).replace(/-/g, ' ');
  const paths = new Set();
  for (const [name, slug] of lookups.nameToSlug) {
    const nameInQuery = name.length >= 4 && foldedQuery.includes(name);
    const queryInName = foldedQuery.length >= 6 && name.includes(foldedQuery);
    if (nameInQuery || queryInName) paths.add(lookups.bySlug.get(slug).path);
  }
  return paths.size ? paths : null;
}

// Not every source is worth the same as an answer, and BM25 cannot know that.
// A curated wiki page exists precisely to be the entry point for a topic, so
// when it and the raw source it documents both match, it should come first -
// the source is one hop away through `sources:` anyway.
//
// The two catalogue files are the opposite case: `wiki/index.md` restates the
// summary of every page and `wiki/log.md` restates every change, so they match
// almost any query about the project and displace the page that actually holds
// the answer. Measured on the eval suite: log.md took rank 1 for a query whose
// answer is an entity page, and index.md kept landing in the top 5 of queries
// it cannot answer. They stay indexed (they are real navigation aids) but stop
// competing with the pages they summarize.
const SOURCE_WEIGHTS = [
  [/^wiki\/(entity|concept|decision)\//, 1.25],
  [/^\.claude\/skills\//, 1.15],
];
const CATALOGUE_WEIGHT = 0.5;
const CATALOGUE_PATHS = new Set(['wiki/index.md', 'wiki/log.md']);

function sourceWeight(sourcePath) {
  if (CATALOGUE_PATHS.has(sourcePath)) return CATALOGUE_WEIGHT;
  for (const [pattern, weight] of SOURCE_WEIGHTS) {
    if (pattern.test(sourcePath)) return weight;
  }
  return 1;
}

// Ranking deliberately does NOT weight `status`, and that is a measured
// decision, not an oversight. Penalising `provisional` looks obviously right -
// those chunks are labelled "design/plan doc, NOT implementation truth" - but
// swept against the eval suite it was neutral at 0.9/0.95 (identical MRR) and
// harmful at 0.75 (MRR 0.907 -> 0.864, recall@1 0.889 -> 0.815). The reason is
// specific to this project: a provisional page is usually the ONLY page on its
// topic, so demoting it demotes the right answer. `daylight-penetration-model`
// is `status: provisional` and is exactly what a daylight query should return;
// the penalty pushed it from rank 1 to 3.
//
// Filters below are the honest way to use this metadata: "only what is
// implemented" and "only decision pages" are questions it can answer exactly,
// and answering them exactly beats nudging scores and hoping.
function passesFilters(chunk, { status, knowledgeType }) {
  if (status && chunk.status !== status) return false;
  if (knowledgeType && chunk.knowledgeType !== knowledgeType) return false;
  return true;
}

// One long file can win every slot in the window: a single doc took 3 of 5 and
// 6 of 15 on real queries, crowding out the pages that answered them. Cap the
// chunks any one file may contribute so the window stays a set of candidate
// *sources*, which is what a navigating agent needs.
function capPerSource(ranked, chunks, top, maxPerSource) {
  const used = new Map();
  const picked = [];
  const overflow = [];
  for (const entry of ranked) {
    const sourcePath = chunks[entry[0]].sourcePath;
    const count = used.get(sourcePath) || 0;
    if (count < maxPerSource) {
      used.set(sourcePath, count + 1);
      picked.push(entry);
      if (picked.length === top) return picked;
    } else if (overflow.length < top) {
      overflow.push(entry);
    }
  }
  // Fewer distinct sources than requested results: fall back to the capped-out
  // chunks rather than returning a short window.
  return [...picked, ...overflow].slice(0, top);
}

// Default excerpt budget per hit. Chunks are up to DEFAULT_CHUNK_CHARS (3 600)
// and used to be returned whole, so a --top 15 query emitted ~54 kB of body
// before the neighbour lane added its own. Measured 2026-08-04 on
// the reference build task: 41 913 B of CLI output, of
// which the agent could use none - the cost was paid in full regardless.
//
// A retrieval hit is navigation, not evidence: the project rule is already
// "chunk je navigace, ne důkaz - tvrzení opírej o přečtený zdroj". Returning
// the whole chunk pretends to be the evidence and charges for it, while the
// agent still has to open the file. So the default is a focused window around
// the match plus an exact `path:start-end` anchor to read. `--full` restores
// whole chunks for the rare case that genuinely wants them.
const DEFAULT_EXCERPT_CHARS = 600;

/**
 * Window of `chunk.text` centred on the lines that matched, with the excerpt's
 * own absolute line range so the anchor points at the match and not at the
 * chunk boundary. Falls back to the head of the chunk when nothing matched
 * lexically (semantic-only hits, and every graph neighbour) - for a wiki page
 * or a curated JSON the head is frontmatter/schema, which is the orienting
 * part anyway.
 */
function excerptFor(chunk, termWeights, maxChars = DEFAULT_EXCERPT_CHARS) {
  const text = chunk.text || '';
  if (maxChars <= 0 || text.length <= maxChars) {
    return { text, startLine: chunk.startLine, endLine: chunk.endLine, truncated: false };
  }

  const lines = text.split('\n');
  const weights = termWeights instanceof Map ? termWeights : new Map();

  let best = 0;
  let bestScore = -1;
  if (weights.size) {
    const lineScores = lines.map((line) => {
      let s = 0;
      for (const t of new Set(tokenize(line))) if (weights.has(t)) s += weights.get(t);
      return s;
    });
    // Slide a window sized to the budget and keep the densest one, so a match
    // spread over several adjacent lines beats a single incidental mention.
    let width = 0;
    let acc = 0;
    let chars = 0;
    for (let i = 0; i < lines.length; i += 1) {
      chars += lines[i].length + 1;
      acc += lineScores[i];
      width += 1;
      while (chars > maxChars && width > 1) {
        chars -= lines[i - width + 1].length + 1;
        acc -= lineScores[i - width + 1];
        width -= 1;
      }
      if (acc > bestScore) {
        bestScore = acc;
        best = i - width + 1;
      }
    }
  }
  if (bestScore <= 0) best = 0;

  const out = [];
  let used = 0;
  let i = best;
  for (; i < lines.length && used + lines[i].length + 1 <= maxChars; i += 1) {
    out.push(lines[i]);
    used += lines[i].length + 1;
  }
  if (!out.length) {           // single line longer than the whole budget
    out.push(lines[best].slice(0, maxChars));
    i = best + 1;
  }

  return {
    text: out.join('\n'),
    startLine: chunk.startLine + best,
    endLine: chunk.startLine + i - 1,
    truncated: out.length < lines.length,
  };
}

function search(ragIndex, query, top, {
  graph = true, maxPerSource = 2, filters = {},
  excerptChars = DEFAULT_EXCERPT_CHARS,
} = {}) {
  const queryTokens = queryTokensOf(query);
  // termWeights includes every typed token (weight 1) plus cross-language/
  // morphological expansions (weight EXPANSION_WEIGHT) - see the comment
  // block above expandQueryTerms for why this is safe to add unconditionally.
  const termWeights = expandQueryTerms(queryTokens);
  const scores = new Map();
  const explanations = new Map();
  const totalDocs = ragIndex.chunks.length;
  const avgDocLength = ragIndex.stats.avgDocLength || 1;
  const k1 = 1.5;
  const b = 0.75;

  for (const [term, weight] of termWeights) {
    const postings = ragIndex.index.postings[term] || [];
    const df = ragIndex.index.documentFrequency[term] || 0;
    if (!df) continue;
    const idf = Math.log(1 + (totalDocs - df + 0.5) / (df + 0.5));
    for (const [docId, tf] of postings) {
      const len = ragIndex.index.docLengths[docId] || avgDocLength;
      const score = weight * idf * ((tf * (k1 + 1)) / (tf + k1 * (1 - b + b * (len / avgDocLength))));
      scores.set(docId, (scores.get(docId) || 0) + score);
      if (!explanations.has(docId)) explanations.set(docId, []);
      explanations.get(docId).push(weight < 1 ? `~${term}` : term);
    }
  }

  const foldedQuery = asciiFold(query);
  const boostPaths = graph ? aliasMatchedPaths(buildGraphLookups(ragIndex.graph), query) : null;
  for (let docId = 0; docId < ragIndex.chunks.length; docId += 1) {
    const chunk = ragIndex.chunks[docId];
    const haystack = asciiFold(`${chunk.sourcePath} ${chunk.section} ${chunk.text}`);
    if (haystack.includes(foldedQuery)) scores.set(docId, (scores.get(docId) || 0) + 1.5);
    if (boostPaths && boostPaths.has(chunk.sourcePath)) {
      scores.set(docId, (scores.get(docId) || 0) + 2.5);
      if (!explanations.has(docId)) explanations.set(docId, []);
      explanations.get(docId).push('graph:alias');
    }
  }

  for (const [docId, score] of scores) {
    const chunk = ragIndex.chunks[docId];
    if (!passesFilters(chunk, filters)) {
      scores.delete(docId);
      continue;
    }
    const weight = sourceWeight(chunk.sourcePath);
    if (weight !== 1) scores.set(docId, score * weight);
  }

  const ranked = [...scores.entries()].sort((a, bScore) => bScore[1] - a[1]);

  return capPerSource(ranked, ragIndex.chunks, top, maxPerSource)
    .map(([docId, score]) => {
      const chunk = ragIndex.chunks[docId];
      const ex = excerptFor(chunk, termWeights, excerptChars);
      return {
        score: Number(score.toFixed(4)),
        matchedTerms: [...new Set(explanations.get(docId) || [])],
        id: chunk.id,
        sourcePath: chunk.sourcePath,
        section: chunk.section,
        ...(chunk.symbols ? { symbols: chunk.symbols } : {}),
        // Anchor of the EXCERPT, so `read path:start-end` lands on the lines
        // that matched rather than on a 3 600-char chunk boundary.
        startLine: ex.startLine,
        endLine: ex.endLine,
        anchor: `${chunk.sourcePath}:${ex.startLine}-${ex.endLine}`,
        chunkLines: `${chunk.startLine}-${chunk.endLine}`,
        chars: ex.text.length,
        chunkChars: chunk.chars,
        truncated: ex.truncated,
        text: ex.text,
        status: chunk.status,
        statusNote: chunk.statusNote,
        knowledgeType: chunk.knowledgeType,
        aliases: chunk.aliases,
        lastVerified: chunk.lastVerified,
        sourceFingerprint: chunk.sourceFingerprint,
      };
    });
}

// 1-hop neighbor expansion. Tři směry, ne dva:
//   A) wiki stránka -> zdroje v jejím `sources:`   (přidáno 2026-07-30)
//   B) wiki stránka -> její [[wikilinky]] a backlinky
//   C) kód/doc -> wiki stránky, které ho dokumentují (`sources:`, prefix match)
//
// Směr A dlouho chyběl a byla to měřitelná díra. Skórování záměrně dává
// kurátorované stránce ×1,25 s odůvodněním „zdroj je stejně jeden hop daleko
// přes `sources:`" — jenže ten hop existoval jen ve směru C. Jakmile stránka
// vyhrála (což ta váha dělá pravděpodobným), zdroj, který dokumentuje, nikdo
// nenabídl. Měřeno 2026-07-30: 10 app dotazů typu „kde se počítá/dělá X"
// dalo hit@5 na zdrojový soubor **1/10** a neighbour dráha nenabídla ani
// jeden, přestože všech 9 zdrojů `runtime-load-monitoring` je v indexu.
//
// Neighbors nesou degradované skóre a `via` provenance; nikdy nevytlačí
// primární výsledky.
// RG-125: `runtime:<label>:<pattern>` chunk -> its edges in ragIndex.runtime (creator C# file, related files, wiki pages).
const RUNTIME_NEIGHBOR_BUDGET = 4;
function runtimePatternOf(ragIndex, sourcePath) {
  if (!ragIndex.runtime || !String(sourcePath).startsWith('runtime:')) return null;
  const rest = sourcePath.slice('runtime:'.length);
  const i = rest.indexOf(':');
  if (i < 0) return null;
  const label = ragIndex.runtime.labels[rest.slice(0, i)];
  return label ? (label.patterns[rest.slice(i + 1)] || null) : null;
}

const SOURCE_NEIGHBOR_BUDGET = 3;   // kolik slotů z `cap` smí zabrat směr A
const SOURCES_PER_PAGE = 2;         // aby jedna stránka s 9 zdroji nesnědla vše

function graphNeighbors(ragIndex, results, cap = 6, queryTokens = [], excerptChars = DEFAULT_EXCERPT_CHARS) {
  const lookups = buildGraphLookups(ragIndex.graph);
  if (!lookups || !results.length) return [];

  const firstChunkByPath = new Map();
  for (const chunk of ragIndex.chunks) {
    if (!firstChunkByPath.has(chunk.sourcePath)) firstChunkByPath.set(chunk.sourcePath, chunk);
  }

  const seen = new Set(results.map((r) => r.sourcePath));
  const out = [];
  // Cited-but-unindexed sources, reported beside the neighbours rather than
  // dropped. Kept small: this is a pointer list, not a result set.
  const UNINDEXED_CAP = 4;
  const unindexed = [];
  const seenUnindexed = new Set();

  const emit = (chunk, via, parentScore) => {
    seen.add(chunk.sourcePath);
    // Neighbours are reached through the graph, not through a term match, so
    // there is no matched region to centre on - excerptFor falls back to the
    // head of the chunk. They are the strongest "go read this" signal in the
    // whole result and the weakest evidence, which is exactly the shape an
    // anchor serves: name the file, spend a few hundred bytes proving it is
    // the right one, let the caller open it.
    const ex = excerptFor(chunk, null, excerptChars);
    out.push({
      score: Number((parentScore * 0.4).toFixed(4)),
      via,
      id: chunk.id,
      sourcePath: chunk.sourcePath,
      section: chunk.section,
      startLine: ex.startLine,
      endLine: ex.endLine,
      anchor: `${chunk.sourcePath}:${ex.startLine}-${ex.endLine}`,
      chunkLines: `${chunk.startLine}-${chunk.endLine}`,
      truncated: ex.truncated,
      status: chunk.status,
      knowledgeType: chunk.knowledgeType,
      text: ex.text,
    });
  };

  const push = (slug, via, parentScore) => {
    if (out.length >= cap) return;
    const page = lookups.bySlug.get(slug);
    if (!page || seen.has(page.path)) return;
    const chunk = firstChunkByPath.get(page.path);
    if (!chunk) return;
    emit(chunk, via, parentScore);
  };

  const pushSource = (sourcePath, via, parentScore) => {
    if (out.length >= cap) return false;
    if (seen.has(sourcePath)) return false;
    let chunk = firstChunkByPath.get(sourcePath);
    if (!chunk) {
      // RG-121: a directory-shaped `sources:` entry (`src/What If.Streaming/`, `tools/worldgen/`) is the normal way to
      // document a code area, and it has no chunk of its own. Reporting it as "NOT INDEXED" lost a hop that exists: the files
      // below it ARE indexed. Offer the best-matching indexed file under it (ranked like any other page source).
      const dirPrefix = sourcePath.replace(/\\/g, '/').replace(/\/+$/, '') + '/';
      const kids = [];
      for (const p of firstChunkByPath.keys()) if (p.startsWith(dirPrefix) && !seen.has(p)) kids.push(p);
      if (kids.length) {
        const best = rankSources(kids.sort())[0];
        chunk = firstChunkByPath.get(best);
        via = `${via} (directory ${sourcePath.replace(/\/+$/, '')})`;
      }
    }
    if (!chunk) {
      // A `sources:` entry with no chunk used to be swallowed here, and that
      // silence was the single most expensive defect found on 2026-08-04: a
      // curated page promises evidence, the corpus cannot deliver it, and the
      // caller is told nothing. Two plausible diagnoses ("nothing cites it",
      // "ranking is off") both fit the symptom and both were wrong.
      //
      // Measured with tools/knowledge/audit-corpus-coverage.js: 18 trees are
      // cited by wiki pages and absent from the corpus, among them
      // simulace/home (10 citing pages), FW/zrfw/exp32s2 (9) and FW/zrfw/tools
      // (8). Indexing all of them is a separate decision with a real size
      // cost; making the gap VISIBLE is free and strictly better than lying.
      //
      // Emitted without body text, so it costs a path and nothing else. A
      // directory-shaped `sources:` entry legitimately has no chunk too - the
      // caller can still open it, which is the point.
      if (unindexed.length >= UNINDEXED_CAP || seenUnindexed.has(sourcePath)) return false;
      seenUnindexed.add(sourcePath);
      unindexed.push({ sourcePath, via: `${via} — NOT INDEXED, open directly` });
      return false;
    }
    emit(chunk, via, parentScore);
    return true;
  };

  // Která z devíti `sources:` je pro TENHLE dotaz ta pravá. Bez toho by se
  // braly v pořadí zápisu a u stránky s devíti zdroji je to loterie —
  // `runtime-metrics.js` vs `widget-loader.js` je přesně rozdíl mezi
  // trefou a minutím. Řadí se podle shody tokenů dotazu s CESTOU, remíza
  // v pořadí zápisu (autoři píšou primární zdroj první).
  // Directories already represented among the scored hits. A `sources:` entry
  // from a directory the caller is ALREADY reading adds little; one from a
  // directory nothing else covers may be the only way that fact arrives.
  const coveredDirs = new Set();
  for (const r of results) {
    const p = String(r.sourcePath || '').replace(/\\/g, '/');
    if (p.includes('/')) coveredDirs.add(p.slice(0, p.lastIndexOf('/')));
  }

  // Ordering a page's `sources:` by how many query words appear in the PATH
  // rewards redundancy and penalises complementarity: the source that echoes
  // the question wins, and the one holding what the asker did not know to ask
  // for loses. Measured on the reference build task -
  // .../weather_dual_i2c/esp_config.h scores 3, advanced_i2c_handling 1, and
  // tools/knowledge/espressif-board-capabilities.v1.json scores 0, so the board
  // database can never win a slot however the question is phrased.
  //
  // v2 keeps path overlap as the primary signal and subtracts a redundancy
  // penalty when the source's own directory is already covered by a scored hit
  // (a small marginal-relevance term, not a domain rule). Behind a flag until
  // measured: intuition about this file has already failed once - raising the
  // neighbour budget made results worse, not better.
  // MEASURED AND REJECTED - author order as the primary key, overlap only as a
  // tie-break. It does not add a source, it SWAPS one: on the reference case it
  // gains the board database and loses the I2C page, same 75% coverage at lower
  // signal (21% against 27%). The binding constraint is SOURCES_PER_PAGE = 2 -
  // only two of that page's five sources can ever arrive, so ordering merely
  // chooses which two. Raising the per-page budget was measured separately and
  // is worse (3/3/8 -> 7/9).
  //
  // Default since 2026-08-05, after the measurement run it was held back for
  // completed. WHATIF_RAG_SOURCE_RANK=v1 restores the path-echo-only ordering.
  const rankV2 = process.env.WHATIF_RAG_SOURCE_RANK !== 'v1';
  const REDUNDANCY_PENALTY = 2;

  // Page scoring has run every query through expandQueryTerms (czechStem + the
  // CS<->EN class table) since the cross-language work; THIS lane never saw any
  // of it - it matched raw typed tokens against raw path words, so the whole
  // synonym table was invisible to the decision about which `sources:` arrive.
  // Both sides now meet on the same vocabulary, and path words are stemmed too:
  // matching is symmetric or it is not matching, and a query stem that cannot
  // reach a path word it IS the stem of ("simulacni" vs the directory
  // "simulace") is just the cross-language gap one layer down.
  //
  // Measured on app-session-db-cs, where this decides the case: five of that
  // page's eight sources tie at overlap 1 - all of them on the single token
  // "session" - so ordering fell through to author order and the app file sits
  // sixth. Path overlap discriminated NOTHING; the two words that would have
  // ("databaze" against src/database/, "simulacni" against simulace/) could not
  // match raw. Expanded terms keep EXPANSION_WEIGHT, as they do in page
  // scoring: a typed word is a fact, an expansion is a guess about intent, and
  // a slot must not be taken from a typed hit by an inferred one.
  // WHATIF_RAG_SOURCE_TERMS=raw restores the typed-tokens-only matching.
  const expandTerms = process.env.WHATIF_RAG_SOURCE_TERMS !== 'raw';
  const termWeights = expandTerms
    ? expandQueryTerms(queryTokens)
    : new Map(queryTokens.map((t) => [t, 1]));

  // A term shared by every source of a page cannot choose between them - and
  // measurably does worse than nothing, because it can cancel one that does.
  // Measured on app-session-db-cs: with path words stemmed, "simulacni" reaches
  // the DIRECTORY "simulace", so six of that page's eight sources collect the
  // same +0.8, the app file's own discriminating hit ("databaze" -> src/database)
  // is exactly cancelled, the tie comes back one value higher, and author order
  // hands the slot to the sim sibling again. Weighting each matched term by how
  // rare it is AMONG THIS PAGE'S OWN SOURCES is the fix: (N-df)/(N-1) is 1 for a
  // term unique to one source and 0 for one they all share.
  //
  // This is the same "reward complementarity, not redundancy" idea the directory
  // penalty below already aims at, applied where the choice is actually made:
  // the penalty compares against dirs the caller is ALREADY reading, this
  // compares against the alternatives competing for the same slot.
  const discriminationFor = (sources) => {
    const df = new Map();
    for (const src of sources) {
      for (const t of new Set(tokenize(src.replace(/[/\\_.-]+/g, ' ')))) {
        df.set(t, (df.get(t) || 0) + 1);
      }
    }
    const n = sources.length;
    return (token) => (n < 2 ? 1 : (n - (df.get(token) || 0)) / (n - 1));
  };

  const rankSources = (sources) => {
    const rarity = discriminationFor(sources);
    return sources
      .map((src, i) => {
        const pathToks = tokenize(src.replace(/[/\\_.-]+/g, ' '));
        let overlap = 0;
        for (const t of new Set(pathToks)) {
          const w = Math.max(
            termWeights.get(t) || 0,
            expandTerms ? (termWeights.get(czechStem(t)) || 0) : 0,
          );
          overlap += expandTerms ? w * rarity(t) : w;
        }
        let score = overlap;
        if (rankV2) {
          const norm = src.replace(/\\/g, '/');
          const dir = norm.includes('/') ? norm.slice(0, norm.lastIndexOf('/')) : norm;
          if (coveredDirs.has(dir)) score -= REDUNDANCY_PENALTY;
        }
        if (process.env.WHATIF_RAG_DBG) console.error('  rank', score.toFixed(2), i, src);
        return { src, i, score };
      })
      .sort((a, b) => (b.score - a.score) || (a.i - b.i))
      .map((x) => x.src);
  };

  // RG-125 runtime lane: a live scene pattern points at the C# that built it and the wiki page that documents that code.
  // Own budget, taken before direction A: the hop pattern -> creator -> page is the reason the pattern is in the index.
  let runtimeSlots = Math.min(RUNTIME_NEIGHBOR_BUDGET, cap);
  for (const result of results) {
    if (runtimeSlots <= 0 || out.length >= cap) break;
    const rp = runtimePatternOf(ragIndex, result.sourcePath);
    if (!rp) continue;
    const via = `runtime pattern ${result.sourcePath.split(':').slice(2).join(':')}`;
    const ordered = [...rp.creators.map((c) => c.file), ...rp.related];
    for (const file of ordered.slice(0, 2)) {
      if (runtimeSlots > 0 && pushSource(file, `${via}: builder`, result.score)) runtimeSlots--;
    }
    // pages that cite a builder file exactly rank above pages that cite only its directory
    const pageScore = (slug) => {
      const page = lookups.bySlug.get(slug);
      const srcs = (page && page.sources) || [];
      return ordered.some((f) => srcs.includes(f)) ? 2 : 1;
    };
    // only pages whose `sources:` name a builder / related file exactly: a page that cites just a component's file (SurfaceTag -> physics-plugins) is noise
    const pages = [...rp.pages].filter((slug) => pageScore(slug) === 2).sort();
    for (const slug of pages.slice(0, 2)) {
      if (runtimeSlots <= 0) break;
      const before = out.length;
      push(slug, `${via}: documented by`, result.score);
      if (out.length > before) runtimeSlots--;
    }
  }

  // Směr A první — má vlastní rozpočet, jinak by ho wikilinky vždy předběhly.
  //
  // Dedup je podle SLUGU, ne podle výsledku: strop výsledků je 2 chunky na
  // soubor, takže tatáž stránka umí přijít dvakrát a limit `SOURCES_PER_PAGE`
  // by obešla. Měřeno 2026-07-30 — jedna sim stránka takhle snědla celý
  // rozpočet a na relevantní stránku z 3. místa nezbylo nic.
  let sourceSlots = Math.min(SOURCE_NEIGHBOR_BUDGET, cap);
  const pagesUsed = new Set();
  for (const result of results) {
    if (sourceSlots <= 0 || out.length >= cap) break;
    const slug = lookups.byPath.get(result.sourcePath);
    if (!slug || pagesUsed.has(slug)) continue;
    pagesUsed.add(slug);
    const page = lookups.bySlug.get(slug);
    let taken = 0;
    for (const src of rankSources(page.sources || [])) {
      if (taken >= SOURCES_PER_PAGE || sourceSlots <= 0) break;
      if (pushSource(src, `source of ${slug}`, result.score)) { taken++; sourceSlots--; }
    }
  }

  // Směry B a C beze změny.
  for (const result of results) {
    if (out.length >= cap) break;
    const slug = lookups.byPath.get(result.sourcePath);
    if (slug) {
      const page = lookups.bySlug.get(slug);
      for (const link of page.links || []) push(link, `wikilink from ${slug}`, result.score);
      for (const back of lookups.backlinks.get(slug) || []) push(back, `backlink to ${slug}`, result.score);
    } else {
      for (const [candidate, page] of lookups.bySlug) {
        if ((page.sources || []).some((src) => result.sourcePath === src || result.sourcePath.startsWith(`${src}/`))) {
          push(candidate, `documents ${result.sourcePath}`, result.score);
        }
      }
    }
  }
  // Attached non-enumerably: every existing consumer iterates this array, and
  // a cited-but-missing path is metadata about the corpus, not a result.
  Object.defineProperty(out, 'unindexedSources', { value: unindexed, enumerable: false });
  return out;
}

// Code-graph cards (code-graph-cs.js): a text chunk of C# says nothing about
// who calls it or what it calls. A card is attached for a project symbol NAMED
// in the query, and otherwise for the method enclosing a top code hit. Missing
// graph = no cards, never an error.
function codeGraphCards(query, results, maxCards = 3, ragIndex = null) {
  const graph = loadCodeGraph();
  if (!graph) return [];
  const cards = [];
  const seen = new Set();
  for (const name of symbolsInText(graph, query, maxCards)) {
    const defs = codeCard(graph, name, 4);
    if (!defs.length) continue;
    cards.push({ symbol: name, via: 'named in query', definitions: defs });
    seen.add(name);
  }
  // RG-125: a runtime pattern in the top hits gets a card for the C# type of its related / builder file
  for (const result of results.slice(0, 5)) {
    const rp = ragIndex ? runtimePatternOf(ragIndex, result.sourcePath) : null;
    if (!rp) continue;
    for (const file of [...rp.related, ...rp.creators.map((c) => c.file)].slice(0, 2)) {
      if (cards.length >= maxCards) break;
      const type = graph.symbols.find((x) => x.file === file && ['class', 'struct', 'interface', 'enum', 'record'].includes(x.kind));
      if (!type || seen.has(type.qname)) continue;
      seen.add(type.qname);
      // a partial class spans files: keep the definition that lives in the builder file
      const defs = codeCard(graph, type.qname, 4);
      const here = defs.filter((d) => d.at.startsWith(`${file}:`));
      cards.push({ symbol: type.qname, via: `builds runtime pattern ${result.sourcePath.split(':').slice(2).join(':')} (${file})`, definitions: here.length ? here.slice(0, 1) : defs.slice(0, 1) });
    }
    break;
  }
  for (const result of results.slice(0, 5)) {
    if (cards.length >= maxCards) break;
    if (!graph.files[result.sourcePath]) continue;
    const fn = functionsInRange(graph, result.sourcePath, result.startLine, result.endLine)[0];
    if (!fn || seen.has(fn.qname)) continue;
    seen.add(fn.qname);
    cards.push({ symbol: fn.qname, via: `encloses ${result.anchor}`, definitions: codeCard(graph, fn.qname, 1) });
  }
  return cards;
}

function renderCodeCards(cards) {
  if (!cards.length) return [];
  const lines = ['## Code graph (lexical - navigation, not proof)', ''];
  for (const card of cards) {
    lines.push(`### ${card.symbol} — ${card.via}`);
    for (const d of card.definitions) {
      lines.push(`- ${d.kind} \`${d.qname}\` at \`${d.at}\` — ${d.callers} callers, ${d.callees} call sites`);
      for (const at of d.topCallers) lines.push(`  - caller \`${at}\``);
      for (const note of d.notes) lines.push(`  - note: ${note}`);
    }
    lines.push(`- more: \`node tools/knowledge/code-graph-cs.js ${card.symbol.split('.').pop()}\``);
    lines.push('');
  }
  return lines;
}

// Skill cards: which project skill to load for this query, and why. A skill is
// procedure (order of steps + traps), so a hit on a doc it summarises should
// also point at the skill - without a router pushing it into the session
// (the Zonio router fired prompts mid-run and held agents in a loop). Three
// evidence lanes, read from disk because skills are few:
//   1. a skill's own chunk is in the results;
//   2. a wiki page in the results cites the skill in `sources` (graph edge);
//      a hub page citing more than SKILL_HUB_LIMIT skills says nothing;
//   3. the query shares >= 2 content terms with the skill's name/description.
const SKILLS_DIR = path.join(ROOT, '.claude', 'skills');
const SKILL_HUB_LIMIT = 2;
const SKILL_LEXICAL_MIN = 2;

function loadSkills() {
  if (!fs.existsSync(SKILLS_DIR)) return [];
  return fs.readdirSync(SKILLS_DIR, { withFileTypes: true })
    .filter((e) => e.isDirectory() && fs.existsSync(path.join(SKILLS_DIR, e.name, 'SKILL.md')))
    .map((e) => {
      const file = path.join(SKILLS_DIR, e.name, 'SKILL.md');
      const { data } = parseFrontmatter(fs.readFileSync(file, 'utf8'));
      const name = typeof data.name === 'string' && data.name ? data.name : e.name;
      const description = typeof data.description === 'string' ? data.description : '';
      const stems = new Set(tokenize(`${name.replace(/-/g, ' ')} ${description}`)
        .filter((t) => !QUERY_STOPWORDS.has(t))
        .flatMap((t) => [t, czechStem(t)]));
      return { name, description, path: rel(file), stems };
    });
}

function skillCards(ragIndex, query, results, maxCards = 2) {
  const skills = loadSkills();
  if (!skills.length) return [];
  const byPath = new Map(skills.map((s) => [s.path, s]));
  const evidence = new Map(skills.map((s) => [s.name, { score: 0, via: [] }]));
  const note = (skill, score, via) => {
    const e = evidence.get(skill.name);
    e.score += score;
    if (!e.via.includes(via)) e.via.push(via);
  };
  results.forEach((result, idx) => {
    const own = skills.find((s) => result.sourcePath === s.path || result.sourcePath.startsWith(path.posix.dirname(s.path) + '/'));
    if (own) note(own, idx < 3 ? 3 : 1, `result #${idx + 1}`);
  });
  const pages = (ragIndex.graph && ragIndex.graph.pages) || {};
  for (const [slug, page] of Object.entries(pages)) {
    if (!results.some((r) => r.sourcePath === page.path)) continue;
    const cited = (page.sources || []).map((s) => byPath.get(s)).filter(Boolean);
    if (!cited.length || cited.length > SKILL_HUB_LIMIT) continue;
    for (const skill of cited) note(skill, 2, `[[${slug}]] cites it`);
  }
  // A typed token and its stem are one match, not two.
  const tokens = queryTokensOf(query);
  for (const skill of skills) {
    const matched = tokens.filter((t) => skill.stems.has(t) || skill.stems.has(czechStem(t)));
    if (matched.length >= SKILL_LEXICAL_MIN) note(skill, matched.length, `matches ${matched.slice(0, 4).join(', ')}`);
  }
  const ranked = skills
    .map((s) => ({ skill: s.name, path: s.path, purpose: s.description.split(/(?<=[.!?])\s|\s-\s/)[0], ...evidence.get(s.name) }))
    .filter((c) => c.score > 0)
    .sort((a, b) => b.score - a.score || a.skill.localeCompare(b.skill));
  // A runner-up far below the top card is noise next to a clear answer.
  return ranked.filter((c) => c.score >= 0.4 * ranked[0].score).slice(0, maxCards);
}

function renderSkillCards(cards) {
  if (!cards.length) return [];
  const lines = ['## Skill cards (load the skill before acting)', ''];
  for (const c of cards) {
    lines.push(`- **${c.skill}** — ${c.purpose} (\`${c.path}\`; via ${c.via.join('; ')})`);
  }
  lines.push('');
  return lines;
}

function renderMarkdown(args, ragIndex, results, neighbors, codeCards = [], cards = []) {
  const lines = [
    `# RAG query: ${args.query}`,
    '',
    `Index: \`${rel(args.index)}\``,
    `Generated: ${ragIndex.generatedAt}`,
    '',
    ...renderSkillCards(cards),
  ];

  if (results.length === 0) {
    lines.push('No results.');
    return lines.join('\n');
  }

  results.forEach((result, idx) => {
    lines.push(`## ${idx + 1}. ${result.anchor || `${result.sourcePath}:${result.startLine}`}`);
    lines.push('');
    if (result.status === 'provisional') {
      lines.push(`> ⚠ PROVISIONAL: ${result.statusNote || 'design/plan doc, not implementation truth'}  `);
    }
    lines.push(`Score: ${result.score}  `);
    lines.push(`Section: ${result.section || '(none)'}  `);
    if (result.symbols) lines.push(`Functions: ${result.symbols.join(', ')}  `);
    lines.push(`Matched: ${result.matchedTerms.join(', ') || '(phrase/path boost)'}`);
    if (args.showText) {
      lines.push('');
      lines.push('```text');
      lines.push(result.text.slice(0, 1600));
      lines.push('```');
    }
    lines.push('');
  });

  if (neighbors && neighbors.length) {
    lines.push('## Graph neighbors (1 hop)');
    lines.push('');
    neighbors.forEach((neighbor) => {
      const anchor = neighbor.anchor || `${neighbor.sourcePath}:${neighbor.startLine}`;
      lines.push(`- \`${anchor}\` — via ${neighbor.via} (score ${neighbor.score})`);
    });
    lines.push('');
  }
  lines.push(...renderCodeCards(codeCards));
  const unindexed = neighbors && neighbors.unindexedSources;
  if (unindexed && unindexed.length) {
    lines.push('## Cited but NOT in the RAG corpus');
    lines.push('');
    lines.push('A wiki page names these as evidence and the index cannot serve them.');
    lines.push('Open them directly — retrieval will never surface them, however you phrase the query.');
    lines.push('');
    unindexed.forEach((u) => lines.push(`- \`${u.sourcePath}\` — ${u.via}`));
    lines.push('');
  }

  return lines.join('\n');
}

// Why the index no longer describes the tree, or null. A per-worktree index is
// built once and then silently answers from the past: on 2026-10-19 the
// second-brain worktree's index (2026-10-11) still had RG-077/078 as todo.
function staleReason(ragIndex, defaultIndex) {
  const f = ragIndex.freshness;
  if (!f) return 'index predates freshness tracking';
  const head = gitHead(ROOT);
  if (head && f.head !== head) return `Git HEAD moved (${String(f.head).slice(0, 7)} -> ${head.slice(0, 7)})`;
  if (defaultIndex) {
    const { poolStatusSource } = require('./build-rag-index');
    const status = poolStatusSource();
    if (status && status.hash !== f.poolStatusHash) return `ticket status changed (${status.virtualPath})`;
    const { runtimeGraphSource } = require('./build-rag-index');
    const rt = runtimeGraphSource();
    if ((rt ? rt.hash : null) !== (f.runtimeHash || null)) return 'runtime graph snapshots changed (artifacts/knowledge/runtime)';
    // Uncommitted wiki edits move no HEAD; the page just written must be findable.
    const built = Date.parse(ragIndex.generatedAt || 0);
    const newer = wikiFiles().find((abs) => fs.statSync(abs).mtimeMs > built);
    if (newer) return `wiki changed (${rel(newer)})`;
    const newerSkill = wikiFiles(SKILLS_DIR).find((abs) => fs.statSync(abs).mtimeMs > built);
    if (newerSkill) return `skill changed (${rel(newerSkill)})`;
  }
  const ageMs = Date.now() - Date.parse(ragIndex.generatedAt || 0);
  if (Number.isFinite(ageMs) && ageMs > 24 * 60 * 60 * 1000) return 'older than 24h';
  return null;
}

function wikiFiles(dir = path.join(ROOT, 'wiki')) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const abs = path.join(dir, e.name);
    if (e.isDirectory()) return wikiFiles(abs);
    return e.name.endsWith('.md') ? [abs] : [];
  });
}

function rebuildIndex(reason, json) {
  // Notice on stdout for the markdown output: PowerShell 5.1 turns any native stderr line under `2>&1`
  // into a NativeCommandError and the whole query reports exit 255 (4 agent sessions, 2026-10-29..11-01).
  // JSON mode keeps stdout machine-readable, so the notice stays on stderr there.
  (json ? console.error : console.log)(`RAG index stale (${reason}); rebuilding...`);
  const r = spawnSync(process.execPath, [path.join(__dirname, 'build-rag-index.js')], { cwd: ROOT, encoding: 'utf8' });
  if (r.status !== 0) throw new Error(`index rebuild failed (exit ${r.status}): ${(r.stderr || '').trim().slice(-500)}`);
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const defaultIndex = path.resolve(args.index) === path.resolve(DEFAULT_INDEX);
  if (args.rebuild && defaultIndex) {
    const reason = fs.existsSync(args.index)
      ? staleReason(JSON.parse(fs.readFileSync(args.index, 'utf8')), true)
      : 'no index yet';
    if (reason) rebuildIndex(reason, args.json);
  }
  if (!fs.existsSync(args.index)) {
    // No index is not "no results": say so, so nobody reads silence as absence.
    throw new Error(`No RAG index at ${rel(args.index)} - build it first: node tools/knowledge/build-rag-index.js`);
  }
  const ragIndex = JSON.parse(fs.readFileSync(args.index, 'utf8'));
  const ageMs = Date.now() - Date.parse(ragIndex.generatedAt || 0);
  const staleBecause = staleReason(ragIndex, defaultIndex);
  const stale = Boolean(staleBecause);
  const indexAgeHours = Number.isFinite(ageMs) ? Number((ageMs / (60 * 60 * 1000)).toFixed(2)) : null;
  if (!args.json && stale) {
    console.log(`Warning: RAG index is stale (${staleBecause}). Rebuild: node tools/knowledge/build-rag-index.js`);
  }
  const excerptChars = args.excerptChars === undefined ? DEFAULT_EXCERPT_CHARS : args.excerptChars;
  const results = search(ragIndex, args.query, args.top, {
    graph: args.graph,
    filters: { status: args.status, knowledgeType: args.knowledgeType },
    excerptChars,
  });
  const neighbors = args.graph
    ? graphNeighbors(ragIndex, results, 6, queryTokensOf(args.query), excerptChars)
    : [];
  const codeCards = args.graph ? codeGraphCards(args.query, results, 3, ragIndex) : [];
  const cards = args.graph ? skillCards(ragIndex, args.query, results) : [];
  if (args.json) {
    console.log(JSON.stringify({
      query: args.query,
      index: rel(args.index),
      generatedAt: ragIndex.generatedAt,
      indexAgeHours,
      stale,
      staleReason: staleBecause,
      total: results.length,
      results,
      graphNeighbors: neighbors,
      unindexedSources: neighbors.unindexedSources || [],
      codeGraph: codeCards,
      skillCards: cards,
    }, null, 2));
    return;
  }
  console.log(renderMarkdown(args, ragIndex, results, neighbors, codeCards, cards));
}

if (require.main === module) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}

module.exports = {
  search, tokenize, queryTokensOf, graphNeighbors, buildGraphLookups,
  czechStem, expandQueryTerms, loadSynonymIndex, EXPANSION_WEIGHT,
  excerptFor, DEFAULT_EXCERPT_CHARS, codeGraphCards, skillCards,
};
