# Contributing

Thanks for wanting to help bring a Finnish summer to Skyrim. The project is still at the prototype stage, so the codebase will move fast and things will break. Opening an issue before starting anything big saves everyone time.

## Ground rules

- Be kind. Everyone here follows the [Code of Conduct](CODE_OF_CONDUCT.md).
- Keep pull requests focused: one feature or fix per PR.
- Update [CHANGELOG.md](CHANGELOG.md) under `## [Unreleased]` for anything a user would notice.
- Update the docs in [`docs/`](docs/) when you change the design or the protocol.

## Game assets

This rule is strict because the project can only stay public if it holds:

- **Never commit assets from My Summer Car or Skyrim**: meshes (`.nif`, Unity dumps), textures (`.dds`), archives (`.bsa`, `.ba2`), audio, or game DLLs (`Assembly-CSharp.dll`, `PlayMaker.dll`, `UnityEngine*.dll`, ...).
- Tools may convert or extract assets from the user's **own** install at build time or runtime, but the output must never be committed or shipped.
- Original assets you made yourself (for example a garage model) are welcome, under the project's MIT license.

`.gitignore` blocks the common file types, but please check your diff anyway.

## Development setup

All building and testing happens on Windows, since both games run there. A full setup guide (`docs/DEV_SETUP.md`) will land with the first buildable code. In short you'll need:

- Skyrim Anniversary Edition (Steam, latest), SKSE64, Address Library
- My Summer Car (Steam) with MSCLoader
- Visual Studio 2022 with the C++ and .NET desktop workloads, CMake, vcpkg

## Workflow

1. Fork the repo and create a branch from `main`.
2. Make your change, with tests where the code is testable outside the games (protocol layout, coordinate conversion, and so on).
3. Make sure the CI build passes.
4. Open a pull request using the template and describe how you tested it in-game.

## Commit messages

Start the subject with one of these prefixes:

| Prefix | Use for |
|---|---|
| `Add:` | New functionality or resource |
| `Edit:` | Change to existing functionality |
| `Fix:` | Bug fix |
| `Update:` | Dependency, version, configuration or data update |
| `Remove:` | Removed functionality or resource |
| `Docs:` | Documentation only |
| `DevOps:` | CI, build, tooling |

## Code style

- Follow [.editorconfig](.editorconfig).
- C++: match the style of CommonLibSSE-based plugins (C++23, `clang-format` config to come with the first code).
- C#: standard .NET conventions; the mod targets .NET Framework 3.5 because My Summer Car runs on an old Unity/Mono.

## Reporting bugs

Use the bug report template. Include your Skyrim runtime version, My Summer Car version, MSCLoader version, and the logs from both sides.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
