# Windows install

## What you need

Windows 10 or 11, 64 bit. The patcher is a single self contained
`YsPerfPatcher.exe`, so no .NET, no Visual C++ runtime, and no administrator
rights are required. It writes one file inside your game folder.

The game keeps running the way you start it today. The patch changes the DLL on
disk and nothing else.

## Install

1. Download `YandereSimPerfPatch-win-x64.zip` from the releases page.
2. Extract it into one folder, for example `Downloads\YandereSimPerfPatch`.
   Keep `inject.ps1`, `inject.bat`, and `YsPerfPatcher.exe` together.
3. Close Yandere Simulator. Windows keeps `Assembly-CSharp.dll` locked while
   the game runs, and the injector refuses to touch it.
4. Double click `inject.bat`. It launches `inject.ps1` with the right policy
   flags, nothing else to configure.
5. The console shows the game folder it found and the patch summary, for
   example:

```
Found game DLL: C:\Users\you\Downloads\YandereSimulator\YandereSimulator_Data\Managed\Assembly-CSharp.dll
Summary: 13/13 patch groups applied.
Patched: C:\...\Assembly-CSharp.dll
```

6. Start the game. The first launch applies the safe defaults from the patch,
   so bloom, ambient occlusion, and high shadows start off.

If you prefer the shell:

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1
```

Windows may show a SmartScreen window the first time, because the binary is
new and unsigned. Choose `More info` and `Run anyway` if you built or downloaded
it from this repository. Your antivirus may also flag a packer that rewrites a
game DLL, that is the nature of a patcher, the source is in this repository.

## How the injector finds the game

The search runs in this order and stops at the first hit:

1. The folder you pass with `-GamePath`, either the game folder or the DLL
   path itself.
2. The environment variable `YANDERE_SIM_PATH`.
3. The environment variable `YANDERE_LAUNCHER_INSTALL_DIR`, which the official
   launcher exports when it runs.
4. The folder next to `inject.ps1`, both directly and under a
   `YandereSimulator` subfolder.
5. Under your user profile: `Downloads\YandereSimulator`,
   `Downloads\YandereSimulator\YandereSimulator`, `Desktop\YandereSimulator`,
   `Documents\YandereSimulator`, `Games\YandereSimulator`, `YandereSimulator`.
6. As a last resort a shallow sweep with `YandereSimulator.exe` three levels
   deep through `Downloads`, `Desktop`, and `Documents`.

A hit must contain `YandereSimulator_Data\Managed\Assembly-CSharp.dll`.
When two or more installs qualify, the injector lists them and asks for an
explicit path instead of guessing.

Manual run with a folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1 -GamePath "D:\Games\YandereSimulator"
```

Manual run with the DLL:

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1 -GamePath "D:\Games\YandereSimulator\YandereSimulator_Data\Managed\Assembly-CSharp.dll"
```

Set a permanent override so future runs skip the search:

```powershell
setx YANDERE_SIM_PATH "D:\Games\YandereSimulator"
```

## All injector options

| Option | Effect |
|--------|--------|
| `inject.bat` | Search, then patch, double click entry point |
| `.\inject.ps1` | Search, then patch |
| `.\inject.ps1 -GamePath <folder>` | Patch this install |
| `.\inject.ps1 -GamePath <dll>` | Patch this DLL |
| `.\inject.ps1 -Update` | Download the newest release from GitHub, then patch |
| `.\inject.ps1 -Restore` | Search, then restore the original |
| `.\inject.ps1 -Restore -GamePath <folder>` | Restore in this install |

## Updates

Every normal run checks the newest release on GitHub before patching. The
check takes a moment and stays silent when you are offline. When a newer
version exists, the same line appears in the console and as a Windows popup
on the desktop.

To update, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1 -Update
```

The injector downloads the newest zip, replaces itself, the patcher, and the
docs next to itself, and patches your game right after.
`Assembly-CSharp.dll.orig` is not touched by updates.

## Restore the original DLL

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1 -Restore
```

The backup `Assembly-CSharp.dll.orig` lives next to the DLL and appears on the
first patch run. Restore copies it back. A run without a backup stops with an
error and changes nothing.

## After a game update

Every release ships a new `Assembly-CSharp.dll`, and the old backup may no
longer match. Run the injector again against the updated install. The patcher
understands the current official build (Unity 6000.2.8f1). When the DLL comes
from an older or modified build, the patcher prints which patterns it could not
find and exits with code 3 without writing the file.

## Troubleshooting

**`Yandere Simulator is running`** The game holds the DLL open. Close it fully,
including tray icons, then run the injector again.

**`Could not find the game`** Pass the folder that contains
`YandereSimulator.exe` with `-GamePath`, or set `YANDERE_SIM_PATH` with `setx`.

**`Found more than one install`** Several folders contain a valid DLL. Pick one
from the printed list and pass it with `-GamePath`.

**`Patcher not found next to this script`** The zip was extracted into
subfolders. `inject.ps1` and `YsPerfPatcher.exe` have to sit in the same
folder.

**`Access to the path ... is denied`** The game sits in a protected location
such as `Program Files`. Run PowerShell as administrator and repeat the command,
or move the game to a folder you own such as `Downloads`.

**`error: the patcher stopped before touching the file` (exit 3)** The DLL is
not from the current official build. The patcher prints the group it could not
find. Nothing was written, the file keeps its previous state.

**Exit codes of `YsPerfPatcher.exe`**

| Code | Meaning |
|------|---------|
| 0 | Patched, the summary shows how many groups matched |
| 2 | Already patched, file untouched |
| 3 | A required pattern was missing, file untouched |
| 1 | Bad arguments or the DLL could not be read |

## Build from source

```powershell
cd src\YsPerfPatcher
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\win
```

Mono.Cecil comes in through the project file, no extra steps are required.
