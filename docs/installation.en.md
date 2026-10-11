# Install, update, and uninstall

## Install

1. Download `The-NOexistenceN-of-Lilith-Mod-v0.3.4-win-x64.zip` from Releases and compare its SHA-256 with the published checksum.
2. Quit *The NOexistenceN of Lilith* from the tray and wait until Steam no longer shows **Stop**.
3. Extract every file into the game root. `winhttp.dll`, `Lilith.exe`, `BepInEx`, and `dotnet` should be in that game directory.
4. Start from Steam. The first IL2CPP cache build can take longer; do not force-close it while loading.
5. Right-click Lilith or the tray icon and open **Mod Settings**. Choose **Follow game language** or an override under **Other**, then enter an API supplied by your own model provider if you want AI chat.

The package already contains BepInEx, Node.js, and the media bridge. Players do not install or pin Node globally. Mod Settings uses Windows PowerShell 5.1 included with Windows 10/11.

The game checks the latest stable GitHub Release once in the background at startup. No dialog appears when the installed version is current or GitHub is unavailable. A newer version only prompts to open its Release page; nothing is downloaded or replaced automatically.

## Configure Gemini

1. Create your own Gemini API key in Google AI Studio and confirm Gemini API and Google Search grounding are available in your region.
2. Under **Mod Settings → AI API**, choose **Google Gemini official**. The preset fills the official Base URL, `gemini-3.6-flash`, native Interactions API, and `low` thinking.
3. Select **Built into model** for model-directed Google Search, or keep Search off/use Brave/SearXNG. The separate viewer and Search billing occur only when Gemini actually executes a search.
4. If Google cannot be reached directly, enter an unauthenticated HTTP/HTTPS proxy. The settings test and in-game Companion use the same proxy path.

The viewer uses the Microsoft Edge WebView2 Evergreen Runtime normally present on Windows 10/11. If it is missing, the same one-time page opens in the default browser with the grounded output, citations, and Search Suggestions intact. `store=false` disables the hosted interaction chain only; free-tier data use and the 30-day Search-grounding request retention remain governed by Google's terms.

## Update

Fully quit the game, then overwrite the old files with the new ZIP. API settings, sessions, and player memories under `%APPDATA%\LilithAI` are not overwritten. Changing the Mod language does not translate history or long-term memories.


Updates replace program files in the game directory. API keys, settings, chat history and long-term memory remain under `%APPDATA%\LilithAI`; native game saves are not included or overwritten. Keep this user data directory, and copy it separately if you want a backup. Automatic backup DNS is enabled by default. Saved and manually entered model names are preserved.

## Uninstall

After quitting the game, remove the bundled BepInEx/Doorstop files or verify game files in Steam. To erase API settings, sessions, and player memories as well, manually delete `%APPDATA%\LilithAI`.

## Troubleshooting

- Mod not loaded: make sure `winhttp.dll` is beside `Lilith.exe`, then inspect `BepInEx\LogOutput.log`.
- Settings do not open: check that `BepInEx\plugins\companion\configure.ps1` and `BepInEx\plugins\locales` exist.
- Missing glyphs: set the base game to the matching language first. The Mod reuses the game's TMP font and fallbacks.
- Gemini cannot connect: check regional availability, key, Base URL, and proxy. Authenticated or SOCKS proxies are not accepted.
- No separate Search window: it opens only after an actual Google Search call. Check WebView2/default-browser availability and security-software blocks.
- Remove private dialogue, player names, and local paths before sharing logs.
