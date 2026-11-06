# YandereSimPerfPatch

A patcher for the current Windows build of Yandere Simulator. It edits `Assembly-CSharp.dll` in place so the game keeps a higher and steadier frame rate on weaker hardware. No content is removed, no gameplay logic is rewritten, and saves keep working.

Eleven independent patch groups. The injector backs up the original file as `Assembly-CSharp.dll.orig` and applies them. Running it twice changes nothing, and the restore switch puts the original file back at any time.

## What it does

- Higher and steadier frame rate on weaker hardware
- Safe low graphics defaults on first launch: bloom, ambient occlusion, and high shadows start off
- No online changes, no account, no telemetry
- Full source in this repository; anyone can read, change, and check what it does to the game

The game then runs exactly the way you start it today, natively or through Wine.

## Quick start

You do not need any programming knowledge. Download the zip for your system, extract it, run one file, done.

1. Download `YandereSimPerfPatch-win-x64.zip` or `YandereSimPerfPatch-linux-x64.zip` from the Releases page.
2. Extract it into one folder. Keep the injector script and `YsPerfPatcher` together.
3. Close Yandere Simulator. The game locks its DLL while it runs, and the injector refuses to touch a locked file.
4. Run the injector:
   - Windows: double click `inject.bat`
   - Linux: `./inject.sh` (make it executable with `chmod +x` first)
5. Start the game. The first launch applies the safe defaults from the patch.

The console shows the game folder it found and the patch summary, for example `Summary: 11/11 patch groups applied.`

## Launcher (easiest way)

If you prefer buttons over the terminal, grab `YsPerfLauncher`. Pick your game folder once, then:

- **Patch** backs up the original DLL and applies the patch
- **Restore** puts the original back
- **Launch Game** starts the game the same way the injector scripts do
- **Download Game** fetches the official zip from yanderesimulator.com and extracts it into the chosen folder
- **Discord / YouTube / GitHub** links sit at the top
- A status dot shows the state: gray vanilla, green patched, red no game found
- A log box shows every step, the same messages as the command line tool
- The chosen folder is remembered between starts

## Documentation

| File | Content |
|------|---------|
| [docs/WINDOWS.md](docs/WINDOWS.md) | Windows install, detection, restore, troubleshooting |
| [docs/LINUX.md](docs/LINUX.md) | Linux install, Wine and Discord notes, troubleshooting |
| [docs/MOBILE.md](docs/MOBILE.md) | Android, GameHub, and the mobile Yandere Simulator community |

## Build from source

The patcher is a .NET 10 console application using Mono.Cecil:

```bash
cd src/YsPerfPatcher
dotnet build -c Release
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/linux
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/win
```

The injector scripts live in `injector/` and expect the patcher binary next to them.

The launcher is an Avalonia app that calls the same patcher code in process:

```bash
cd src/YsPerfLauncher
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/linux
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win
```

No .NET installation is needed to run the result. Prebuilt zips live in the releases.

## Getting help

The Discord server is the fastest place to get help, also for complete beginners: https://discord.gg/Qf5cgQK3Y2

There is a video guide on YouTube that shows every click: https://www.youtube.com/@xyzhakuu

## License

MIT, see [LICENSE](LICENSE). Yandere Simulator and its assets belong to YandereDev and are not covered by it.
