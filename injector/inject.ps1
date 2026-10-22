# Yandere Simulator Performance Patch
# Finds your game install and patches Assembly-CSharp.dll with YsPerfPatcher.
#
#   powershell -ExecutionPolicy Bypass -File .\inject.ps1
#   powershell -ExecutionPolicy Bypass -File .\inject.ps1 -GamePath "D:\YandereSimulator"
#   powershell -ExecutionPolicy Bypass -File .\inject.ps1 -Update
#   powershell -ExecutionPolicy Bypass -File .\inject.ps1 -Restore

param(
    [string]$GamePath,
    [switch]$Restore,
    [switch]$Update
)

$ErrorActionPreference = "Stop"

$RepoApi = "https://api.github.com/repos/hakuxyz/YandereSimPerfPatch/releases/latest"
$RepoDownload = "https://github.com/hakuxyz/YandereSimPerfPatch/releases/latest/download"
$VersionFile = Join-Path $PSScriptRoot "VERSION"

function Write-Note([string]$Text) { Write-Host $Text }
function Write-Problem([string]$Text) { Write-Host $Text -ForegroundColor Red }
function Write-Good([string]$Text) { Write-Host $Text -ForegroundColor Green }

$Patcher = Join-Path $PSScriptRoot "YsPerfPatcher.exe"

function Get-LocalVersion {
    try {
        if (Test-Path -LiteralPath $VersionFile) {
            return ((Get-Content -LiteralPath $VersionFile -Raw) -replace "[vV\s]", "")
        }
    } catch { }
    return ""
}

function Get-LatestVersion {
    try {
        $release = Invoke-RestMethod -Uri $RepoApi -TimeoutSec 8
        if ($release.tag_name) { return ($release.tag_name -replace "^[vV]", "") }
    } catch { }
    return ""
}

function Show-DesktopMessage([string]$Text) {
    Write-Note $Text
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shell.Popup($Text, 10, "YandereSimPerfPatch", 0x40) | Out-Null
    } catch { }
}

# Silent during normal runs, skips everything when GitHub cannot be reached.
function Test-Update {
    $current = Get-LocalVersion
    if (-not $current) { return }
    $latest = Get-LatestVersion
    if (-not $latest) { return }
    if ($current -eq $latest) { return }
    # Stay quiet when the local copy is ahead of the latest release.
    try {
        if ([version]$current -ge [version]$latest) { return }
    } catch { return }
    Show-DesktopMessage "Your patch is not up to date (installed: $current, latest: $latest). Run .\inject.ps1 -Update to update."
}

function Invoke-SelfUpdate {
    $latest = Get-LatestVersion
    if (-not $latest) {
        Write-Problem "Cannot reach GitHub, check your internet connection."
        exit 1
    }

    $asset = "YandereSimPerfPatch-win-x64.zip"
    $url = "$RepoDownload/$asset"
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("yspp_update_" + [System.IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Path $tmp | Out-Null

    try {
        Write-Note "Downloading $asset ($latest)..."
        $zip = Join-Path $tmp $asset
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 300

        $extract = Join-Path $tmp "extracted"
        Expand-Archive -Path $zip -DestinationPath $extract -Force

        $src = Join-Path $extract "YandereSimPerfPatch"
        if (-not (Test-Path -LiteralPath $src)) {
            Write-Problem "Unexpected archive layout."
            exit 1
        }

        foreach ($f in @("inject.sh", "inject.ps1", "inject.bat", "YsPerfPatcher.exe", "VERSION", "README.md", "LICENSE")) {
            $from = Join-Path $src $f
            if (Test-Path -LiteralPath $from) {
                Copy-Item -LiteralPath $from -Destination (Join-Path $PSScriptRoot $f) -Force
            }
        }
        $docs = Join-Path $src "docs"
        if (Test-Path -LiteralPath $docs) {
            $docsTarget = Join-Path $PSScriptRoot "docs"
            New-Item -ItemType Directory -Path $docsTarget -Force | Out-Null
            Copy-Item -Path (Join-Path $docs "*") -Destination $docsTarget -Recurse -Force
        }

        Write-Good "Updated to $latest."
    } finally {
        try { Remove-Item -LiteralPath $tmp -Recurse -Force } catch { }
    }
}

# A valid install contains YandereSimulator.exe and the Managed folder.
function Resolve-Dll([string]$InputPath) {
    if (-not $InputPath) { return $null }
    if (Test-Path -LiteralPath $InputPath -PathType Container) {
        $candidate = Join-Path $InputPath "YandereSimulator_Data\Managed\Assembly-CSharp.dll"
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path $candidate).Path }
        return $null
    }
    if (Test-Path -LiteralPath $InputPath -PathType Leaf) {
        $full = (Resolve-Path -LiteralPath $InputPath).Path
        if ($full -like "*\YandereSimulator_Data\Managed\Assembly-CSharp.dll") { return $full }
    }
    return $null
}

# Candidate game folders, most likely first.
function Get-Candidates {
    $roots = New-Object System.Collections.Generic.List[string]

    foreach ($value in @($env:YANDERE_SIM_PATH, $env:YANDERE_LAUNCHER_INSTALL_DIR)) {
        if ($value) { $roots.Add($value) }
    }

    $roots.Add($PSScriptRoot)
    $roots.Add((Join-Path $PSScriptRoot "YandereSimulator"))

    $home_ = $env:USERPROFILE
    foreach ($rel in @(
        "Downloads\YandereSimulator",
        "Downloads\YandereSimulator\YandereSimulator",
        "Desktop\YandereSimulator",
        "Documents\YandereSimulator",
        "Games\YandereSimulator",
        "YandereSimulator"
    )) {
        $roots.Add((Join-Path $home_ $rel))
    }

    $results = New-Object System.Collections.Generic.List[string]
    foreach ($root in $roots) {
        $dll = Resolve-Dll $root
        if ($dll) { $results.Add($dll) }
    }

    # Shallow sweep over the usual folders as a last resort.
    if ($results.Count -eq 0) {
        $searchRoots = @(
            (Join-Path $home_ "Downloads"),
            (Join-Path $home_ "Desktop"),
            (Join-Path $home_ "Documents")
        )
        foreach ($root in $searchRoots) {
            if (-not (Test-Path $root)) { continue }
            try {
                $exe = Get-ChildItem -Path $root -Filter "YandereSimulator.exe" -Recurse -Depth 3 -File -ErrorAction SilentlyContinue |
                    Select-Object -First 1
            } catch {
                $exe = $null
            }
            if ($exe) {
                $dll = Resolve-Dll $exe.DirectoryName
                if ($dll) { $results.Add($dll); break }
            }
        }
    }

    return $results
}

function Restore-Dll([string]$Dll) {
    $orig = "$Dll.orig"
    if (-not (Test-Path -LiteralPath $orig)) {
        Write-Problem "No backup next to the DLL, nothing to restore:"
        Write-Problem "  $orig"
        exit 1
    }
    Copy-Item -LiteralPath $orig -Destination $Dll -Force
    Write-Good "Restored the original Assembly-CSharp.dll."
    exit 0
}

function Invoke-Patcher([string]$Dll) {
    $proc = Start-Process -FilePath $Patcher -ArgumentList "`"$Dll`"" -NoNewWindow -Wait -PassThru
    switch ($proc.ExitCode) {
        0 { Write-Good "Patched: $Dll" }
        2 { Write-Good "Already patched, nothing to do: $Dll" }
        3 {
            Write-Problem "The patcher stopped before touching the file, see the message above."
            exit 1
        }
        default {
            Write-Problem "Patcher failed with exit code $($proc.ExitCode)."
            exit 1
        }
    }
    exit 0
}

# ---------------------------------------------------------------------------

if ($Update) {
    Invoke-SelfUpdate
}

if (-not (Test-Path -LiteralPath $Patcher)) {
    Write-Problem "Patcher not found next to this script:"
    Write-Problem "  $Patcher"
    Write-Note "Keep inject.ps1 and YsPerfPatcher.exe together in the same folder."
    exit 1
}

if (-not $Restore) {
    Test-Update
}

$dll = Resolve-Dll $GamePath
if ($GamePath -and -not $dll) {
    Write-Problem "No Assembly-CSharp.dll under: $GamePath"
    exit 1
}

if ($Restore) {
    if (-not $dll) {
        $found = @(Get-Candidates)
        if ($found.Count -eq 0) {
            Write-Problem "Game not found, pass the folder:"
            Write-Note "  .\inject.ps1 -Restore -GamePath `"D:\YandereSimulator`""
            exit 1
        }
        $dll = $found[0]
    }
    Restore-Dll $dll
}

if (-not $dll) {
    $found = @(Get-Candidates)
    $unique = @($found | Where-Object { $_ } | Select-Object -Unique)

    if ($unique.Count -eq 0) {
        Write-Problem "Could not find the game. Looked in these folders:"
        Write-Note "  the folder next to this script, Downloads, Desktop,"
        Write-Note "  Documents, and YandereSimulator subfolders in each."
        Write-Note ""
        Write-Note "Run it with the game folder instead:"
        Write-Note "  .\inject.ps1 -GamePath `"D:\YandereSimulator`""
        Write-Note ""
        Write-Note "or set YANDERE_SIM_PATH to the folder that contains YandereSimulator.exe."
        exit 1
    }

    if ($unique.Count -gt 1) {
        Write-Problem "Found more than one install:"
        for ($i = 0; $i -lt $unique.Count; $i++) {
            Write-Note "  $($i + 1)) $($unique[$i])"
        }
        Write-Note "Pick one by passing its folder:"
        Write-Note "  .\inject.ps1 -GamePath `"path from the list`""
        exit 1
    }

    $dll = $unique[0]
    Write-Note "Found game DLL: $dll"
}

$running = Get-Process -Name "YandereSimulator" -ErrorAction SilentlyContinue
if ($running) {
    Write-Problem "Yandere Simulator is running. Close the game first,"
    Write-Problem "otherwise Windows keeps Assembly-CSharp.dll locked."
    exit 1
}

Invoke-Patcher $dll
