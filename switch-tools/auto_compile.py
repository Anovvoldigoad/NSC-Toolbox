#!/usr/bin/env python3
"""Discover original Switch CPK files under a RomFS tree and build only needed archives.

No guessing archive priority: ambiguously duplicated XFBIN paths cause a hard stop.
Uses genuine installed FastCPK extractor/rebuilder and retains original stock.
"""
import argparse
import json
import sys
from pathlib import Path

from build_cpks import build, safe_romfs_target
from cpk_inventory import FormatError
from route_nsc import route

MAX_ARCHIVES = 256


def discover_romfs(romfs_root):
    root = Path(romfs_root).expanduser().resolve(strict=True)
    if not root.is_dir() or not (root / "data").is_dir():
        raise FormatError("RomFS root must be a directory containing data/")
    found = []
    for candidate in (root / "data").rglob("*"):
        if not candidate.is_file() or candidate.suffix.casefold() != ".cpk":
            continue
        if candidate.is_symlink():
            raise FormatError("Refusing symlinked CPK")
        resolved = candidate.resolve(strict=True)
        if not resolved.is_relative_to(root):
            raise FormatError("CPK escapes RomFS root")
        label = safe_romfs_target(candidate.relative_to(root).as_posix())
        found.append((resolved, label))
        if len(found) > MAX_ARCHIVES:
            raise FormatError("Too many CPKs discovered (limit 256)")
    found.sort(key=lambda item: item[1].casefold())
    if not found:
        raise FormatError("No CPK archives found in RomFS/data/")
    return found


def plan(mods, romfs_root):
    discovered = discover_romfs(romfs_root)
    stock_paths = [p for p, _ in discovered]
    scan = route(mods, stock_paths)
    available = {str(p): label for p, label in discovered}
    selected = [
        {"stock": str(path), "romfs_target": available[str(path)],
         "replacement_count": len(scan["replacements"][str(path)])}
        for path in stock_paths if str(path) in scan["replacements"]
    ]
    return {
        "plan_format": "nsc_switch_auto_cpk_v1",
        "status": scan["status"],
        "archives_scanned": len(discovered),
        "archives_to_rebuild": selected,
        "matched_resources": scan["matched_count"],
        "duplicate_identical_resources": scan["duplicate_count"],
        "missing_resources": scan["missing"],
        "ambiguous_resources": scan["ambiguous"],
        "unported_runtime_dependencies": scan["unported_runtime_dependencies"],
        "warning": "Archive lookup is exact. Gameplay ModdingAPI ARM64 is NOT implemented."
    }


def build_auto(mods, romfs_root, fastcpk, rebuild, output, *, allow_unported=False):
    root=Path(romfs_root).resolve(strict=True)
    destination=Path(output).expanduser().resolve()
    if destination==root or destination.is_relative_to(root):
        raise FormatError("Output must be outside the original extracted game RomFS")
    result = plan(mods, romfs_root)
    if result["missing_resources"] or result["ambiguous_resources"]:
        raise FormatError("Missing or ambiguous resources; see dry-run report")
    if result["unported_runtime_dependencies"] and not allow_unported:
        raise FormatError("Mod has PC/character dependencies; use --dry-run to inspect")
    if not result["archives_to_rebuild"]:
        raise FormatError("No resources can be placed into stock CPKs")
    selected = result["archives_to_rebuild"]
    report = build(
        mods,
        [x["stock"] for x in selected],
        [x["romfs_target"] for x in selected],
        fastcpk, rebuild, output,
        allow_unported=allow_unported
    )
    report["auto_discovery"] = {
        "archives_scanned": result["archives_scanned"],
        "archive_count_built": len(selected)
    }
    # The transactional builder already writes a report into output. Update its
    # metadata atomically in a sibling temporary file only after build succeeded.
    target = Path(output).resolve() / "COMPILER_REPORT.json"
    old = json.loads(target.read_text(encoding="utf-8"))
    old["auto_discovery"] = report["auto_discovery"]
    import os
    tmp = target.with_name("COMPILER_REPORT.json.new")
    try:
        tmp.write_text(json.dumps(old, indent=2, ensure_ascii=False), encoding="utf-8")
        os.replace(tmp, target)
    finally:
        tmp.unlink(missing_ok=True)
    return report


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--romfs-root", required=True, help="Path to extracted game RomFS containing data/")
    p.add_argument("--mod", action="append", required=True, help="Mod .nsc (repeatable)")
    p.add_argument("--dry-run", action="store_true", help="Only report mapped/missing/ambiguous assets")
    p.add_argument("--fastcpk", help="Path to Android-native FastCPK extractor")
    p.add_argument("--rebuild", help="Path to Android-native fastcpk-rebuild")
    p.add_argument("--output", help="Result directory (or report JSON for dry-run)")
    p.add_argument("--allow-unported", action="store_true", help="Mark incomplete; never certify playable")
    args = p.parse_args()
    try:
        if args.dry_run:
            result = plan(args.mod, args.romfs_root)
            message = json.dumps(result, indent=2, ensure_ascii=False)
            if args.output:
                Path(args.output).write_text(message, encoding="utf-8")
            else:
                print(message)
        else:
            if not args.fastcpk or not args.rebuild or not args.output:
                p.error("--fastcpk, --rebuild and --output required without --dry-run")
            result = build_auto(args.mod, args.romfs_root,
                                args.fastcpk, args.rebuild, args.output,
                                allow_unported=args.allow_unported)
            print(json.dumps(result, indent=2, ensure_ascii=False))
    except (FormatError, OSError, ValueError) as e:
        sys.exit("ERROR: " + str(e))


if __name__ == "__main__":
    main()
