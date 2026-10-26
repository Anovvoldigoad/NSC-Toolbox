# NSC Switch CPK Builder v0.7 — Android/Termux

Target: Naruto x Boruto Ultimate Ninja Storm Connections, Switch update 1.70.

These programs are Python 3 **standard-library-only**. They do not require a PC and do not distribute stock CPKs or proprietary binaries.

## What is implemented

- Read real CRI CPK files using UTF/TOC metadata without decompressing the whole archive (`cpk_inventory.py`).
- Route `Resources/Files/data/**/*.xfbin` from ZIP-format `.nsc` packages to the exact stock CPK that contains that resource (`route_nsc.py`).
- Report missing and duplicate/ambiguous file mappings; fail closed rather than guessing.
- Detect PC character or ModdingAPI dependencies and refuse a supposedly playable build.
- With separately installed Android-native FastCPK utilities, unpack, replace, rebuild, re-extract and SHA256-verify replacement bytes for **multiple** CPK files before atomically publishing a RomFS tree (`build_cpks.py`).
- Reject overwrites and keep original stock CPK files untouched.

## Requirements

Run in Termux with `pkg install python`. The `build_cpks.py` command additionally requires FastCPK and fastcpk-rebuild **already built for Android**. The official source is https://github.com/Al-Hydra/FastCPK. Specify the real executable paths: do not copy a Windows .exe into Android.

## Index one archive

```bash
python cpk_inventory.py /sdcard/Download/SWITCH/EXTRACTED_GAME/READY_TO_MOD/romfs/data/patch/170/common.cpk -o cpk_index.json
```

## Inspect routing safely

```bash
python route_nsc.py \
  --mod "/sdcard/Download/Isshiki Moveset Mod v1.2.1.nsc" \
  --cpk "/sdcard/Download/SWITCH/EXTRACTED_GAME/READY_TO_MOD/romfs/data/patch/170/common.cpk" \
  --output audit.json
```

Repeat `--cpk FILE.cpk` for every relevant **stock** archive. Some paths can legitimately exist in multiple archives or patch layers. This tool blocks ambiguity; it never assumes the latest file wins.

## Build only when routing has no unresolved resources or PC runtime requirements

```bash
python build_cpks.py \
  --mod "/sdcard/Download/YOUR_MOD.nsc" \
  --cpk "/sdcard/Download/SWITCH/EXTRACTED_GAME/READY_TO_MOD/romfs/data/patch/170/common.cpk" \
  --romfs data/patch/170/common.cpk \
  --fastcpk "$HOME/.cache/naruto-fastcpk/target/release/fastcpk" \
  --rebuild "$HOME/.cache/naruto-fastcpk/target/release/fastcpk-rebuild" \
  --output "/sdcard/Download/NSC_SWITCH_BUILD"
```

For multiple CPKs, repeat `--cpk` and matching `--romfs` options **in the same order**. Use the correct game archive destinations, not guesses. The compiler uses FastCPK template packing with CRILAYLA and Storm encryption. The game build file and executable patch dependencies must still match Switch 1.70.

**Do not use `--allow-unported` as evidence a mod is complete.** It intentionally produces a marked incomplete artifact.

## Verification and limitations

- Offline CI tests cover synthetic TOC, missing/duplicate resources, collision checks, and mocked FastCPK extraction/repack transaction handling.
- The local parser was independently exercised against a user-supplied 118,842,856-byte `common.cpk` with **489 indexed entries**.
- The user-supplied Isshiki mod has 28 loose candidate XFBIN in `Resources/Files`; 2 map to that one CPK. This DOES NOT mean only two XFBIN are valid globally.
- **No runtime gameplay test has been performed.** FastCPK has not been run with an authentic complete stock archive set in this integration.
- Full Windows ModdingAPI v2.1.2.1 is **not** ported to Switch ARM64; MovesetPlus, PC runtime DLLs, Lua hooks, scene transitions and projectile control are still missing.
- The output is **NOT an IPS patch**. For code modifications, a separately implemented ARM64 runtime or a verified build-ID-specific IPS is required. Atmosphere IPS patches apply to the **uncompressed** NSO representation, not directly to the compressed source.

## Status meanings

`READY_FOR_CPK_REBUILD`: exact filenames were located, but data semantics are not certified.

`CPKS_REEXTRACT_VERIFIED_NOT_GAMEPLAY_TESTED`: file-level rebuild and verification completed; gameplay remains unknown.

`BLOCKED_UNPORTED_MODDINGAPI` / `INCOMPLETE_UNPORTED_MODDINGAPI`: PC-native hooks remain unimplemented; do not advertise as playable.
