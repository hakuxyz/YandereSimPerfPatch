# Mobile and Android (GameHub)

YandereSimPerfPatch patches a standalone `Assembly-CSharp.dll`. It is not tied to a store, a launcher, or a platform, so there is nothing in the patch code itself that limits it to PC. The PC releases are the supported ones today.

## GameHub

GameHub is a PC emulator for Android (gamehub.xiajio.com). The community has asked about running Yandere Simulator on Android through GameHub, and a GameHub-compatible release is being explored as a separate build category.

This is not part of the PC patcher releases, and it is not yet available. When it lands, it will live here as its own category with its own install steps, so PC users and Android users never patch the wrong build.

## What would a mobile version need

- A GameHub-compatible build of the patched `Assembly-CSharp.dll`
- A GameHub-compatible injector that finds the game folder inside Android storage
- Verification on real Android hardware, because Wine-on-Android (box64 / Wine-box) behaves differently from desktop Wine

If you want to help test it, say so in the Discord: https://discord.gg/Qf5cgQK3Y2

## What stays the same

- No content removed
- No gameplay logic rewritten
- Saves keep working
- No online changes, no account, no telemetry
