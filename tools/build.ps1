<#
.SYNOPSIS
    Build / test / publish helper for the JAV_subs_Nakatashi fork of Subtitle Edit.

.DESCRIPTION
    Wraps the exact commands used by .github/workflows/build-ui.yml so a local
    rebuild produces the same artifacts CI does. Run from anywhere; paths are
    resolved relative to the repository root.

.EXAMPLE
    ./tools/build.ps1 restore
    ./tools/build.ps1 build   -Configuration Debug
    ./tools/build.ps1 test
    ./tools/build.ps1 publish -Runtime win-x64 -SelfContained
    ./tools/build.ps1 run
    ./tools/build.ps1 clean
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('restore', 'build', 'test', 'publish', 'mpv', 'run', 'clean', 'all')]
    [string]$Task = 'build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = 'win-x64',

    # Bundle the .NET runtime into the published output (what the release
    # artifacts do). Without it, publish is framework-dependent and needs the
    # .NET 10 desktop runtime present on the target machine.
    [switch]$SelfContained,

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot 'SubtitleEdit.sln'
$UiProject = Join-Path $RepoRoot 'src/ui/UI.csproj'
$SeConfig = Join-Path $RepoRoot 'src/ui/Logic/Config/Se.cs'
$ThirdParty = Join-Path $RepoRoot 'third_party'

# Pinned alongside .github/workflows/build-ui.yml. Bump both together.
$MpvUrl = 'https://github.com/SubtitleEdit/support-files/releases/download/libmpv-2026-04-21/libmpv2-win64.zip'

function Write-Step($Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked {
    param([string]$Exe, [string[]]$Arguments)
    Write-Host "    $Exe $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Exe exited with code $LASTEXITCODE"
    }
}

function Assert-Toolchain {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw 'dotnet SDK not found on PATH. Install .NET SDK 10.0.x from https://dotnet.microsoft.com/download'
    }
    $sdks = & dotnet --list-sdks
    if (-not ($sdks | Select-String -SimpleMatch -Quiet '10.')) {
        throw "No .NET 10 SDK found. Installed SDKs:`n$($sdks -join "`n")"
    }
}

# Mirrors the version-extraction step in build-ui.yml so local publishes stamp
# the same version numbers as CI.
function Get-SeVersion {
    $content = Get-Content $SeConfig -Raw
    $m = [regex]::Match($content, 'public\s+static\s+string\s+Version\s*\{[^}]+\}\s*=\s*"v([^"]+)"')
    if (-not $m.Success) { throw "Could not find Version in $SeConfig" }

    $display = $m.Groups[1].Value
    $parts = $display -split '-', 2
    $nums = $parts[0].Split('.')
    $major = [int]$nums[0]
    $minor = if ($nums.Length -gt 1) { [int]$nums[1] } else { 0 }
    $build = if ($nums.Length -gt 2) { [int]$nums[2] } else { 0 }
    $revision = 0
    if ($parts.Length -gt 1) {
        $rm = [regex]::Match($parts[1], '\d+$')
        if ($rm.Success) { $revision = [int]$rm.Value }
    }
    [pscustomobject]@{
        Display = $display
        Full    = "$major.$minor.$build.$revision"
    }
}

function Task-Restore {
    Write-Step 'dotnet restore'
    Invoke-Checked dotnet @('restore', $Solution)
}

function Task-Build {
    Write-Step "dotnet build ($Configuration)"
    $dotnetArgs = @('build', $Solution, '--configuration', $Configuration)
    if ($NoRestore) { $dotnetArgs += '--no-restore' }
    Invoke-Checked dotnet $dotnetArgs
}

function Task-Test {
    Write-Step "dotnet test ($Configuration)"
    $dotnetArgs = @('test', $Solution, '--configuration', $Configuration)
    if ($NoRestore) { $dotnetArgs += '--no-restore' }
    Invoke-Checked dotnet $dotnetArgs
}

# Downloads and caches libmpv, which Subtitle Edit loads at runtime for video
# playback. Windows only: on Linux/macOS mpv comes from the system package
# manager (see README.md) or the bundled .app / flatpak.
function Task-Mpv {
    if (-not $Runtime.StartsWith('win-')) {
        Write-Host "    Skipping libmpv download: install mpv via your package manager for $Runtime." -ForegroundColor Yellow
        return $null
    }
    $dll = Join-Path $ThirdParty 'libmpv-2.dll'
    if (Test-Path $dll) {
        Write-Host "    libmpv already cached at $dll" -ForegroundColor DarkGray
        return $dll
    }

    Write-Step 'Downloading libmpv'
    New-Item -ItemType Directory -Force -Path $ThirdParty | Out-Null
    $zip = Join-Path $ThirdParty 'libmpv2-win64.zip'
    $extract = Join-Path $ThirdParty 'libmpv-temp'
    Invoke-WebRequest -Uri $MpvUrl -OutFile $zip
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $found = Get-ChildItem -Path $extract -Filter 'libmpv-2.dll' -Recurse -File | Select-Object -First 1
    if (-not $found) { throw "libmpv-2.dll not found inside $MpvUrl" }
    Move-Item -LiteralPath $found.FullName -Destination $dll -Force
    Remove-Item -LiteralPath $zip -Force
    Remove-Item -LiteralPath $extract -Recurse -Force
    Write-Host "    Cached $dll" -ForegroundColor Green
    return $dll
}

function Task-Publish {
    $version = Get-SeVersion
    $outDir = Join-Path $RepoRoot "publish/$Runtime"
    Write-Step "dotnet publish -> $outDir (v$($version.Display), self-contained=$($SelfContained.IsPresent))"

    $dotnetArgs = @(
        'publish', $UiProject,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', $SelfContained.IsPresent.ToString().ToLower(),
        '-p:PublishSingleFile=true',
        '-p:DebugSymbols=false',
        '-p:DebugType=none',
        "-p:Version=$($version.Full)",
        "-p:FileVersion=$($version.Full)",
        "-p:InformationalVersion=$($version.Display)",
        '-o', $outDir
    )
    Invoke-Checked dotnet $dotnetArgs

    # DebugType=none does not suppress native PDBs shipped by the SkiaSharp /
    # HarfBuzz runtime packages, so strip them the way CI does.
    Get-ChildItem -Path $outDir -Filter '*.pdb' -Recurse -File | Remove-Item -Force

    $mpv = Task-Mpv
    if ($mpv) { Copy-Item -LiteralPath $mpv -Destination $outDir -Force }

    Copy-Item -LiteralPath (Join-Path $RepoRoot 'LICENSE') -Destination $outDir -Force
    Write-Host "    Output: $outDir" -ForegroundColor Green

    # UI.csproj sets RestorePackagesWithLockFile, so a RID-specific publish rewrites
    # src/ui/packages.lock.json: it appends a "net10.0/<rid>" section and bakes the
    # -p:Version value into the libse ProjectReference range. That churn is a publish
    # artifact, not a dependency change — don't commit it.
    $lockFile = 'src/ui/packages.lock.json'
    if (& git -C $RepoRoot status --porcelain -- $lockFile) {
        Write-Host ''
        Write-Host "    NOTE: publish modified $lockFile (expected for a RID-specific publish)." -ForegroundColor Yellow
        Write-Host "          Discard it with:  git checkout -- $lockFile" -ForegroundColor Yellow
    }
}

function Task-Run {
    Write-Step "dotnet run ($Configuration)"
    Invoke-Checked dotnet @('run', '--project', $UiProject, '-c', $Configuration)
}

function Task-Clean {
    Write-Step 'Removing bin/ obj/ publish/'
    Get-ChildItem -Path (Join-Path $RepoRoot 'src'), (Join-Path $RepoRoot 'tests') `
        -Include 'bin', 'obj' -Directory -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host "    rm $($_.FullName)" -ForegroundColor DarkGray
            Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    $publish = Join-Path $RepoRoot 'publish'
    if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
}

Assert-Toolchain
Push-Location $RepoRoot
try {
    switch ($Task) {
        'restore' { Task-Restore }
        'build'   { Task-Build }
        'test'    { Task-Test }
        'publish' { Task-Publish }
        'mpv'     { Task-Mpv | Out-Null }
        'run'     { Task-Run }
        'clean'   { Task-Clean }
        'all'     { Task-Restore; $script:NoRestore = $true; Task-Build; Task-Test; Task-Publish }
    }
    Write-Host ''
    Write-Host "OK: $Task" -ForegroundColor Green
}
finally {
    Pop-Location
}
