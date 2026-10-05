# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Repository scaffold: README, MIT license, contributing guide, code of conduct, security policy, issue and PR templates.
- Design document ([docs/DESIGN.md](docs/DESIGN.md)) and protocol proposal ([docs/PROTOCOL.md](docs/PROTOCOL.md)).
- Windows development setup guide ([docs/DEV_SETUP.md](docs/DEV_SETUP.md)) and `scripts/build.ps1`, `deploy-local.ps1`, `logs.ps1`.
- Hello-world SKSE plugin (CMake, vcpkg, CommonLibSSE-NG) and MSCLoader mod (`net35`, `dotnet build`) that log when they load.
- GitHub Actions build on `windows-latest`; the C# mod compiles against MSCLoader API stubs there.
- Hidden-MSC spike ([docs/SPIKE_HIDDEN_MSC.md](docs/SPIKE_HIDDEN_MSC.md)): MSC input redirect and FSM dump tools, Skyrim per-frame player placement test (F10), PlayMaker FSM map ([docs/FSM_MAP.md](docs/FSM_MAP.md)).
- Dev loop scripts: `scripts/launch-msc.ps1` (no-click MSC start), `msc-save.ps1` (save snapshots), `spike.ps1` (commands and input scripts for the running game).

- Protocol v1 link ([docs/PROTOCOL.md](docs/PROTOCOL.md)): shared memory section with version handshake and heartbeat, latest-value slots for input and player state, one ring per direction, Skyrim <-> Unity coordinate conversion. C++ in `protocol/` (used by the SKSE plugin), C# mirror in `msc/src/Link`. Both games log `link up` when running together.
- Unit tests for both ends (`scripts/test.ps1`, also run in CI): C++ doctest, C# NUnit, layout checked against the C++ header's asserts, shared conversion vectors.

### Changed

- `scripts/launch-msc.ps1` keeps watching for Unity's configuration dialog until the game window appears (it can take over 10 s on a cold start).

- Project renamed from SkyrimSummerCar to PerkeleRim (tagline "Fus-Perkele-Dah!").
