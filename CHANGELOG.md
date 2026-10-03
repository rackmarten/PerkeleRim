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

### Changed

- Project renamed from SkyrimSummerCar to PerkeleRim (tagline "Fus-Perkele-Dah!").
