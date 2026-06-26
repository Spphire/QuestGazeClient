param(
    [string]$Version = "",
    [string]$ApkPath = "Build/EyeTrackingBuild/EyeTrackingTest.apk",
    [string]$OutputDir = "dist",
    [string]$ReleaseAssetsDir = "ReleaseAssets",
    [switch]$NoReleaseMirror,
    [switch]$SkipSourceZip
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptDir "..")
Set-Location $projectRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $commit = ""
    try {
        $commit = (git rev-parse --short HEAD 2>$null).Trim()
    } catch {
        $commit = ""
    }
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $Version = if ($commit) { "$stamp-$commit" } else { $stamp }
}

$outDir = Join-Path $projectRoot $OutputDir
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$safeVersion = $Version -replace '[^A-Za-z0-9._-]', '_'
$sourceZip = Join-Path $outDir "QuestGazeClient-source-$safeVersion.zip"
$apkOut = Join-Path $outDir "QuestGazeClient-$safeVersion.apk"
$manifestPath = Join-Path $outDir "QuestGazeClient-$safeVersion.release.json"
$shaPath = Join-Path $outDir "QuestGazeClient-$safeVersion.sha256.txt"

$excludedRootNames = @(
    ".git",
    ".vs",
    ".idea",
    ".vscode",
    ".zed",
    ".utmp",
    ".plastic",
    "_codex_checkpoints",
    "Library",
    "Temp",
    "Obj",
    "obj",
    "Build",
    "Builds",
    "Logs",
    "UserSettings",
    "Recordings",
    "MemoryCaptures",
    "dist",
    "ReleaseAssets"
)

$excludedFileExtensions = @(
    ".csproj",
    ".sln",
    ".suo",
    ".tmp",
    ".user",
    ".userprefs",
    ".pidb",
    ".booproj",
    ".svd",
    ".pdb",
    ".mdb",
    ".opendb"
)

if (-not $SkipSourceZip) {
    $sourceItems = Get-ChildItem -Force $projectRoot | Where-Object {
        $excludedRootNames -notcontains $_.Name -and
        (-not $_.PSIsContainer) -and
        ($excludedFileExtensions -notcontains $_.Extension)
    }
    $sourceDirs = Get-ChildItem -Force $projectRoot | Where-Object {
        $_.PSIsContainer -and ($excludedRootNames -notcontains $_.Name)
    }
    $paths = @($sourceItems.FullName + $sourceDirs.FullName) | Where-Object { $_ }
    if (Test-Path $sourceZip) {
        Remove-Item -LiteralPath $sourceZip -Force
    }
    Compress-Archive -Path $paths -DestinationPath $sourceZip -CompressionLevel Optimal -Force
}

$resolvedApk = Resolve-Path $ApkPath
Copy-Item -LiteralPath $resolvedApk -Destination $apkOut -Force

$hashRows = @()
if (-not $SkipSourceZip) {
    $hashRows += Get-FileHash -Algorithm SHA256 -LiteralPath $sourceZip
}
$hashRows += Get-FileHash -Algorithm SHA256 -LiteralPath $apkOut
$hashRows | ForEach-Object { "$($_.Hash)  $(Split-Path -Leaf $_.Path)" } | Set-Content -Encoding ASCII $shaPath

$manifest = [ordered]@{
    name = "QuestGazeClient"
    version = $Version
    unityVersion = (Get-Content "ProjectSettings/ProjectVersion.txt" | Select-String "m_EditorVersion:" | ForEach-Object { $_.ToString().Split(":", 2)[1].Trim() })
    sourceZip = if (-not $SkipSourceZip) { (Split-Path -Leaf $sourceZip) } else { $null }
    apk = (Split-Path -Leaf $apkOut)
    sha256 = (Split-Path -Leaf $shaPath)
    apkSource = $resolvedApk.Path
    createdUtc = (Get-Date).ToUniversalTime().ToString("o")
}

try {
    $manifest.gitCommit = (git rev-parse HEAD 2>$null).Trim()
    $manifest.gitBranch = (git branch --show-current 2>$null).Trim()
} catch {
    $manifest.gitCommit = $null
    $manifest.gitBranch = $null
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding ASCII $manifestPath

$releaseDir = $null
if (-not $NoReleaseMirror) {
    $releaseDir = Join-Path (Join-Path $projectRoot $ReleaseAssetsDir) $safeVersion
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    if (-not $SkipSourceZip) {
        Copy-Item -LiteralPath $sourceZip -Destination $releaseDir -Force
    }
    Copy-Item -LiteralPath $apkOut -Destination $releaseDir -Force
    Copy-Item -LiteralPath $shaPath -Destination $releaseDir -Force
    Copy-Item -LiteralPath $manifestPath -Destination $releaseDir -Force
}

Write-Host "Release artifacts:"
if (-not $SkipSourceZip) { Write-Host "  source:   $sourceZip" }
Write-Host "  apk:      $apkOut"
Write-Host "  sha256:   $shaPath"
Write-Host "  manifest: $manifestPath"
if ($releaseDir) { Write-Host "  mirror:   $releaseDir" }
