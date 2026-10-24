#!/usr/bin/env bash
set -euo pipefail

APP_ID="io.github.anovvoldigoad.nsctoolboxswitch"
mapfile -t APK_FILES < <(find android-apks -type f -name '*.apk' | sort)

if (( ${#APK_FILES[@]} == 0 )); then
  echo "::error::No APK found in downloaded workflow artifact"
  find android-apks -maxdepth 4 -type f || true
  exit 1
fi

printf 'APK candidates:\n%s\n' "${APK_FILES[@]}"
APK=""
for item in "${APK_FILES[@]}"; do
  if [[ "$item" == *-Signed.apk ]]; then
    APK="$item"
    break
  fi
done
if [[ -z "$APK" ]]; then
  APK="${APK_FILES[0]}"
fi
echo "Installing $APK"
adb install -r "$APK"
adb logcat -c

# The package can launch only if the generated Android manifest has a launcher activity.
adb shell am force-stop "$APP_ID" || true
adb shell monkey -p "$APP_ID" -c android.intent.category.LAUNCHER 1 || true
sleep 12

PID="$(adb shell pidof "$APP_ID" | tr -d '\r' || true)"
if [[ -z "$PID" ]]; then
  echo "::error::App not running 12 seconds after launch. Collecting crash logs."
  adb logcat -d -b crash -v time || true
  adb logcat -d -v time -s AndroidRuntime:E mono-rt:E monodroid:E DOTNET:E NSCSwitch:E '*:S' || true
  adb shell dumpsys activity activities | tail -n 90 || true
  exit 1
fi

echo "PASS: $APP_ID remains alive, PID $PID"
