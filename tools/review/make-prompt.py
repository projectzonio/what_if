"""Generate a worker / reviewer prompt file from agent_pool/shared/prompts/*.md and a brief's "Prepared handoff" .

  python -X utf8 -I tools/review/make-prompt.py start WI-006 [--ask "..."] [--parallel "..."] [--print]
  python -X utf8 -I tools/review/make-prompt.py rework WI-006
  python -X utf8 -I tools/review/make-prompt.py review WI-006

Writes agent_pool/shared/prompts/WI-0xx.md (start) / WI-0xx-rework.md / WI-0xx-review.md. Placeholders: $ticket $brief $worktree $ask_user $parallel_note.
The worktree comes from the brief's "Worktree / branch:" line (default D:/workspace/what_if-worktrees/claude-WI-0xx). Nothing is launched.
"""
import argparse, pathlib, re, string, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROMPTS = ROOT / "agent_pool" / "shared" / "prompts"
DEFAULT_ASK = ("Zeptej se mě jen na to, co AGENTS.md nechává uživateli: architektonická rozhodnutí/ADR, změna acceptance, licence a zdroje dat, instalace a stahování, cokoli na C:, push. "
               "Takové věci sepiš do outboxu jako blok na konec místo blokující otázky uprostřed práce.")
MODES = {"start": ("worker-start.md", "{t}.md"), "rework": ("worker-rework.md", "{t}-rework.md"), "review": ("orchestrator-review.md", "{t}-review.md")}


def handoff(text):
    m = re.search(r"^#+ Prepared handoff.*?$(.*?)(?=^#+ |\Z)", text, re.M | re.S)
    return m.group(1) if m else ""


def worktree_of(section, ticket):
    m = re.search(r"Worktree / branch:\*?\*?\s*`?([A-Za-z]:[/\\][^`\s,]+)", section)
    return m.group(1).replace("\\", "/").rstrip("/") if m else f"D:/workspace/what_if-worktrees/claude-{ticket}"


def build(mode, ticket, ask=None, parallel=None):
    brief = ROOT / "agent_pool" / "shared" / "briefs" / f"{ticket}.md"
    if not brief.exists():
        raise SystemExit(f"no brief {brief}")
    section = handoff(brief.read_text(encoding="utf-8"))
    if mode != "review" and not section:
        print(f"warning: {brief.name} has no 'Prepared handoff' section yet; default worktree path used", file=sys.stderr)
    tpl = string.Template((PROMPTS / MODES[mode][0]).read_text(encoding="utf-8"))
    return tpl.substitute(
        ticket=ticket, brief=f"agent_pool/shared/briefs/{ticket}.md", worktree=worktree_of(section, ticket),
        ask_user=ask or DEFAULT_ASK, parallel_note=(f"Souběžně běží: {parallel}. Drž se svých vlastněných cest.\n" if parallel else ""),
    ).rstrip() + "\n"


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("mode", choices=MODES)
    ap.add_argument("ticket", help="WI-0xx")
    ap.add_argument("--ask", help="replace the default 'ask the user only for' sentence")
    ap.add_argument("--parallel", help="parallel tickets sharing the notebook, e.g. 'WI-003 (Unity, launcher)'")
    ap.add_argument("--print", action="store_true", help="print only, write no file")
    a = ap.parse_args()
    out = build(a.mode, a.ticket, a.ask, a.parallel)
    if a.print:
        print(out, end="")
    else:
        dest = PROMPTS / MODES[a.mode][1].format(t=a.ticket)
        dest.write_text(out, encoding="utf-8")
        print(f"wrote {dest.relative_to(ROOT)}")
