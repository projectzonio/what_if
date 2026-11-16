#!/usr/bin/env python3
"""Retrieval regression for the What If RAG (tools/knowledge/rag-query.js).

Runs every case of rag-evaluation.v1.json through the real query tool and
reports WHERE the expected source landed, not only whether it did, so a change
that moves a hit from #5 to #1 shows as an improvement.

Outcomes, three and not two (a check that cannot see its subject must not go
green):
  PASS     an expected path is within `top`, no forbidden path is, `expectedNeighbourAny` is among the graph neighbours or the direct hits, and the
           skill cards hold `expectedSkill` (or none when `noSkillCard`).
  FAIL     a non-knownGap case missed, or a forbidden path was returned.
  INVALID  rag-query.js failed or returned no results at all - the index was
           not looked at, which is neither a pass nor a ranking failure.
A `knownGap` case documents a measured deficiency: its miss is reported but
does not fail the run, and its pass is reported as an improvement.

Exit: 0 all pass (known gaps allowed), 1 any FAIL, 2 any INVALID.
The suite file is excluded from the indexed corpus (build-rag-index.js
SKIP_REL_FILES), so a case can never retrieve its own text.

  python -X utf8 tools/knowledge/evaluate_rag.py [--suite PATH] [--verbose] [--json]
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
QUERY = HERE / "rag-query.js"


def run_query(query, top, rebuild):
    cmd = ["node", str(QUERY), "--query", query, "--top", str(top), "--json"]
    if not rebuild:
        cmd.append("--no-rebuild")
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        return None, (proc.stderr or proc.stdout).strip()[-400:]
    try:
        return json.loads(proc.stdout), None
    except json.JSONDecodeError as exc:
        return None, f"rag-query output is not JSON: {exc}"


def matches(path, patterns):
    return any(path == p or path.startswith(p.rstrip("/") + "/") for p in patterns)


def evaluate(case, rebuild):
    top = case.get("top", 5)
    data, error = run_query(case["query"], top, rebuild)
    paths = [r["sourcePath"] for r in (data or {}).get("results", [])]
    result = {"id": case["id"], "query": case["query"], "top": top, "paths": paths,
              "knownGap": bool(case.get("knownGap"))}
    if error or not paths:
        result.update(outcome="INVALID", reason=error or "no results")
        return result
    rank = next((i + 1 for i, p in enumerate(paths) if matches(p, case.get("expectedAny", []))), None)
    forbidden = [p for p in paths if matches(p, case.get("forbidden", []))]
    # Skill cards (rag-query "Skill cards"): which skill the query should point at,
    # or that it should point at none (a ticket-status or code question).
    skills = [c["skill"] for c in data.get("skillCards", [])]
    result["rank"] = rank
    result["skills"] = skills
    # RG-121: graph hop check - a path from `expectedNeighbourAny` must be among the 1-hop neighbours (direction A/B/C of rag-query.js).
    neighbours = [n["sourcePath"] for n in data.get("graphNeighbors", [])]
    result["neighbours"] = neighbours
    if forbidden:
        result.update(outcome="FAIL", reason=f"forbidden path returned: {forbidden[0]}")
    elif case.get("expectedAny") and rank is None:
        result.update(outcome="FAIL", reason=f"expected {case['expectedAny']} not in top {top}")
    elif case.get("expectedNeighbourAny") and not any(matches(n, case["expectedNeighbourAny"]) for n in neighbours + paths):   # a direct hit delivers the page too
        result.update(outcome="FAIL", reason=f"expected neighbour {case['expectedNeighbourAny']} not among {neighbours}")
    elif case.get("expectedSkill") and case["expectedSkill"] not in skills:
        result.update(outcome="FAIL", reason=f"expected skill card {case['expectedSkill']}, got {skills or 'none'}")
    elif case.get("noSkillCard") and skills:
        result.update(outcome="FAIL", reason=f"expected no skill card, got {skills}")
    else:
        result["outcome"] = "PASS"
    return result


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--suite", default=str(HERE / "rag-evaluation.v1.json"))
    ap.add_argument("--verbose", action="store_true", help="list every case, not only misses")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args()

    cases = json.loads(Path(args.suite).read_text(encoding="utf-8"))["cases"]
    if not cases:
        print("INVALID RUN: suite has no cases", file=sys.stderr)
        return 2
    # The first query may rebuild a stale index; the rest must see that same index.
    results = [evaluate(c, rebuild=(i == 0)) for i, c in enumerate(cases)]

    if args.json:
        print(json.dumps(results, ensure_ascii=False, indent=2))
    invalid = [r for r in results if r["outcome"] == "INVALID"]
    failed = [r for r in results if r["outcome"] == "FAIL" and not r["knownGap"]]
    gaps = [r for r in results if r["outcome"] == "FAIL" and r["knownGap"]]
    improved = [r for r in results if r["outcome"] == "PASS" and r["knownGap"]]
    if not args.json:
        for r in results:
            if args.verbose or r["outcome"] != "PASS" or r["knownGap"]:
                tag = "GAP " if r["knownGap"] and r["outcome"] == "FAIL" else r["outcome"]
                where = f"rank {r['rank']}" if r.get("rank") else r.get("reason", "")
                print(f"{tag:8} {r['id']:36} {where}")
                if r["outcome"] != "PASS":
                    print(f"         got: {', '.join(r['paths'][:5]) or '-'}")
        passed = sum(r["outcome"] == "PASS" for r in results)
        print(f"\n{passed}/{len(results)} pass, {len(failed)} fail, {len(gaps)} known gap, "
              f"{len(improved)} known gap now passing, {len(invalid)} invalid")
    if invalid:
        return 2
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
