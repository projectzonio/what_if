"""Tests for brief-graph-check.py (RG-121). Run: python -X utf8 tools/knowledge/test_brief_graph_check.py"""
import importlib.util, os, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("bgc", os.path.join(HERE, "brief-graph-check.py"))
bgc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bgc)

BRIEF = """# T
### Exact write ownership (draft)
- New or owned: `Assets/Scripts/Streaming/WorldGenSectorSource.cs`, `wiki/**`, `agent_pool/shared/manual-tests/RG-1.json`
- `src/WhatIf.WorldGen/**`
- `tools/nothing/here.py` (new)
- Also allowed: `tools/worldgen/render_net.py`
- worker evidence: `agent_pool/agents/claude/{outbox,logs}/**`

Read-only:
- `src/What If.Nitro/**`
- everything else
"""


class T(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        os.makedirs(os.path.join(HERE, "..", "..", "artifacts", "tmp"), exist_ok=True)

    def brief(self):
        f = tempfile.NamedTemporaryFile("w", suffix=".md", delete=False, encoding="utf-8", dir=os.path.join(HERE, "..", "..", "artifacts", "tmp"))
        f.write(BRIEF)
        f.close()
        self.addCleanup(os.unlink, f.name)
        return f.name

    def test_heading_prefix_and_globs_and_plain_paths(self):
        owned = bgc.parse_ownership(self.brief(), False)
        self.assertIn("Assets/Scripts/Streaming/WorldGenSectorSource.cs", owned)
        self.assertIn("wiki/**", owned)
        self.assertIn("src/WhatIf.WorldGen/**", owned)       # plain bullet, no backticks mixed in
        self.assertIn("tools/worldgen/render_net.py", owned)     # 'Also allowed' counts as owned
        self.assertNotIn("src/What If.Nitro/**", owned)       # read-only block skipped
        self.assertTrue(any(o.startswith("agent_pool/agents/claude") for o in owned))

    def test_read_only_included_with_all(self):
        self.assertIn("src/What If.Nitro/**", bgc.parse_ownership(self.brief(), True))

    def test_covers_boundary(self):
        self.assertTrue(bgc.covers("src/WhatIf.WorldGen/", "src/WhatIf.WorldGen/Generator.cs", False))
        self.assertFalse(bgc.covers("tools/review", "tools/review-extra/x.py", False))
        self.assertTrue(bgc.covers("a/b/WorldGen.cs", "a/b/WorldGen", False))   # glob stem WorldGen*.cs

    def test_missing_is_reported(self):
        import subprocess
        out = subprocess.run([sys.executable, "-X", "utf8", os.path.join(HERE, "brief-graph-check.py"), self.brief(), "--strict"],
                             capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(out.returncode, 1)
        self.assertIn("MISSING", out.stdout)
        self.assertIn("`tools/nothing/here.py`", out.stdout)


if __name__ == "__main__":
    unittest.main()
