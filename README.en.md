# The NOexistenceN of Lilith Mod

[简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md)

This Mod adds generative-AI conversation, long-term player memory, local lore retrieval, relevant PC context, and external-music synchronisation to the Steam desktop pet *The NOexistenceN of Lilith*. Players only need to configure their own model provider, or may use the music features without AI.

> Current version: 0.3.2 · Windows 10/11 x64

## Highlights

- Lilith consistently continues from Ending 1 (the Happy Ending) of *The NOexistenceN of you AND me*.
- One package supports Simplified Chinese, Traditional Chinese, English, and Japanese. It follows the game language by default and can be overridden in Mod Settings.
- Native Gemini Interactions API, OpenAI-compatible APIs, and presets for DeepSeek, Kimi, Qwen, GLM, and Volcengine Ark.
- Conversation history and long-term player memories are stored locally. All languages share the player's original text; switching languages never translates or duplicates the profile.
- Local time and allow-listed music-app metadata are included only when relevant.
- Optional provider search, Brave Search, or SearXNG; failed search never blocks ordinary chat.
- In-game Unity chat window, overhead replies, history, clickable native dialogue, music-player launch, and native music animation sync.
- One background GitHub Release check per game launch. Only a newer stable version triggers a confirmation prompt; files are never downloaded or replaced automatically.

## Install

1. Download `The-NOexistenceN-of-Lilith-Mod-v0.3.2-win-x64.zip` from GitHub Releases.
2. Fully quit *The NOexistenceN of Lilith*.
3. Extract every file into the game root so that `winhttp.dll` is beside `Lilith.exe`.
4. Start the game from Steam. The first BepInEx launch may take longer.
5. Right-click Lilith or the tray icon, open **Mod Settings**, choose a language, and enter your own model API details.

The package includes BepInEx 6 IL2CPP, verified Unity base libraries, Node.js 24 LTS, and a self-contained media bridge. Players do not install them separately. See [Install, update, and uninstall](docs/installation.en.md).

## Gemini and Google Search

Choose **Google Gemini official** under **Mod Settings → AI API** and enter your own Gemini API key. The preset uses the native Interactions API with stable `gemini-3.6-flash` and `low` thinking by default. Every request sends `store=false`; multi-turn context is rebuilt from locally visible user/assistant history rather than a Google-hosted interaction chain.

With **Built into model** selected, Gemini decides whether Google Search is needed. The normal reply and source links stay in game. Only an actual search call opens a separate window containing the raw grounded output, citations, and Google Search Suggestions. Its random one-time page lives briefly in memory and is destroyed on first read. If the WebView2 Evergreen Runtime is unavailable, the same complete page opens in the default browser.

An optional unauthenticated `http://` or `https://` proxy can be configured for restricted networks. Priority is the explicit setting, `HTTPS_PROXY/HTTP_PROXY`, then direct connection; loopback always bypasses it.

## Privacy and licensing

- API keys stay in `%APPDATA%\LilithAI\config.json`.
- Chat and enabled temporary context are sent to the model or search provider chosen by the player.
- Browser video, clipboard, active window, files, battery, and network state are not read.
- `store=false` opts out of the hosted interaction chain only. Free-tier Gemini input/output may be used for service improvement; Google Search grounding request data remains subject to Google's 30-day retention terms, and Gemini 3 search queries may be billed when the model executes them.
- Original code is under the [MIT License](LICENSE). Character art and third-party components have separate terms; see [Asset Notice](ASSET-NOTICE.md) and [Third-Party Notices](THIRD-PARTY-NOTICES.md).
- This project does not include the game. Users must own and install *The NOexistenceN of Lilith* through Steam.
