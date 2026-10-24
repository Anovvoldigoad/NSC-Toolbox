# NSC Toolbox Switch for Android — 0.4.0 experimental tools

This independent Android prototype lives in `android-switch/` alongside
the existing Windows WPF toolbox. `main` is not modified.

## Implemented

- NUCC / XFBIN inventory explorer: view and search path/name/type string tables.
- **XFBIN swap analyzer**: compare NUCC type, path and name tables before exporting,\n  with truncated previews for differences and explicit warnings.\n- Experimental byte-preserving **whole XFBIN swap** to a new ZIP.
  Two selected XFBIN filenames are retained but their **entire binary payloads**
  are exchanged. ZIP manifest includes per-output SHA-256 digests and the\n  analyzer's counts, previews and warnings. Internal character IDs, references, costumes, animation
  routing, parameter and CPK dependencies are **not** rewritten. This is not
  a validated moveset or model swap and may not be safe to install.
- Advanced **hex patch** to a new XFBIN, at a user-supplied offset, requiring
  exact expected old bytes and equal-length new bytes. No named jutsu/combo
  fields are supported yet. Input files are never modified.
- Interactive **3D model and animation viewer for self-contained GLB files**.
  WebGL viewer uses Babylon.js and online loaders over HTTPS; orbit/touch camera,
  animations and skin/skeleton rendering for compatible GLB models. A 20 MiB
  file-preview limit protects Android memory.
- Standalone Android APK, core tests, and emulator startup test on GitHub Actions.

## Important limitations

- **Direct XFBIN model/animation preview is NOT implemented.** Input for 3D viewer
  must already be converted into .glb (glTF binary). The UI must not pretend to
  show native XFBIN assets.
- No verified Switch 1.70 parameter serializer, named moveset editor, or
  cross-character internal reference remapper.
- No CPK extraction/rebuild or LayeredFS installer in the app yet.
- Neither whole-file swap nor byte patch ensures a working game mod.
- Internet connectivity is required for the current Babylon.js viewer.
- No copyrighted game XFBIN assets are bundled or uploaded.

## Workflow on Android

1. Download the `nsc-toolbox-switch-android-debug` artifact from a successful
   **push** workflow run on `android-switch/mvp-foundation`.
2. Unzip and install `*-Signed.apk`; version 0.4.0.
3. In the app choose NUCC Explorer, experimental full-file swap, expert hex patch,
   or GLB preview.
4. The swap ZIP contains `SWAP_MANIFEST.json` with warnings and maps.
5. In the viewer, use Play/Pause and choose an animation clip if your GLB
   includes animations.

## Next: true Switch moveset/model swap and native 3D XFBIN playback

Real support requires **actual Switch 1.70** resource samples with known
relationships (stock and a corresponding modded pair) and verified schemas for
PRM/animation/body assets. First build round-trip and resource-ID mapping tests,
then add semantic editors and native mesh/skeleton/animation decoding.

Upstream NSC Toolbox and XFBIN_LIB have no license metadata. Seek permission
before redistributing upstream-derived files and assets.


## Switch 1.70 NUCC layout verification

Inspection of 15 extracted XFBIN files from a user-provided dependency archive
confirmed that table counters at offsets 0x24 and 0x2C include a sentinel
entry. The NUCC type counter at 0x1C does not. The inventory and header
readers now expose actual resource string counts, excluding both sentinels.
The presence of nuccChunkModel, nuccChunkClump or nuccChunkAnm in a file
only signals a declared chunk type; model/animation decoding is still absent.
The original game assets are not committed.


## XFBIN resource chunks (read-only 0.4.0)

- **Resource Chunk Browser** enumerates actual chunk headers across NUCC pages,
  resolves type/name/path via the binary map, and reports data offsets and lengths.
- Works with streaming Android file-picker input without loading the complete XFBIN
  into memory, with metadata and individual chunk size limits.
- Allows exporting **individual raw chunk payloads** to `.bin` for offline analysis.
  It does not turn model or animation chunks into renderable 3D objects.
- Tested against synthetic fixtures modelling real Switch chunk/page layouts;
  15 real user-supplied Switch 1.70 XFBIN samples were independently inspected
  without committing copyrighted assets.
- Corrects first-chunk offset calculation to include 8 bytes for each
  extra NUCC mapping (header 0x40), which is needed for effect/model XFBINs.
