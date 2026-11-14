#!/usr/bin/env bash
# Build the saved project; --install also installs and launches on an authorized Quest.
set -euo pipefail
project=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
unity=${UNITY_EDITOR:-/home/bassbess/Unity/Hub/Editor/6000.6.2f1/Editor/Unity}
android="$(dirname -- "$unity")/Data/PlaybackEngines/AndroidPlayer"
apk="$project/Builds/LaserTag-Quest.apk"
install=false
build=true
for arg in "$@"; do
    case "$arg" in
        --install) install=true ;;
        --install-only) install=true; build=false ;;
        --help) echo 'Usage: tools/build-quest.sh [--install | --install-only]'; exit 0 ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done
mkdir -p "$project/Builds/logs"
stamp=$(date +%Y%m%d-%H%M%S)-$$
log="$project/Builds/logs/quest-$stamp.log"
launcher="$project/Builds/logs/launcher-$stamp.log"
fail() { echo "ERROR: $*" >&2; exit 1; }
# Reuse the installed NixOS Unity Hub FHS environment without starting the Hub.
fhs=${UNITY_FHS:-}
if [[ -z "$fhs" && -f /etc/NIXOS ]]; then
    hub=$(command -v unityhub || true)
    if [[ -n "$hub" ]]; then
        hub=$(readlink -f "$hub")
        fhs=$(sed -n 's/.*"\([^" ]*\/bin\/unityhub-fhs-env\)".*/\1/p' "$hub" | head -1)
        # The user's wrapper may forward to the system Hub wrapper.
        if [[ -z "$fhs" && -f /run/current-system/sw/bin/unityhub ]]; then
            fhs=$(sed -n 's/.*"\([^" ]*\/bin\/unityhub-fhs-env\)".*/\1/p' /run/current-system/sw/bin/unityhub | head -1)
        fi
    fi
    [[ -n "$fhs" ]] || fail 'Cannot find Unity FHS wrapper; set UNITY_FHS to its executable path.'
fi
run_android() {
    if [[ -n "$fhs" ]]; then "$fhs" "$@"; else "$@"; fi
}
if $build; then
    [[ -x "$unity" ]] || fail "Unity not found: $unity"
    for tool in SDK/platform-tools/adb NDK/toolchains/llvm/prebuilt/linux-x86_64/bin/clang OpenJDK/bin/java; do
        [[ -x "$android/$tool" ]] || fail "Missing Android module tool: $android/$tool"
    done
    echo 'Save all scenes/assets and close the project in Unity before building.'
    echo "Unity log: $log"
    marker="$project/Builds/logs/build-start-$stamp"
    touch "$marker"
    if ! run_android "$unity" -batchmode -nographics -quit -projectPath "$project" \
        -buildTarget Android -executeMethod SubmissionBuild.BuildQuestBatch \
        -quest-output "$apk" -logFile "$log" >"$launcher" 2>&1; then
        cat "$launcher" >&2
        [[ ! -f "$log" ]] || tail -80 "$log" >&2
        fail "Unity failed. Full logs: $log and $launcher. If project is open, save and close it. For licensing or Package Manager IPC errors, run from your desktop terminal with Unity Hub licensed and local sockets available."
    fi
    [[ -s "$apk" && "$apk" -nt "$marker" ]] || fail 'Unity did not produce a fresh APK.'
    grep -Eq 'Quest APK built|Build.*[Ss]ucceeded|Build Result.*Succeeded' "$log" || fail "APK exists but Unity success was not confirmed; see $log"
    echo "Built: $apk ($(stat -c %s "$apk") bytes)"
fi
$install || exit 0
[[ -s "$apk" ]] || fail "No APK at $apk; run without --install-only first."
adb_cmd=${ADB:-$(command -v adb || true)}
# Use Unity's platform tools in the same FHS environment if no system adb exists.
[[ -n "$adb_cmd" ]] || adb_cmd="$android/SDK/platform-tools/adb"
adb_run() { run_android "$adb_cmd" "$@"; }
if ! devices=$(adb_run devices -l 2>&1); then
    echo "$devices" >&2
    fail 'ADB unavailable. Run from a desktop terminal with USB access; no installation attempted.'
fi
printf '%s\n' "$devices"
quests=()
while read -r serial state rest; do
    [[ "$state" == device ]] || continue
    manufacturer=$(adb_run -s "$serial" shell getprop ro.product.manufacturer | tr -d '\r')
    model=$(adb_run -s "$serial" shell getprop ro.product.model | tr -d '\r')
    if [[ "$manufacturer $model" =~ [Oo]culus|[Mm]eta|[Qq]uest ]]; then quests+=("$serial"); fi
done <<< "$devices"
if [[ ${#quests[@]} == 0 ]]; then
    echo 'APK ready. No authorized Quest detected. Enable Developer Mode and accept Allow USB debugging inside the headset.'
    exit 0
fi
serial=${ANDROID_SERIAL:-}
if [[ -z "$serial" ]]; then
    [[ ${#quests[@]} == 1 ]] || fail 'Multiple Quests detected. Set ANDROID_SERIAL to select one.'
    serial=${quests[0]}
fi
found=false
for q in "${quests[@]}"; do [[ "$q" != "$serial" ]] || found=true; done
$found || fail 'ANDROID_SERIAL does not identify an authorized Quest.'
aapt=$(find "$android/SDK/build-tools" -mindepth 2 -maxdepth 2 -name aapt -type f | sort -V | tail -1)
[[ -n "$aapt" ]] || fail 'SDK aapt not found; cannot inspect APK package.'
badging=$(run_android "$aapt" dump badging "$apk")
package=$(sed -n "s/^package: name='\([^']*\)'.*/\1/p" <<< "$badging")
activity=$(sed -n "s/^launchable-activity: name='\([^']*\)'.*/\1/p" <<< "$badging")
[[ -n "$package" && -n "$activity" ]] || fail 'APK package or launch activity could not be identified.'
echo "Installing $package on $serial"
adb_run -s "$serial" install -r "$apk"
adb_run -s "$serial" shell pm path "$package"
# Timestamp limits logs to this launch without clearing existing headset logs.
since=$(adb_run -s "$serial" shell date '+%m-%d %H:%M:%S.000' | tr -d '\r')
launch=$(adb_run -s "$serial" shell am start -W -n "$package/$activity")
printf '%s\n' "$launch"
grep -q 'Status: ok' <<< "$launch" || fail 'Android did not confirm launch.'
sleep 10
startup="$project/Builds/logs/startup-$stamp.log"
adb_run -s "$serial" logcat -d -T "$since" -v threadtime > "$startup"
pid=$(adb_run -s "$serial" shell pidof "$package" | tr -d '\r') || fail "App exited after launch; inspect $startup"
echo "App running, PID $pid. Startup log: $startup"
if grep -Ei 'FATAL EXCEPTION|Fatal signal|AndroidRuntime.*Exception|Unity.*(Exception|Error)|OpenXR.*(fail|error)|XR_ERROR_' "$startup"; then
    echo 'Potential startup errors found above; inspect their process/package in the full log.' >&2
    exit 1
fi
echo 'Install and launch succeeded; no matching startup errors found. Verify VR rendering in the headset.'
