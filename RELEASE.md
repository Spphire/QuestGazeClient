# QuestGazeClient Release Management

This Unity project is intentionally managed as source code plus external release
assets:

- Git stores the Unity source project.
- Git LFS stores large Unity binary assets such as FBX, TGA, HDR, EXR, PNG, and
  native tool binaries.
- APK files and full source ZIP snapshots are generated into `dist/` and should
  be uploaded to GitHub Releases. They are not committed to Git history.

## Build/Package Environment

- Unity: `6000.0.60f1`
- Android package name: `com.Apricity.EyeTrackingTest`
- Main APK output: `Build/EyeTrackingBuild/EyeTrackingTest.apk`
- Main build menu: `EyeTracking/Build/Build Android APK`

## First-Time Clone

```powershell
git clone git@github.com:Spphire/QuestGazeClient.git
cd QuestGazeClient
git lfs install
git lfs pull
```

Open the repository root in Unity `6000.0.60f1`. Unity will regenerate
`Library/`, `Temp/`, `Obj/`, `Logs/`, and `UserSettings/` locally.

## Create Release Assets

After building the APK in Unity:

```powershell
tools/package_release.ps1
```

This creates:

- `dist/QuestGazeClient-source-<version>.zip`
- `dist/QuestGazeClient-<version>.apk`
- `dist/QuestGazeClient-<version>.sha256.txt`
- `dist/QuestGazeClient-<version>.release.json`

Upload those files to a GitHub Release for the matching commit/tag. The source
ZIP is useful when the Unity/Git LFS environment is painful to reproduce, while
Git remains the canonical source history.
