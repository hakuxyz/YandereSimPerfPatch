# Linux install

## What you need

The patcher is a single x86_64 Linux binary. It needs no runtime, no root, and
no internet access. It only writes one file inside the game folder.

The game itself keeps running the way you start it today, native build or
Wine. The patch changes the DLL on disk and nothing else.

## Install

1. Download `YandereSimPerfPatch-linux-x64.zip` from the releases page.
2. Extract both files into one folder, for example:

```bash
unzip YandereSimPerfPatch-linux-x64.zip -d ~/YandereSimPerfPatch
cd ~/YandereSimPerfPatch
```

3. Mark them executable once:

```bash
chmod +x inject.sh YsPerfPatcher
```

4. Run the injector:

```bash
./inject.sh
```

The run ends with one of these lines:

```
Patched: /home/you/Games/YandereSimulator/YandereSimulator_Data/Managed/Assembly-CSharp.dll
Already patched, nothing to do: ...
```

5. Start the game as usual. First launch sets the safe defaults from the patch,
   so expect bloom, ambient occlusion, and high shadows to be off once.

## How the injector finds the game

The search runs in this order and stops at the first hit:

1. The folder you pass on the command line, either the game folder or the DLL
   path itself.
2. The environment variable `YANDERE_SIM_PATH`.
3. The environment variable `YANDERE_LAUNCHER_INSTALL_DIR`, which the official
   launcher exports when it runs.
4. The folder next to `inject.sh`, both directly and under a
   `YandereSimulator` subfolder. This covers the case where the injector lives
   inside the game folder or next to a launcher download.
5. A fixed list of common spots: `~/Games/YandereSimulator`, `~/YandereSimulator`,
   `~/.wine/drive_c/YandereSimulator`, `~/.wine/drive_c/Games/YandereSimulator`,
   `~/Downloads/YandereSimulator`, `~/Downloads/YandereSimulator/YandereSimulator`,
   `~/Desktop/YandereSimulator`, and the `Desktop/YandereSimulator` folder of
   every Wine user.

A hit must contain `YandereSimulator_Data/Managed/Assembly-CSharp.dll`,
otherwise the folder is skipped. When two or more installs qualify, the
injector lists them and asks for an explicit path instead of guessing.

Manual run with a folder:

```bash
./inject.sh "$HOME/Games/YandereSimulator"
```

Manual run with the DLL:

```bash
./inject.sh "$HOME/Games/YandereSimulator/YandereSimulator_Data/Managed/Assembly-CSharp.dll"
```

Permanent override in your shell profile:

```bash
export YANDERE_SIM_PATH="$HOME/Games/YandereSimulator"
```

## All injector options

| Command | Effect |
|---------|--------|
| `./inject.sh` | Search, then patch |
| `./inject.sh <folder>` | Patch this install |
| `./inject.sh <dll path>` | Patch this DLL |
| `./inject.sh --restore` | Search, then restore the original |
| `./inject.sh --restore <folder>` | Restore in this install |
| `./inject.sh --help` | Print the usage text |

## Restore the original DLL

```bash
./inject.sh --restore
```

The backup `Assembly-CSharp.dll.orig` lives next to the DLL and appears on the
first patch run. Restore copies it back. A run without a backup stops with an
error and changes nothing.

## After a game update

Every release ships a new `Assembly-CSharp.dll`, and the old backup may no
longer match. Run `./inject.sh` again against the updated install. The patcher
understands the current official build (Unity 6000.2.8f1). When the DLL comes
from an older or modified build, the patcher prints which patterns it could not
find and exits with code 3 without writing the file.

## Discord Rich Presence under Wine

Group 12 puts a start time into the Discord presence, and the game then talks
to Discord through the named pipe the same way it does on Windows. Under Wine
that pipe has no native counterpart, so a small bridge has to sit between the
game and the Discord desktop client.

The setup that works for the current build:

1. Install and run the Discord desktop client on Linux, it creates a socket at
   `$XDG_RUNTIME_DIR/discord-ipc-0`.
2. Run a Wine Discord IPC bridge that forwards the Wine named pipe to that
   socket. The package `wine-discord-ipc-bridge` does this, or any script that
   exports `WINEDLLOVERRIDES="winex11.drv=n,b"` together with the bridge binary
   before starting the game.
3. Start the game through the bridge. When Discord is logged in, the profile
   shows the session start time after a few seconds.

Without the bridge the game still runs normally, only the presence entry
stays empty. If presence works but the timer does not, Discord itself is too
old, update the client.

## Troubleshooting

**`error: game not found`** The search missed your install. Pass the folder
that contains `YandereSimulator.exe`, or export `YANDERE_SIM_PATH`.

**`Found more than one install`** Several folders contain a valid DLL. Pick one
from the printed list and pass it as the argument.

**`patcher is not executable`** The zip was extracted in a way that dropped the
permission bit, run `chmod +x YsPerfPatcher`.

**`error: the patcher stopped before touching the file` (exit 3)** The DLL is
not from the current official build. The patcher prints the group it could not
find. Nothing was written, the file keeps its previous state.

**`error: patcher failed with exit code 1`** The patcher was called with a bad
path or a missing target DLL, check the printed usage line.

**Exit codes of `YsPerfPatcher`**

| Code | Meaning |
|------|---------|
| 0 | Patched, the summary shows how many groups matched |
| 2 | Already patched, file untouched |
| 3 | A required pattern was missing, file untouched |
| 1 | Bad arguments or the DLL could not be read |

## Build from source

```bash
cd src/YsPerfPatcher
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/linux
```

Mono.Cecil comes in through the project file, no extra steps are required.
