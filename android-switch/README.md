# NSC Toolbox Switch — Android port (alpha foundation)

Independent Android proof-of-concept added **alongside** the original Windows WPF project.
The Windows project and its default branch remain unchanged. This folder targets
Naruto x Boruto Ultimate Ninja Storm Connections on Nintendo Switch, initially update 1.70.

## Implemented

- .NET 10 MAUI Android app shell and native Android file picker.
- Read-only XFBIN / NUCC header inspector with big-endian metadata parsing.
- Streaming read for non-seekable Android Storage Access Framework streams.
- Shareable JSON inspection report.
- Unit tests including malformed headers and non-seekable streams.
- GitHub Actions workflow to attempt a debug APK build and run core tests.

## NOT implemented / not verified

- No Switch-specific game parameter editors, no mutation or save of XFBIN, no CPK repack.
- No character, moveset, jutsu, icons, or LayeredFS exporter yet.
- Header inspection does not prove Switch-format parity with PC or in-game compatibility.
- **The APK build and Android runtime have not been verified.**

## Build in GitHub Actions

Pushing to an `android-switch/**` branch triggers the workflow.
Download the `nsc-toolbox-switch-android-debug` artifact from a successful workflow run.
It contains a debug/test APK only, not a signed release.

## Implementation sequence

1. Verify real stock Switch 1.70 XFBIN fixtures (never commit copyrighted game assets).
2. Confirm lossless round-trip and identifier preservation per parameter format.
3. Implement editors for `characode`, `playerSettingParam`, `duelPlayerParam`,
   `characterSelectParam`, and `player_icon` against verified schemas.
4. Add CPK handling with metadata/encoding preservation and regression tests.
5. Export ROMFS replacement structures supported by the chosen Switch loader.
6. Verify in an Android emulator or on hardware before claiming gameplay compatibility.

## Architecture

`src/NSCSwitch.Core` is UI-independent and has no WPF or Android dependency.
`src/NSCSwitch.Android` contains Android-only UI and file access code.
Upstream XFBIN_LIB is not copied automatically; cross-platform compatibility and
redistribution permission need separate review.

The upstream NSC Toolbox and XFBIN_LIB repos do not declare a license in their
repository metadata. Seek appropriate permission before redistributing derivatives
or upstream assets.
