# Game Plugin

The BepInEx 6 Unity IL2CPP plugin hosts the in-game chat UI and integrates the
local Companion with *The NOexistenceN of Lilith*.

## Runtime responsibilities

- Resolve `Archive.Instance.playerName` for each model request.
- Display the Unity uGUI/TMP compact and expanded chat views.
- Restore session history and seed clicked autonomous dialogue lines.
- Show model replies above Lilith without replacing native dialogue behavior.
- Start and stop the bundled Companion process with the game.
- Read allowlisted SMTC music sessions through the media bridge.
- Reuse the game's `MusicListeningBehavior` loop and exit lifecycle.
- Launch QQ Music or NetEase Cloud Music from the character interaction menu.
- Apply player-name changes through the game's native `TraySettingModel` API.

Browser media is excluded from music activation. Model responses are limited
to fixed text/emotion/animation fields and cannot execute arbitrary commands.

## Development build

The project requires BepInEx-generated IL2CPP interop assemblies from a legal
local game installation:

```powershell
dotnet build LilithMod.csproj -c Release -p:GameDir='full game directory'
```

Do not commit the game assemblies, generated interop directory, local logs or
runtime configuration. Public bundles are produced only through
`scripts/build-bundle.ps1`.
