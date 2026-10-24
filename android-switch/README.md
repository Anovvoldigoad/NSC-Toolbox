# NSC Toolbox Switch for Android — 0.3.0 experimental tools

This independent Android prototype lives in `android-switch/` alongside
the existing Windows WPF toolbox. `main` is not modified.

## Implemented

- NUCC / XFBIN inventory explorer: view and search path/name/type string tables.
- Experimental byte-preserving **whole XFBIN swap** to a new ZIP.
  Two selected XFBIN filenames are retained but their **entire binary payloads**
  are exchanged. Internal character IDs, references, costumes, animation
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
2. Unzip and install `*-Signed.apk`; version 0.3.0.
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
