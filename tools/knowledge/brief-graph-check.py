"""RG-121 scope 5: which wiki pages (and their neighbours) must a worker read for the files a brief owns?

    python -X utf8 tools/knowledge/brief-graph-check.py <brief.md | RG-0xx> [--strict] [--all]

Reads the brief's `### Exact write ownership` section (heading matched by PREFIX, so `... (draft)` works),
takes the owned paths (bullets with backticked tokens or plain paths; `Read-only` bullets/blocks are skipped
unless --all), and for each path prints, from the wiki files themselves (no index needed):
  - direction C: the wiki pages whose `sources:` cover the path,
  - direction B: the pages those link to / are linked from,
  - `MISSING` when no wiki page covers the path (an owned path with no page is a gap, not an exemption).
Evidence paths (agent_pool/**, *.meta, manual-tests) are listed as `evidence` and not counted.
The output is a Markdown block the orchestrator pastes into the brief (section "Knowledge pointers").
--strict: exit 1 when any counted path is MISSING."""
import glob, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def read_pages():
    pages = {}
    for p in glob.glob(os.path.join(ROOT, "wiki", "**", "*.md"), recursive=True):
        rel = os.path.relpath(p, ROOT).replace("\\", "/")
        if rel in ("wiki/index.md", "wiki/log.md"):
            continue
        t = open(p, encoding="utf-8").read()
        m = re.match(r"---\r?\n(.*?)\r?\n---\r?\n(.*)", t, re.S)
        fm, body = (m.group(1), m.group(2)) if m else ("", t)

        def lst(key):
            sm = re.search(r"^%s:\s*\r?\n((?:\s+-\s.*\r?\n?)+)" % key, fm + "\n", re.M)
            return [re.sub(r"^\s+-\s*", "", l).strip().strip("'\"") for l in sm.group(1).splitlines() if l.strip()] if sm else []

        st = re.search(r"^status:\s*(\S+)", fm, re.M)
        pages[rel] = {"slug": os.path.basename(rel)[:-3], "aliases": [a.lower() for a in lst("aliases")], "sources": lst("sources"),
                      "links": [l.split("|")[0].strip() for l in re.findall(r"\[\[([^\]]+)\]\]", body)],
                      "status": st.group(1) if st else "-"}
    return pages


def resolve(pages, name):
    n = name.lower()
    for p, d in pages.items():
        if d["slug"].lower() == n or n in d["aliases"]:
            return p
    return None


def brief_path(arg):
    if os.path.isfile(arg):
        return arg
    cand = os.path.join(ROOT, "agent_pool", "shared", "briefs", arg + ".md")
    if os.path.isfile(cand):
        return cand
    sys.exit(f"brief not found: {arg}")


def parse_ownership(path, include_ro):
    txt = open(path, encoding="utf-8").read()
    m = re.search(r"^### Exact write ownership[^\n]*\n(.*?)(?=^#{1,3} |\Z)", txt, re.S | re.M)
    if not m:
        sys.exit(f"{path}: no '### Exact write ownership' section")
    owned, in_ro = [], False
    for line in m.group(1).splitlines():
        s = line.strip()
        if re.match(r"^(-\s*)?Read-only", s):
            in_ro = True
            if not include_ro:
                continue
        elif re.match(r"^-\s*(New|Owned|Own|Also allowed)", s):
            in_ro = False
        elif re.match(r"^-\s*worker evidence", s):
            owned.append("agent_pool/agents/claude/outbox/")  # evidence
            continue
        if in_ro and not include_ro:
            continue
        if not s.startswith("-") and not (in_ro and include_ro):
            continue
        toks = re.findall(r"`([^`]+)`", s)
        if not toks:
            body = re.sub(r"^-\s*", "", s)
            body = re.sub(r"\s*\(new.*$", "", body)
            toks = [body] if body and " " not in body else []
        for tk in toks:
            if "/" in tk or "." in tk:
                owned.append(tk)
    seen, out = set(), []
    for tk in owned:
        if tk not in seen:
            seen.add(tk)
            out.append(tk)
    return out


def norm(p, tracked):
    p = p.replace("\\", "/")
    if p.startswith("Assets/") or p.startswith("ProjectSettings/"):
        p = "engine-unity/What If/" + p
    p = re.sub(r"\*\*?$", "", p).replace("*", "").rstrip("/")
    if "/" not in p:  # bare file name: any directory
        hits = [t for t in tracked if t.endswith("/" + p) or t == p]
        if len(hits) == 1:
            return hits[0], True
    return p, False


def covers(src, q, bare):
    s = src.replace("\\", "/").rstrip("/")
    if bare:
        return s == q
    return q == s or q.startswith(s + "/") or s.startswith(q)


def is_evidence(p):
    return p.startswith("agent_pool/") or p.endswith(".meta") or p.startswith("worker evidence") or p == ".meta"


def shown(path):
    try:
        return os.path.relpath(path, ROOT).replace("\\", "/")
    except ValueError:  # other drive (Windows)
        return path.replace("\\", "/")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        sys.exit(__doc__)
    brief = brief_path(args[0])
    tracked = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8").stdout.split()
    pages = read_pages()
    back = {p: [] for p in pages}
    outl = {}
    for p, d in pages.items():
        outl[p] = sorted({resolve(pages, l) for l in d["links"]} - {None, p})
        for q in outl[p]:
            back[q].append(p)
    paths = parse_ownership(brief, "--all" in sys.argv)
    missing, counted = [], 0
    lines = [f"### Knowledge pointers (generated by `tools/knowledge/brief-graph-check.py` from `{shown(brief)}`)", ""]
    pages_all, evidence = {}, []
    for raw in paths:
        if is_evidence(raw):
            evidence.append(raw)
            continue
        q, bare = norm(raw, tracked)
        counted += 1
        hit = [p for p, d in pages.items() if any(covers(s, q, bare) for s in d["sources"])]
        if not hit:
            missing.append(raw)
            lines.append(f"- `{raw}` — **MISSING**: no wiki page lists a covering `sources:` entry. Write one or say in the brief that none exists.")
            continue
        for h in hit:
            pages_all.setdefault(h, set()).add(raw)
        lines.append(f"- `{raw}` — " + ", ".join(f"`{h}`" + (" (provisional)" if pages[h]["status"] == "provisional" else "") for h in sorted(hit)))
    nb = {}
    for h in pages_all:
        for n in outl[h] + back[h]:
            if n not in pages_all:
                nb.setdefault(n, set()).add(h)
    lines += ["", "Read first (wiki pages covering the owned paths): " + (", ".join(f"`{h}`" for h in sorted(pages_all)) or "none"), ""]
    if nb:
        lines.append("Graph neighbours (linked from or to those pages; read when the change crosses the boundary): " +
                     ", ".join(f"`{n}`" for n in sorted(nb, key=lambda n: (-len(nb[n]), n))[:8]))
        lines.append("")
    # Read-only paths are not owned, but a worker calls their API: the page that documents them is a dependency too.
    ro = [x for x in parse_ownership(brief, True) if x not in paths and not is_evidence(x)]
    ro_lines = []
    for raw in ro:
        q, bare = norm(raw, tracked)
        hit = [p for p, d in pages.items() if any(covers(s, q, bare) for s in d["sources"])]
        if hit and not all(h in pages_all for h in hit):
            ro_lines.append(f"`{raw}` — " + (", ".join(f"`{h}`" for h in sorted(hit)) if len(hit) <= 4 else f"{len(hit)} pages (broad glob, query `rag-query.js` for the part you call)"))
    if ro_lines:
        lines.append("Read-only dependencies with a wiki page (read the page before calling their API): " + "; ".join(ro_lines))
        lines.append("")
    if evidence:
        lines.append(f"Evidence paths, not checked: {len(evidence)}.")
    lines.append(f"Coverage: {counted - len(missing)}/{counted} owned paths have a wiki page" + (f"; MISSING: {', '.join(f'`{m}`' for m in missing)}" if missing else "") + ".")
    print("\n".join(lines))
    if "--strict" in sys.argv and missing:
        sys.exit(1)



if __name__ == "__main__":
    main()
