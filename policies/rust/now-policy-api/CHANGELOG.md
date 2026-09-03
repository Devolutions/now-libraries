# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [[0.4.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.3.1...now-policy-api-v0.4.0)] - 2026-09-03

### <!-- 1 -->Features

- Add `PolicyResponse` for active policy inspection ([#93](https://github.com/Devolutions/now-libraries/issues/93)) ([cd5a6e9f8e](https://github.com/Devolutions/now-libraries/commit/cd5a6e9f8eeb3c70cc9ef9003ffeb46716ceced6))
- [**breaking**] Make `now-policy` a required dependency because `PolicyDocument` is part of `PolicyResponse` ([#93](https://github.com/Devolutions/now-libraries/issues/93)) ([cd5a6e9f8e](https://github.com/Devolutions/now-libraries/commit/cd5a6e9f8eeb3c70cc9ef9003ffeb46716ceced6))
- [**breaking**] Remove the `policy-compat` feature and its cross-model conversions ([#93](https://github.com/Devolutions/now-libraries/issues/93)) ([cd5a6e9f8e](https://github.com/Devolutions/now-libraries/commit/cd5a6e9f8eeb3c70cc9ef9003ffeb46716ceced6))
- Add policy management snapshots for active, missing, and invalid policies ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add raw-draft validation contracts with structured findings and receipts ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add optimistic policy replacement contracts with explicit update, identity replacement, creation, and repair operations ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add policy management error contracts, including the current snapshot on stale-token errors ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))

## [[0.3.1](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.3.0...now-policy-api-v0.3.1)] - 2026-08-13

### <!-- 4 -->Bug Fixes

- Allow '/' and ':' in PackageIdentifier ([#91](https://github.com/Devolutions/now-libraries/issues/91)) ([0bac944c46](https://github.com/Devolutions/now-libraries/commit/0bac944c464b6c54f951eb80c2085baa301273b7)) 



## [[0.3.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.2.0...now-policy-api-v0.3.0)] - 2026-08-05

### <!-- 1 -->Features

- [**breaking**] Add package operation cancelation support ([#88](https://github.com/Devolutions/now-libraries/issues/88)) ([d8ba0f8515](https://github.com/Devolutions/now-libraries/commit/d8ba0f85153f2d1c54953a4c2f7dd4e6a37e5448)) 

- [**breaking**] Add per-operation event channel protocol ([#89](https://github.com/Devolutions/now-libraries/issues/89)) ([d25e7eb631](https://github.com/Devolutions/now-libraries/commit/d25e7eb631b1f6c00ef1ee27c5b1d16a1381d298)) 

## [[0.2.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.1.0...now-policy-api-v0.2.0)] - 2026-07-27

### <!-- 1 -->Features

- [**breaking**] Add 14 new package manager variants to `ManagerName` (Apt, Bun, Cargo, Chocolatey, Dnf, Dotnet, Flatpak, Homebrew, Npm, Pacman, Pip, Scoop, Snap, Vcpkg) ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))
- Add `CapabilitiesResponse::manager_capability` and `CapabilitiesResponse::supports_manager` helpers for querying advertised broker capabilities ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))
- Extend `policy-compat` bidirectional `ManagerName` conversions to cover the new package managers ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))

## [[0.1.0](https://github.com/Devolutions/now-libraries/releases)] - 2026-06-30

### <!-- 1 -->Features

- Initial release of the implementation-agnostic Devolutions NOW policy package broker API model
