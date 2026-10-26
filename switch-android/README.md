# NSC Switch Compiler Android v0.5-native-preview

Buildable native APK (no PC needed for using the installed APK). Select a ZIP-based .nsc/.zip mod or a verified common.cpk; save a ZIP with Atmosphere-style paths, staged unsupported PC assets, and COMPILER_REPORT.json.

This is **not** a full ModdingAPI port. All Windows DLL/native-hook-dependent content stays under STAGING and is marked BLOCKED_UNPORTED_RESOURCES. IPS is deliberately not accepted in this APK because validation against the original NSO is required.

For multi-mod bundles and byte-audited IPS generation, use the Python Termux source from the full release archive (termux/bundle.py + ips_audit.py).

The official Mod Manager release metadata is checked automatically from GitHub; no user-supplied DLL is necessary for metadata, but Windows runtime does not magically become Switch ARM64 code.
