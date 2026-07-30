# Lilith Companion

Lilith Companion is the dependency-free Node.js service used by the Mod. It
listens only on loopback and creates a per-run bearer token in
`%APPDATA%\LilithAI\runtime.json`.

## Responsibilities

- Native Gemini Interactions plus OpenAI-compatible Chat Completions and Responses providers.
- Presets for Gemini, custom relays, DeepSeek, Kimi, Qwen, GLM and Volcengine Ark.
- Optional provider search, Brave Search or SearXNG.
- Local session history and bounded player long-term memory.
- Reviewed local retrieval from `character/lore/index.json`.
- Relevant-only local time and allowlisted music context.
- Strict response normalization and parent-process lifecycle cleanup.
- Stateless Gemini requests (`store=false`) rebuilt from local visible transcripts.
- In-memory, nonce-protected Google Search grounding pages for the WebView2 viewer.
- Node 24 proxy routing with explicit config, environment fallback, and loopback bypass.

No AstrBot installation is required. The public Windows bundle includes Node
24 LTS as `companion/node.exe`; `LILITH_NODE_PATH` is a development override.

## Local data

Runtime configuration is stored outside the game under `%APPDATA%\LilithAI`.
The repository and release package contain only an empty
`config.example.json`. API keys, chat history and player memory must never be
copied into the source repository or release package.

Gemini thought/signature/tool steps and Search Suggestions are never written to
session files. Only visible user/assistant text is retained. An actual
`google_search_call` creates a short-lived one-time page; the public Companion
response continues to expose citations through protocol v1.

## Tests

```powershell
npm test
```

The test suite uses mock providers and must not require a real API key.
