# AGENTS.md

Notes for coding agents working on PerkeleRim. Humans: [CONTRIBUTING.md](CONTRIBUTING.md) has the same rules in long form.

## Read first

`docs/DESIGN.md` (how the SKSE plugin and the MSCLoader mod fit together), `docs/PROTOCOL.md` (the shared memory between them) and `docs/DEV_SETUP.md` (the build and the CI).

## Building and testing on Windows

Everything builds and runs on Windows, where both games are installed:

- Run `scripts/*.ps1` from PowerShell, or `pwsh -NoProfile -File scripts/<name>.ps1` from Git Bash. They set up the MSVC dev shell themselves; `cmake` isn't on PATH outside them.
- `scripts/build.ps1` builds both sides, `scripts/test.ps1` runs the game-free tests (C++ doctest and C# link tests), `scripts/logs.ps1` tails both logs.
- `scripts/restart-games.ps1` is the whole in-game loop: it closes both games, optionally builds, then deploys and starts them. Closing the games discards anything unsaved in them.
- `scripts/msc-save.ps1` snapshots and restores My Summer Car's save, so a test can start from a known state.
- `scripts/skyrim-key.ps1` presses a key in Skyrim (e.g. F10 for the handover) and can take a screenshot.
- Put code that doesn't need a game somewhere testable (protocol headers, coordinate conversion) and cover it in `scripts/test.ps1`. Keep the game-facing code a thin layer over it.
- Files are LF (`.gitattributes`). Edit them with your editor tools: PowerShell's `Set-Content` and Python's default text mode on Windows write CRLF.

Without Windows (a Linux sandbox, say) nothing builds. Use the CI build on your pull request as compiler and test runner: `gh pr checks`, then `gh run view <run-id> --log-failed`.

## Rules

- Never commit assets from My Summer Car or Skyrim (meshes, textures, archives, audio, game DLLs). Tools may read the user's own install at build time or runtime; their output is never committed or shipped.
- Commit subjects start with `Add:`, `Edit:`, `Fix:`, `Update:`, `Remove:`, `Docs:` or `DevOps:`.
- Add an entry under `## [Unreleased]` in `CHANGELOG.md` for anything a player would notice.
- A protocol layout change updates `docs/PROTOCOL.md` in the same pull request.
- Every pull request says how it was tested in-game: what to start, what to press, what you should see.
