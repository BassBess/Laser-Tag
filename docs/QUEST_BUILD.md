# Quest APK build and deployment

Save all scenes/assets and close this project in the Unity Editor. From a normal desktop terminal:

```bash
/home/bassbess/Downloads/Laser-Tag-FullGame/tools/build-quest.sh --install
```

No flags builds only. `--install` builds, then installs and launches if exactly one authorized Quest is present. `--install-only` installs and launches an existing APK without rebuilding. With multiple Quests, set `ANDROID_SERIAL`. `UNITY_EDITOR`, `UNITY_FHS`, and `ADB` accept executable path overrides.

Expected output: `/home/bassbess/Downloads/Laser-Tag-FullGame/Builds/LaserTag-Quest.apk`.
Timestamped Unity, launcher and device startup logs: `Builds/logs/`.
The script requires Unity success plus a freshly produced, nonempty APK; it never installs a stale APK after a failed build. Installation uses `adb install -r`, keeping existing app data. Startup checks inspect logs and confirm the process survives ten seconds. Headset visual/interaction testing remains necessary.

The script uses the existing `SubmissionBuild.BuildQuestBatch`, with MainMenu first and RelayVRGame second. It discovers the existing NixOS Unity Hub FHS wrapper without opening the Hub. It does not reset settings, delete caches or force asset reimports. Save Editor changes first: batchmode can only build files saved on disk. Normal Unity imports/build caches may still update during a successful build.

## Inspection on 2026-11-14

- Branch: `feature/arena-boundary-stair-teleport`. Existing dirty gameplay, scenes, settings, untracked assets and three stashes were left in place. No Git mutation was performed.
- Unity `6000.6.2f1`; Android IL2CPP; ARM64 only; explicit Vulkan; minimum API 32 and target API 34.
- MainMenu is the first enabled Editor build scene. Existing submission pipeline explicitly selects MainMenu and RelayVRGame, excluding the additional test/duplicate scenes listed in EditorBuildSettings.
- Android XR initialization on startup enabled; OpenXR loader assigned. Meta Quest support and Quest 2/3 target devices enabled. Oculus Touch and Quest Touch Plus controller profiles enabled. Android XR-specific features disabled. The serialized manager automatic-loading flags are intentionally false: XRGeneralSettings initializes the loader itself (verified against the installed XR Management source).
- Bundled Android tools exist: SDK platform 34 and build-tools 36.0.0, platform-tools 36.0.0, NDK r27c `27.2.12479018`, OpenJDK 17.0.18. NDK/JDK versions match Unity's requirements. Tool execution through the full build is unverified.
- Plain Editor launch failed with missing `libxml2.so.2`; the installed Unity Hub FHS environment resolves that startup dependency.
- Initial FHS build attempt refused to open an already-open project. After user saved/closed the Editor, Unity progressed past that check but failed licensing IPC and Package Manager IPC (`Upm-25`, 30-second connection timeout). Local socket operations reported `Operation not permitted` inside the agent sandbox. No APK was produced and no device installation/launch was attempted.
- Full retry log: `Builds/logs/quest-20261114-185901-2.log`. The restriction does not establish that the user's Unity license is invalid. Run from the desktop terminal; if licensing errors persist there, sign in/activate normally through Unity Hub. Do not bypass licensing or delete project lock/cache files.

## USB diagnosis and recovery

Host sysfs exposes an Oculus Quest 3 at `3-2`, USB vendor/product `2833:5013`, including interface class/subclass/protocol `ff/42/01` (ADB). This proves USB data enumeration at inspection time. It does not prove ADB authorization. A cable replacement is not the first action for this observed connection.

Inside the agent sandbox `/dev/bus/usb` is absent, `lsusb` fails to initialize libusb, and ADB 35.0.2 cannot bind its server at port 5037 (`Operation not permitted`). Therefore the previous empty list cannot be diagnosed conclusively here; it must be checked from a normal desktop terminal:

```bash
nix-shell -p android-tools usbutils --run 'lsusb; adb version; adb start-server; adb devices -l'
```

Use one ADB installation consistently. If an old server causes a version mismatch, close other Android tooling and restart that server once with `adb kill-server` then `adb start-server` from the same terminal (this disconnects other ADB sessions).

If the Quest appears as `unauthorized`, put on the awake headset, reconnect USB, and accept **Allow USB debugging**, selecting **Always allow from this computer**. If no prompt/device appears, verify Developer Mode in the paired Meta Horizon phone app's headset settings, restart/reconnect the headset, and inspect its developer USB debugging setting if available. Meta instructions: https://developers.meta.com/vr/documentation/native/android/mobile-device-setup/ . If USB enumeration disappears, try a known data cable and direct USB port.

If desktop ADB specifically reports permissions problems, inspect device permissions and active desktop-session access first. No system-wide changes were made. On this NixOS release, prefer an explicit narrow udev rule if automatic access is insufficient. The exact optional configuration is:

```nix
# In the existing NixOS configuration (merge with existing attributes):
environment.systemPackages = [ pkgs.android-tools pkgs.usbutils ];
services.udev.extraRules = ''
  SUBSYSTEM=="usb", ATTR{idVendor}=="2833", MODE="0660", GROUP="users", TAG+="uaccess"
'';
```

This grants the existing `users` group access to Meta/Oculus USB devices and tags them for active-session access. Review this before applying `sudo nixos-rebuild switch`, then reconnect USB. It does not fix an agent sandbox hiding USB or blocking sockets. Do not blindly use old `programs.adb.enable` instructions: newer NixOS versions may retire that option.

## Install later without rebuilding

```bash
/home/bassbess/Downloads/Laser-Tag-FullGame/tools/build-quest.sh --install-only
```

Expected project application identifier: `edu.aub.cmps297.lasertag`. The script reads the actual package and launchable activity from the generated APK using SDK `aapt`, rather than assuming this identifier or a Unity activity name.

For direct ADB installation once an APK exists and a single authorized headset is connected:

```bash
adb install -r /home/bassbess/Downloads/Laser-Tag-FullGame/Builds/LaserTag-Quest.apk && adb shell monkey -p edu.aub.cmps297.lasertag -c android.intent.category.LAUNCHER 1
```
