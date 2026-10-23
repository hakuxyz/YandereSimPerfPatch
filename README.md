# YandereSimPerfPatch

A patcher for the current Windows build of Yandere Simulator. It edits
`Assembly-CSharp.dll` in place so the game keeps a higher and steadier frame
rate on weaker hardware. No content is removed, no gameplay logic is rewritten,
and saves keep working.

The injector finds your game folder, backs up the original file as
`Assembly-CSharp.dll.orig`, and applies thirteen independent patch groups.
Running it a second time changes nothing, and the restore switch puts the
original file back at any time.

Both binaries are self contained, so no .NET installation is required. The
patcher itself only rewrites the DLL and can be used on Windows and on Linux.
The game then runs the way it always did, either natively or through Wine.

## Before you start

You do not need any programming knowledge for this. Download the zip for your
system, extract it, run one file, done. The whole thing is open source, the
full code lives in this repository and everyone can read it, change it, and
check what it does to the game. It changes nothing online, there is no account
and no telemetry.

If something does not work, ask in the Discord or watch the video guide,
both links are at the bottom of this page.

## What the patch changes

| # | Change | Why it helps |
|---|--------|--------------|
| 1 | Students beyond 30 meters refresh every second frame, beyond 70 meters every third frame | A full school with 80 students is mostly CPU cost, and far away students do not need every frame |
| 2 | The same distance rule for animation weights in LateUpdate | Keeps animation sampling in step with the throttled updates |
| 3 | First launch defaults to VSync on | Avoids screen tearing without extra work |
| 4 | First launch defaults to bloom off | Removes a full screen shader pass |
| 5 | First launch defaults to ambient occlusion off | Removes a second full screen pass |
| 6 | First launch defaults to a 60 FPS limit | Uncapped frame rates stutter on older GPUs |
| 7 | First launch defaults to particle count low | Fewer particles during rain, dust, and crowd scenes |
| 8 | First launch defaults to low detail students at 20 meters | Distant students use the simple model |
| 9 | Soft shadows become hard shadows everywhere | Roughly half the shadow shader cost, only matters when shadows are on (they start off) |
| 10 | The first run of the game sets draw distance to 200 instead of 350 | Less geometry to render each frame, the setting stays adjustable up to 350 |
| 11 | Bloom and ambient occlusion settings from the options screen are applied on level start | The original never reloaded the post processing profile, so the options silently did nothing in some scenes |
| 12 | Discord Rich Presence now sends a session start time | The Discord profile shows a running playtime timer |
| 13 | Press F10 on the title screen to write the full low spec preset into the options | One key press gives an existing profile the same values first time players get, and the title bar confirms with Low Spec Preset Applied |

Groups 3 through 8 only write their value when the corresponding setting does
not exist yet, so an existing profile is never overwritten. Everything else
checks its own marker and skips when the DLL was already patched.

## Install on Windows

1. Download `YandereSimPerfPatch-win-x64.zip` from the releases page.
2. Extract it anywhere, keep the files together.
3. Close Yandere Simulator, Windows locks the DLL while the game runs.
4. Double click `inject.bat`.
5. The injector prints the game folder it found and the patch summary.

If the search comes up empty, start it with the folder that contains
`YandereSimulator.exe`:

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1 -GamePath "D:\Games\YandereSimulator"
```

The full Windows documentation, including the list of searched folders and
troubleshooting, is in [docs/WINDOWS.md](docs/WINDOWS.md).

## Install on Linux

1. Download `YandereSimPerfPatch-linux-x64.zip` from the releases page.
2. Extract it, then make the two files executable:

```bash
chmod +x inject.sh YsPerfPatcher
```

3. Run the injector:

```bash
./inject.sh
```

4. It searches common locations, prints the DLL it found, and patches it.

When the search misses, pass the game folder yourself:

```bash
./inject.sh "$HOME/Games/YandereSimulator"
```

Or set the location once for future runs:

```bash
export YANDERE_SIM_PATH="$HOME/Games/YandereSimulator"
```

The full Linux documentation, including the Wine and Discord notes, is in
[docs/LINUX.md](docs/LINUX.md).

## Restore the original DLL

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1 -Restore
```

Linux:

```bash
./inject.sh --restore
```

Both take an optional folder argument when the search would find nothing. The
backup `Assembly-CSharp.dll.orig` sits next to the DLL and is created on the
first patch run.

## Updates

Every normal run of the injector checks GitHub for a newer version. When one
is available, a desktop notification tells you and the console shows the same
line. To update, run the injector with the update switch:

```powershell
powershell -ExecutionPolicy Bypass -File .\inject.ps1 -Update
```

```bash
./inject.sh --update
```

It downloads the newest release, replaces itself and the patcher with it, and
patches your game right after. The check stays quiet when you are offline.

## After a game update

YandereDev replaces `Assembly-CSharp.dll` with every release, and the backup
next to it may be gone. Run the injector again and it patches the new file. The
patcher is built for the current official build (Unity 6000.2.8f1). Older
builds have different code shapes and make it exit with code 3 without touching
the file, which is the moment to stop and report it.

## Settings worth checking in game

First time players get the defaults from groups 3 through 8 automatically.
Existing profiles do not need to set anything by hand: press F10 on the main
menu once, the title bar confirms with Low Spec Preset Applied, and the table
below shows what changes. Every value stays adjustable in the options screen
afterwards:

| Setting | Value |
|---------|-------|
| FPS limit | 60 |
| VSync | on, off when your monitor runs above 60 Hz |
| Draw distance | 200 |
| Particles | low |
| Low detail students | on, 20 meters |
| Shadows | off for the highest frame rate, on now uses hard shadows |
| Bloom, ambient occlusion | off |
| Motion blur, depth of field | off |
| Post anti aliasing | on, cheap and cleans up edges |

The options screen also has a shortcut to the built in low spec preset: press
lowercase `l` there and everything jumps to the lowest values.

## Limits

The patch targets the single player build. There is no multiplayer and no anti
cheat to worry about, and nothing is injected at runtime, the DLL on disk is
simply written once.

Under Wine and Proton, Discord Rich Presence needs a working IPC bridge before
the game starts and the Discord desktop client has to be running. Without both,
only the presence entry is missing, the game runs normally. See
[docs/LINUX.md](docs/LINUX.md).

## Build from source

The patcher is a .NET 10 console application using Mono.Cecil.

```bash
cd src/YsPerfPatcher
dotnet build -c Release
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/linux
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/win
```

The injector scripts live in `injector/` and expect the patcher binary next to
them.

## Getting help

The Discord server is the fastest place to get help, also for complete
beginners: https://discord.gg/Qf5cgQK3Y2

There is a video guide on YouTube that shows every click:
https://www.youtube.com/@xyzhakuu

## Documentation

| File | Content |
|------|---------|
| [docs/WINDOWS.md](docs/WINDOWS.md) | Windows install, detection, restore, troubleshooting |
| [docs/LINUX.md](docs/LINUX.md) | Linux install, Wine and Discord notes, troubleshooting |

## License

MIT, see [LICENSE](LICENSE). Yandere Simulator and its assets belong to
YandereDev and are not covered by it.
