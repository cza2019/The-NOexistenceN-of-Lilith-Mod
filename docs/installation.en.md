# Install, update, and uninstall

## Install

1. Download `The-NOexistenceN-of-Lilith-Mod-v0.3.0-win-x64.zip` from Releases and compare its SHA-256 with the published checksum.
2. Quit *The NOexistenceN of Lilith* from the tray and wait until Steam no longer shows **Stop**.
3. Extract every file into the game root. `winhttp.dll`, `Lilith.exe`, `BepInEx`, and `dotnet` should be in that game directory.
4. Start from Steam. The first IL2CPP cache build can take longer; do not force-close it while loading.
5. Right-click Lilith or the tray icon and open **Mod Settings**. Choose **Follow game language** or an override under **Other**, then enter an API supplied by your own model provider if you want AI chat.

The package already contains BepInEx, Node.js, and the media bridge. Players do not install or pin Node globally. Mod Settings uses Windows PowerShell 5.1 included with Windows 10/11.

## Update

Fully quit the game, then overwrite the old files with the new ZIP. API settings, sessions, and player memories under `%APPDATA%\LilithAI` are not overwritten. Changing the Mod language does not translate history or long-term memories.

## Uninstall

After quitting the game, remove the bundled BepInEx/Doorstop files or verify game files in Steam. To erase API settings, sessions, and player memories as well, manually delete `%APPDATA%\LilithAI`.

## Troubleshooting

- Mod not loaded: make sure `winhttp.dll` is beside `Lilith.exe`, then inspect `BepInEx\LogOutput.log`.
- Settings do not open: check that `BepInEx\plugins\companion\configure.ps1` and `BepInEx\plugins\locales` exist.
- Missing glyphs: set the base game to the matching language first. The Mod reuses the game's TMP font and fallbacks.
- Remove private dialogue, player names, and local paths before sharing logs.
