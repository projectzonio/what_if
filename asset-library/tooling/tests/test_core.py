import hashlib, json, tempfile, unittest, zipfile
from pathlib import Path
from assetlib.core import archive_report, duplicates, package_report, safe_member

class AssetLibTests(unittest.TestCase):
    def test_duplicate_sha(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d); (p/"a").write_bytes(b"x"); (p/"b").write_bytes(b"x")
            self.assertEqual(duplicates(p)["exact_duplicates"][0]["sha256"],hashlib.sha256(b"x").hexdigest())
    def test_archive_ok_and_corrupt(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/"ok.zip"
            with zipfile.ZipFile(p,"w") as z: z.writestr("safe/a.txt","ok")
            self.assertFalse(archive_report(p)["errors"])
            bad=Path(d)/"bad.zip"; bad.write_bytes(b"broken")
            self.assertTrue(archive_report(bad)["errors"])
    def test_traversal(self):
        self.assertFalse(safe_member("../evil")); self.assertFalse(safe_member("C:\\\\evil")); self.assertTrue(safe_member("folder/a"))
    def test_package_checks(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d); (p/"a").write_bytes(b"actual"); (p/"extra").write_bytes(b"extra")
            (p/"package-manifest.json").write_text(json.dumps({"files":[{"path":"a","sha256":"0"*64},{"path":"missing","sha256":"0"*64}]}))
            codes={x["code"] for x in package_report(p)["errors"]}
            self.assertTrue({"CHECKSUM_MISMATCH","PACKAGE_FILE_MISSING","UNEXPECTED_FILE"}<=codes)

if __name__=="__main__": unittest.main()
