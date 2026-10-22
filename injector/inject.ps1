# Yandere Simulator Performance Patch
# Finds your game install and patches Assembly-CSharp.dll with YsPerfPatcher.
#
#   powershell -ExecutionPolicy Bypass -File .\inject.ps1
#   powershell -ExecutionPolicy Bypass -File .\inject.ps1 -GamePath "D:\YandereSimulator"
#   powershell -ExecutionPolicy Bypass -File .\inject.ps1 -Restore

param(
    [string]$GamePath,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

function Write-Note([string]$Text) { Write-Host $Text }
function Write-Problem([string]$Text) { Write-Host $Text -ForegroundColor Red }
function Write-Good([string]$Text) { Write-Host $Text -ForegroundColor Green }

$Patcher = Join-Path $PSScriptRoot "YsPerfPatcher.exe"

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

if (-not (Test-Path -LiteralPath $Patcher)) {
    Write-Problem "Patcher not found next to this script:"
    Write-Problem "  $Patcher"
    Write-Note "Keep inject.ps1 and YsPerfPatcher.exe together in the same folder."
    exit 1
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
