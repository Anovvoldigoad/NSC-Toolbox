#!/usr/bin/env python3
"""Safe auto-discovery tests; extracted game assets are not needed."""
import tempfile
import unittest
from pathlib import Path
from cpk_inventory import FormatError
from test_routing import cpk, nsc
from auto_compile import plan, discover_romfs, build_auto


class AutoRouteTests(unittest.TestCase):
    def make_romfs(self, base):
        root = base / "romfs"
        (root / "data" / "patch" / "170").mkdir(parents=True)
        (root / "data" / "main").mkdir(parents=True)
        return root

    def test_discovers_and_routes_matching_cpk(self):
        with tempfile.TemporaryDirectory() as d:
            t = Path(d);romfs = self.make_romfs(t)
            a=cpk(romfs/"data"/"patch"/"170","common.cpk",["data/spc/a.xfbin"])
            b=cpk(romfs/"data"/"main","other.cpk",["data/effect/b.xfbin"])
            mod=nsc(t,"one.nsc",{"data/spc/a.xfbin":b"replacement"})
            result=plan([mod],romfs)
            self.assertEqual(result["archives_scanned"],2)
            self.assertEqual(len(result["archives_to_rebuild"]),1)
            self.assertEqual(result["archives_to_rebuild"][0]["stock"],str(a))
            self.assertEqual(result["archives_to_rebuild"][0]["romfs_target"],"data/patch/170/common.cpk")

    def test_detect_duplicate_cross_archive(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);root=self.make_romfs(t)
            cpk(root/"data"/"patch"/"170","common.cpk",["data/spc/a.xfbin"])
            cpk(root/"data"/"main","other.cpk",["data/spc/a.xfbin"])
            mod=nsc(t,"one.nsc",{"data/spc/a.xfbin":b"replacement"})
            report=plan([mod],root)
            self.assertEqual(report["status"],"BLOCKED_UNKNOWN_OR_DUPLICATE_CPK")
            self.assertEqual(len(report["ambiguous_resources"]),1)
            with self.assertRaises(FormatError):
                build_auto([mod],root,__file__,__file__,t/"output")

    def test_block_unported_character(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);root=self.make_romfs(t)
            cpk(root/"data"/"patch"/"170","common.cpk",["data/spc/a.xfbin"])
            mod=nsc(t,"one.nsc",{"data/spc/a.xfbin":b"A"},pc=True)
            report=plan([mod],root)
            self.assertEqual(report["status"],"BLOCKED_UNPORTED_MODDINGAPI")
            with self.assertRaises(FormatError):
                build_auto([mod],root,__file__,__file__,t/"output")

    def test_reject_non_romfs_root(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(FormatError):
                discover_romfs(d)

    def test_no_cpk(self):
        with tempfile.TemporaryDirectory() as d:
            t=Path(d);root=self.make_romfs(t)
            with self.assertRaises(FormatError):
                discover_romfs(root)


if __name__ == "__main__":
    unittest.main()
