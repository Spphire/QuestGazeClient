# QuestGazeClient

Unity Quest client for gaze/camera/controller recording and PC telemetry used by
the Quest calibration/data-collection workflow.

## Environment

- Unity `6000.0.60f1`
- Target device: Quest 3
- Android package: `com.Apricity.EyeTrackingTest`
- Main APK output: `Build/EyeTrackingBuild/EyeTrackingTest.apk`

## Repository Layout

- `Assets/EyeTracking/` contains the recording, telemetry, UI, and build tooling
  used by the Quest data collector.
- `ProjectSettings/` and `Packages/` pin the Unity project configuration.
- `Build/`, `Library/`, `Logs/`, `Temp/`, `Obj/`, and `UserSettings/` are local
  generated directories and are not committed.
- Large Unity binary assets are stored through Git LFS.

## Build

Open the project in Unity `6000.0.60f1`, then use:

```text
EyeTracking/Build/Build Android APK
```

The expected APK output is:

```text
Build/EyeTrackingBuild/EyeTrackingTest.apk
```

## Release Assets

APK files and full source ZIP snapshots are managed under `ReleaseAssets/` with
Git LFS so the project can be restored even when the Unity/GitHub Release
environment is awkward. After building the APK:

```powershell
tools/package_release.ps1
```

See `RELEASE.md` for the release workflow.

## Credits

This project was derived from an experimental mixed-reality laser tag project.
Original credits:

- UI sounds from [Fourier](https://opengameart.org/users/fourier) on opengameart.org
- "Level up sound effects" by Bart Kelsey. Commissioned by Will Corwin for OpenGameArt.org (http://opengameart.org)
