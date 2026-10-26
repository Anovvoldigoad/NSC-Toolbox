#!/usr/bin/env python3
"""CI integration with the *real* pinned FastCPK Rust binaries.

Uses generated CRI CPK fixtures only: no copyrighted NSC game assets are included.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

from build_cpks import build
from auto_compile import build_auto
from cpk_inventory import inventory
from route_nsc import route


def digest(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def cmd(*args):
    p = subprocess.run([str(x) for x in args], capture_output=True, text=True, timeout=180)
    if p.returncode:
        raise AssertionError(f"command failed: {' '.join(map(str, args))}\n"
                             f"stdout: {p.stdout[-2000:]}\nstderr: {p.stderr[-2000:]}")
    return p.stdout


def assert_contents(fastcpk, archive, wanted, dest):
    cmd(fastcpk, "--game", "cc2-storm", "-o", dest, archive)
    for relative, data in wanted.items():
        candidate = dest / relative
        assert candidate.is_file(), f"missing file: {candidate}"
        got = candidate.read_bytes()
        assert got == data, f"content mismatch {relative} {len(got)}!={len(data)}"


def verify_case(fastcpk, rebuild):
    with tempfile.TemporaryDirectory(prefix="nsc_real_cpk_integration_") as d:
        root = Path(d)
        left = root / "left" / "data"
        right = root / "right" / "data"
        (left / "spc").mkdir(parents=True)
        (right / "effect").mkdir(parents=True)
        untouched = b"NUCC" + (b"U" * 0x5000)
        replaced_old = b"NUCC" + (b"A" * 0x4000)
        replaced_new = b"NUCC" + (b"B" * 0x6000)
        effect_old = b"NUCC" + (b"E" * 0x2000)
        effect_new = b"NUCC" + (b"Z" * 0x3000)
        (left / "spc" / "original.xfbin").write_bytes(replaced_old)
        (left / "spc" / "untouched.xfbin").write_bytes(untouched)
        (right / "effect" / "blast.xfbin").write_bytes(effect_old)
        a, b = root / "stock_a.cpk", root / "stock_b.cpk"
        cmd(rebuild, "pack", left, "--output", a, "--crilayla", "--encrypt-storm")
        cmd(rebuild, "pack", right, "--output", b, "--crilayla", "--encrypt-storm")
        stock_digest_a, stock_digest_b = digest(a), digest(b)
        ia, ib = inventory(a), inventory(b)
        assert ia["file_count"] == 2, ia
        assert ib["file_count"] == 1, ib
        assert {x["path"] for x in ia["entries"]} == {
            "data/spc/original.xfbin", "data/spc/untouched.xfbin"
        }, ia
        mod = root / "test_multi_resource.nsc"
        with zipfile.ZipFile(mod, "w", compression=zipfile.ZIP_DEFLATED) as z:
            z.writestr("Resources/Files/data/spc/original.xfbin", replaced_new)
            z.writestr("Resources\\Files\\data\\effect\\blast.xfbin", effect_new)
            z.writestr("mod_config.ini", "[General]\nname=CI test\n")
        routing = route([mod], [a, b])
        assert routing["status"] == "READY_FOR_CPK_REBUILD", routing
        out = root / "compiled"
        labels = ["data/patch/170/common.cpk", "data/patch/170/other.cpk"]
        report = build([mod], [a, b], labels, fastcpk, rebuild, out)
        assert report["status"] == "CPKS_REEXTRACT_VERIFIED_NOT_GAMEPLAY_TESTED", report
        assert len(report["archives"]) == 2, report
        assert sum(x["untouched_files_verified"] for x in report["archives"]) == 1, report
        output_a = out / "atmosphere/contents/010084D00CF5E000/romfs" / labels[0]
        output_b = out / "atmosphere/contents/010084D00CF5E000/romfs" / labels[1]
        assert_contents(fastcpk, output_a, {
            "data/spc/original.xfbin": replaced_new,
            "data/spc/untouched.xfbin": untouched
        }, root / "verify_a")
        assert_contents(fastcpk, output_b, {
            "data/effect/blast.xfbin": effect_new
        }, root / "verify_b")
        assert (out / "COMPILER_REPORT.json").exists()
        assert digest(a) == stock_digest_a and digest(b) == stock_digest_b, "stock CPK modified"
        # Auto-discovery: same real encrypted stock archives beneath RomFS/data.
        romfs = root / "game_romfs"
        target_a=romfs / "data" / "patch" / "170" / "common.cpk"
        target_b=romfs / "data" / "patch" / "170" / "other.cpk"
        target_a.parent.mkdir(parents=True)
        shutil.copyfile(a, target_a)
        shutil.copyfile(b, target_b)
        auto_result=build_auto([mod],romfs,fastcpk,rebuild,root/"auto_compiled")
        assert auto_result["auto_discovery"]["archive_count_built"] == 2
        assert_contents(fastcpk,root/"auto_compiled"/"atmosphere/contents/010084D00CF5E000/romfs"/labels[0],{
            "data/spc/original.xfbin":replaced_new,
            "data/spc/untouched.xfbin":untouched
        },root/"verify_auto_a")
        assert_contents(fastcpk,root/"auto_compiled"/"atmosphere/contents/010084D00CF5E000/romfs"/labels[1],{
            "data/effect/blast.xfbin":effect_new
        },root/"verify_auto_b")
        assert digest(target_a)==stock_digest_a and digest(target_b)==stock_digest_b
        print(json.dumps({"status":"PASS_REAL_FASTCPK",
                          "archives":len(report["archives"]),
                          "replacements":report["route_count"],
                          "auto_discovery_archives_built":auto_result["auto_discovery"]["archive_count_built"],
                          "untouched_files_verified":sum(x["untouched_files_verified"]
                                                         for x in report["archives"]),
                          "source_sha256":{a.name:digest(a), b.name:digest(b)},
                          "output_sha256":{output_a.name:digest(output_a),output_b.name:digest(output_b)}},
                         indent=2))


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--fastcpk", required=True)
    p.add_argument("--rebuild", required=True)
    args = p.parse_args()
    try:
        verify_case(Path(args.fastcpk).resolve(), Path(args.rebuild).resolve())
    except Exception as ex:
        print(f"FAIL_REAL_FASTCPK: {ex}", file=sys.stderr)
        raise
