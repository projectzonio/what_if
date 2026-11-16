"""Branch diff vs the ticket brief: owned / read-only / unlisted paths plus the hygiene checks every review repeated by hand.

Ownership comes from the brief's "### Exact write ownership" section: backticked paths in the New/Owned/Also allowed bullets are owned, those under Read-only are
read-only. `Assets/...` and `ProjectSettings/...` are relative to engine-unity/NightRacer; a bare file name (`GameBuilder.cs`) matches any directory;
`**` matches across folders. Parsing prose is heuristic: an "unlisted" path is for the reviewer to judge (exit 0), a touched read-only path fails.

  python -X utf8 -I tools/review/branch-scope.py --worktree <worker-worktree> --brief agent_pool/shared/briefs/RG-0xx.md [--base <rev>] [--own <glob> ...]

Fails on: read-only path touched, binaries (dll/exe/pdb/kn5/zip/fbx/blend), Unity caches, files > 1 MB, pool files on the branch,
a tracked .meta deleted without its asset, or a dirty worktree.
"""
import argparse, fnmatch, pathlib, re, subprocess, sys

ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
ap.add_argument("--worktree", required=True)
ap.add_argument("--brief", required=True)
ap.add_argument("--base", help="base revision (default: merge-base HEAD main)")
ap.add_argument("--head", default="HEAD", help="last revision of the ticket (default HEAD)")
ap.add_argument("--own", action="append", default=[], help="extra owned glob (repo-relative)")
a = ap.parse_args()
wt = pathlib.Path(a.worktree)


def git(*args):
    return subprocess.run(["git", "-C", str(wt), *args], capture_output=True, text=True, check=True).stdout


def section(text):
    m = re.search(r"^### Exact write ownership\s*$(.*?)(?=^#{1,3} |^\*\*[A-Z][^*]*:\*\*|\Z)", text, re.M | re.S)
    if not m:
        sys.exit("brief has no '### Exact write ownership' section: pass --own globs or review by hand")
    owned, ro, mode = [], [], None
    for line in m.group(1).splitlines():
        head = line.lstrip("- ").lower()
        if not line.startswith(" "):   # top-level bullet decides the mode; nested bullets inherit it
            mode = "ro" if head.startswith("read-only") else "own" if head.startswith(("new", "owned", "own", "also allowed")) else mode
        if mode is None:   # the explanatory preamble names tools and `engine-unity/NightRacer`; counting it made every engine path owned
            continue
        for tok in re.findall(r"`([^`]+)`", line):
            if "/" in tok or "." in tok:
                (ro if mode == "ro" else owned).append(tok)
    return owned, ro


def to_regex(glob):
    g = glob.strip().rstrip(";,.")
    if g.startswith(("Assets/", "ProjectSettings")):
        g = "engine-unity/NightRacer/" + g
    if "/" not in g:
        g = "**/" + g
    if g.endswith("/"):
        g += "**"
    tr = {"**/": "(.*/)?", "**": ".*", "*": "[^/]*"}
    return re.compile("^" + "".join(tr.get(part, re.escape(part)) for part in re.split(r"(\*\*/|\*\*|\*)", g)) + "$")


brief = pathlib.Path(a.brief).read_text(encoding="utf-8")
owned_g, ro_g = section(brief)
owned_g += a.own
owned_re, ro_re = [to_regex(g) for g in owned_g], [to_regex(g) for g in ro_g]
base = a.base or git("merge-base", a.head, "main").strip()
status = [l.split("\t") for l in git("diff", "--name-status", "--no-renames", f"{base}..{a.head}").splitlines()]
changed = [p for _, p in status]
deleted = [p for s, p in status if s == "D"]
fails = 0


def report(name, bad, fatal=True):
    global fails
    print(("FAIL" if bad and fatal else "WARN" if bad else "PASS"), name, "::", ", ".join(bad) if bad else "")
    fails += 1 if bad and fatal else 0


print(f"INFO base {base[:10]}, {len(changed)} changed path(s), owned globs {len(owned_re)}, read-only globs {len(ro_re)}")
def matches(p, res):   # X.meta belongs with X; a folder .meta with the folder's contents
    probes = [p, p[:-5], p[:-5] + "/x"] if p.endswith(".meta") else [p]
    return any(r.match(q) for r in res for q in probes)


ro_hit = [p for p in changed if matches(p, ro_re) and not matches(p, owned_re)]
unlisted = [p for p in changed if not matches(p, owned_re) and p not in ro_hit]
report("no read-only path touched", ro_hit)
report("every changed path listed as owned (heuristic, judge by hand)", unlisted, fatal=False)
report("no binaries", [p for p in changed if re.search(r"\.(dll|exe|pdb|kn5|zip|7z|fbx|blend|unitypackage)$", p, re.I)])
report("no Unity/build caches", [p for p in changed if re.search(r"(^|/)(Library|Temp|Logs|obj|bin|UserSettings|artifacts)/", p)])
present = [p for s, p in status if s != "D"]
report("no file > 1 MB", [f"{p} ({n} B)" for p in present for n in [int(git("cat-file", "-s", f"{a.head}:{p}"))] if n > 1_000_000])
report("no pool files on the branch", [p for p in changed if re.search(r"(task_pool\.json|TASKS\.md|_pool\.md|^agent_pool/(tasks|inbox|state)/)", p)])
gone = set(deleted)
report("no .meta deleted without its asset", [p for p in deleted if p.endswith(".meta") and p[:-5] not in gone and not any(d.startswith(p[:-5] + "/") for d in gone)])
if a.head == "HEAD":
    report("worktree clean", git("status", "--short", "--untracked-files=normal").splitlines())
print(f"RESULT {'PASS' if not fails else 'FAIL'} failures={fails} unlisted={len(unlisted)}")
sys.exit(1 if fails else 0)
