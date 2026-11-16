#!/usr/bin/env python3
"""Loop economics of Claude Code sessions, from the raw JSONL transcripts.

A "loop" is a run of API requests with no end_turn between them. Every request re-reads the whole
context from the cache, so the cost of a loop grows with requests x context size. Waiting does not
cost tokens by itself; what matters is how many requests are spent and how big the context is.

  python -X utf8 -I tools/knowledge/loop-stats.py --since 2026-10-27                 one row per session
  python -X utf8 -I tools/knowledge/loop-stats.py --session <id-prefix> --detail     loops + long waits
"""
import argparse
import glob
import json
import os
import sys
from datetime import datetime

ROOT = os.path.expanduser("~/.claude/projects")
PROJECT_GLOB = "D--workspace-what-if*"
LONG_WAIT_S = 120


def parse_time(s):
    return datetime.fromisoformat(s.replace("Z", "+00:00")) if s else None


def user_kind(msg):
    """prompt | notification | tool_result | meta"""
    c = msg.get("content")
    if isinstance(c, str):
        text = c
    else:
        blocks = c or []
        if any(b.get("type") == "tool_result" for b in blocks if isinstance(b, dict)):
            return "tool_result"
        text = " ".join(b.get("text", "") for b in blocks if isinstance(b, dict))
    t = text.lstrip()
    if t.startswith("<task-notification"):
        return "notification"
    if t.startswith("<system-reminder") or t.startswith("<command-") or t.startswith("<local-command"):
        return "meta"
    return "prompt"


def load(path):
    """Requests (deduplicated by message id, a message spans several JSONL lines) and user events, in order."""
    events, seen = [], {}
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            try:
                o = json.loads(line)
            except ValueError:
                continue
            if o.get("isSidechain"):
                continue
            kind = o.get("type")
            ts = parse_time(o.get("timestamp"))
            m = o.get("message") or {}
            if kind == "user":
                events.append({"t": "user", "kind": "meta" if o.get("isMeta") else user_kind(m), "ts": ts})
            elif kind == "assistant" and m.get("usage") and m.get("model") != "<synthetic>":
                r = seen.get(m.get("id"))
                if r is None:
                    r = {"t": "req", "ts": ts, "end": ts, "model": m.get("model"), "stop": None, "tools": [], "usage": m["usage"]}
                    seen[m.get("id")] = r
                    events.append(r)
                r["end"] = ts
                r["usage"] = m["usage"]
                if m.get("stop_reason"):
                    r["stop"] = m["stop_reason"]
                for b in m.get("content") or []:
                    if isinstance(b, dict) and b.get("type") == "tool_use":
                        r["tools"].append(b.get("name"))
    return events


def ctx(r):
    u = r["usage"]
    return u.get("input_tokens", 0) + u.get("cache_read_input_tokens", 0) + u.get("cache_creation_input_tokens", 0)


def loops_of(events):
    """Split requests into loops. A loop ends on end_turn, or when a prompt/notification arrives."""
    loops, cur, trigger = [], None, "start"
    for e in events:
        if e["t"] == "user":
            if e["kind"] in ("prompt", "notification"):
                if cur:
                    loops.append(cur)
                    cur = None
                trigger = e["kind"]
            continue
        if cur is None:
            cur = {"trigger": trigger, "reqs": [], "start": e["ts"]}
        cur["reqs"].append(e)
        if e["stop"] == "end_turn":
            loops.append(cur)
            cur = None
            trigger = "continue"
    if cur:
        loops.append(cur)
    return loops


def stats(events):
    reqs = [e for e in events if e["t"] == "req"]
    if not reqs:
        return None
    loops = loops_of(events)
    tools = [t for r in reqs for t in r["tools"]]
    return {
        "model": max({r["model"] for r in reqs}, key=lambda m: sum(1 for r in reqs if r["model"] == m)),
        "requests": len(reqs),
        "loops": len(loops),
        "prompts": sum(1 for e in events if e["t"] == "user" and e["kind"] == "prompt"),
        "notifications": sum(1 for e in events if e["t"] == "user" and e["kind"] == "notification"),
        "longest": max(len(l["reqs"]) for l in loops),
        "max_ctx": max(ctx(r) for r in reqs),
        "cache_read": sum(r["usage"].get("cache_read_input_tokens", 0) for r in reqs),
        "cache_write": sum(r["usage"].get("cache_creation_input_tokens", 0) for r in reqs),
        "output": sum(r["usage"].get("output_tokens", 0) for r in reqs),
        "monitor": tools.count("Monitor"),
        "wall_h": (reqs[-1]["end"] - reqs[0]["ts"]).total_seconds() / 3600,
    }


def detail(events):
    print("\nloops (trigger, requests, context at start -> end, cache read, output, minutes):")
    for i, l in enumerate(loops_of(events), 1):
        rs = l["reqs"]
        read = sum(r["usage"].get("cache_read_input_tokens", 0) for r in rs)
        out = sum(r["usage"].get("output_tokens", 0) for r in rs)
        mins = (rs[-1]["end"] - rs[0]["ts"]).total_seconds() / 60
        print(f"  {i:3d} {l['trigger']:<12} {len(rs):4d} req  {ctx(rs[0]) // 1000:4d}k -> {ctx(rs[-1]) // 1000:4d}k  read {read / 1e6:6.2f}M  out {out // 1000:4d}k  {mins:6.1f} min")
    reqs = [e for e in events if e["t"] == "req"]
    print(f"\nwaits >= {LONG_WAIT_S} s (the tool the request ended on, then what woke the session):")
    for a, b in zip(reqs, reqs[1:]):
        gap = (b["ts"] - a["end"]).total_seconds()
        if gap >= LONG_WAIT_S:
            between = [e["kind"] for e in events if e["t"] == "user" and a["end"] <= e["ts"] <= b["ts"] and e["kind"] != "meta"]
            print(f"  {gap / 60:6.1f} min  ctx {ctx(a) // 1000:4d}k  after {','.join(a['tools']) or a['stop']}  woken by {between[-1] if between else '?'}")


def context_fill(path, top=12):
    """What fills the context: the biggest tool results, and every Monitor with the wakes it caused."""
    uses, results, monitors, notes, images = {}, [], [], [], 0
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            try:
                o = json.loads(line)
            except ValueError:
                continue
            if o.get("isSidechain"):
                continue
            c = (o.get("message") or {}).get("content")
            if isinstance(c, str):
                if c.lstrip().startswith("<task-notification"):
                    notes.append(c)
                continue
            for b in c or []:
                if not isinstance(b, dict):
                    continue
                if b.get("type") == "tool_use":
                    uses[b["id"]] = b
                    if b.get("name") == "Monitor":
                        monitors.append(b.get("input") or {})
                elif b.get("type") == "tool_result":
                    rc = b.get("content")
                    # base64 of an image is not its token cost (an image is ~2k tokens), so only text counts
                    size = len(rc) if isinstance(rc, str) else sum(len(x.get("text", "")) for x in rc or [] if isinstance(x, dict))
                    images += sum(1 for x in rc if isinstance(x, dict) and x.get("type") == "image") if isinstance(rc, list) else 0
                    u = uses.get(b.get("tool_use_id"), {})
                    i = u.get("input") or {}
                    results.append((size, u.get("name", "?"), str(i.get("command") or i.get("file_path") or i.get("pattern") or i)[:110].replace("\n", " ")))
                elif b.get("type") == "text" and b.get("text", "").lstrip().startswith("<task-notification"):
                    notes.append(b["text"])
    total = sum(r[0] for r in results)
    by_tool = {}
    for size, name, _ in results:
        by_tool[name] = by_tool.get(name, 0) + size
    print(f"\ntool results: {total // 1000}k text chars (~{total // 4000}k tokens) + {images} images: " + ", ".join(f"{k} {v // 1000}k" for k, v in sorted(by_tool.items(), key=lambda kv: -kv[1])))
    for size, name, what in sorted(results, reverse=True)[:top]:
        print(f"  {size // 1000:5d}k {name:<11} {what}")
    print(f"\nMonitor calls: {len(monitors)}, notifications: {len(notes)}")
    for m in monitors:
        print(f"  persistent={m.get('persistent')} timeout_ms={m.get('timeout_ms')} {str(m.get('description'))[:70]} :: {str(m.get('command'))[:170]}")
    for n in notes[:40]:
        print("  note:", " ".join(n.split())[:190])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--project", default=PROJECT_GLOB, help="glob of the project folders under ~/.claude/projects")
    ap.add_argument("--since", help="YYYY-MM-DD: sessions modified since")
    ap.add_argument("--session", help="session id prefix")
    ap.add_argument("--detail", action="store_true")
    ap.add_argument("--min-requests", type=int, default=40)
    a = ap.parse_args()
    files = glob.glob(os.path.join(ROOT, a.project, "*.jsonl"))
    if a.session:
        files = [f for f in files if os.path.basename(f).startswith(a.session)]
    if a.since:
        limit = datetime.fromisoformat(a.since).timestamp()
        files = [f for f in files if os.path.getmtime(f) >= limit]
    print("session  where              model      req loops longest maxctx  read_M write_M out_k monitor notif wall_h")
    for f in sorted(files, key=os.path.getmtime):
        events = load(f)
        s = stats(events)
        if not s or s["requests"] < a.min_requests and not a.session:
            continue
        where = os.path.basename(os.path.dirname(f)).replace("D--workspace-what-if", "").replace("-worktrees-claude-", "").lstrip("-") or "main"
        print(f"{os.path.basename(f)[:8]} {where[:18]:<18} {s['model'].replace('claude-', '')[:10]:<10} {s['requests']:4d} {s['loops']:5d} {s['longest']:7d} {s['max_ctx'] // 1000:5d}k {s['cache_read'] / 1e6:7.1f} {s['cache_write'] / 1e6:7.2f} {s['output'] // 1000:5d} {s['monitor']:7d} {s['notifications']:5d} {s['wall_h']:6.1f}")
        if a.detail:
            detail(events)
            context_fill(f)
    return 0


if __name__ == "__main__":
    sys.exit(main())
