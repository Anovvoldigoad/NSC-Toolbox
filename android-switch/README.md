# NSC Toolbox Switch — Android port (read-only alpha 0.2.0)

This independent Android prototype lives **alongside** the original Windows WPF
application; `main` remains untouched. It targets Naruto x Boruto Ultimate Ninja
Storm Connections for Nintendo Switch (starting from update 1.70).

## Implemented

- Android .NET MAUI application, native file picker and standalone sideloadable APK.
- Read-only NUCC/XFBIN header and string-table explorer: NUCC packs, paths, names.
- Search/filter in each string table, capped UI preview (80 entries) with full JSON export.
- Bounded metadata parsing (8 MiB limit), big-endian section sizes, truncated input checks,
  non-seekable Android document stream support.
- Unit tests for normal/invalid/truncated XFBIN data and Android-style streams.
- GitHub Actions core tests, APK build and emulator startup smoke test.

## Not yet implemented

- No parameter writes, CPK rebuilding, game mod packaging or ROMFS export.
- No verified Switch-specific gameplay/animation/moveset format yet.
- Successful NUCC parsing is not evidence of Switch 1.70 gameplay compatibility.
- No copyrighted game files or test fixtures are checked into the repository.

## How to test on a phone

1. Download the latest artifact from the **push** GitHub Actions run on the
   `android-switch/mvp-foundation` branch; pick the standalone **Signed.apk**.
2. Install / update the application. If Android reports incompatible signing
   certificates, uninstall the prior test APK first (app-local data will be removed).
3. Use **Open XFBIN** to select an extracted Switch 1.70 file, not a .cpk.
4. Switch tabs between NUCC / Paths / Names and try filtering with a keyword.
5. Use **Share JSON report** to export all parsed entry names. The report does
   not contain the original binary file. It may still expose resource paths.
6. For invalid or unusually large metadata, the app displays an error rather
   than attempting to modify or recover the file.

## Follow-on implementation

- Validate inventory results against actual extracted **stock** Switch 1.70 assets.
- Build exact parameter schemas with binary round-trip tests, including untouched bytes.
- Add editable files one format at a time, then CPK rebuild with metadata preservation.
- Regression-test ROMFS outputs in emulator/on Switch before claiming compatibility.

## Upstream and attribution

The upstream NSC Toolbox and XFBIN_LIB repos expose no license in GitHub metadata.
Get appropriate permission before distributing derivative upstream source/assets;
this directory is independently implemented cross-platform prototype code.
