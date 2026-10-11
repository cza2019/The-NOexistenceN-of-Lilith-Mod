# Lilith Companion

Lilith Companion is the dependency-free Node.js service used by the Mod. It
listens only on loopback and creates a per-run bearer token in
`%APPDATA%\LilithAI\runtime.json`.

## Responsibilities

- Native Gemini Interactions plus OpenAI-compatible Chat Completions and Responses providers.
- Presets for Gemini, custom relays, DeepSeek, Kimi, Qwen, GLM and Volcengine Ark.
- Transient provider connection failures and HTTP 408/429/500/502/503/504 responses retry once within the configured reply timeout. Language correction shares that time budget. Diagnostic logs include error codes, HTTP status and elapsed time, without API keys, response bodies or chat text.
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

Provider connections use system DNS first. With
`network.provider_dns_fallback_enabled` (default `true`), TLS establishment
failures on direct official provider HTTPS endpoints retry once with Ali DNS
`223.5.5.5`. Successful backup resolution is preferred for that hostname for
10 minutes; reloading configuration clears this preference. Connection
establishment is limited to 5 seconds, and all retries and language correction
share the configured request timeout. Custom relay domains and proxy connections
do not automatically switch DNS. HTTP authentication, balance and parameter
errors never switch DNS; certificate checks remain enabled.

The optional `network.provider_dns_servers` array retains manual provider DNS
configuration and takes precedence over automatic recovery. This does not change
Windows DNS or pin IP addresses. Search and update requests use system DNS.
Settings connection tests call the same bundled Node request module with credentials
passed through stdin. Logs record safe error codes, connection stage, DNS route,
elapsed time and retries, without keys, request bodies or response bodies.

Gemini thought/signature/tool steps and Search Suggestions are never written to
session files. Only visible user/assistant text is retained. An actual
`google_search_call` creates a short-lived one-time page; the public Companion
response continues to expose citations through protocol v1.

## Tests

```powershell
npm test
```

The test suite uses mock providers and must not require a real API key.
