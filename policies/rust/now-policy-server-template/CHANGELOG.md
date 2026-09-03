# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- [**breaking**] Add required policy management, validation, and optimistic replacement trait methods, routes, status mappings, and OpenAPI operations. Unsafe policy paths map to HTTP 409, unsupported non-JSON policy paths map to HTTP 422, and absent routes retain ordinary HTTP 404 behavior. Validation and replacement use a separate public 16 MiB full-request-body limit while package operations retain their 256 KiB limit.


## [[0.3.0](https://github.com/Devolutions/now-libraries/compare/now-policy-server-template-v0.2.0...now-policy-server-template-v0.3.0)] - 2026-08-05

### <!-- 1 -->Features

- Add package operation cancelation support ([#88](https://github.com/Devolutions/now-libraries/issues/88)) ([d8ba0f8515](https://github.com/Devolutions/now-libraries/commit/d8ba0f85153f2d1c54953a4c2f7dd4e6a37e5448)) 

- Add per-operation event channel protocol ([#89](https://github.com/Devolutions/now-libraries/issues/89)) ([d25e7eb631](https://github.com/Devolutions/now-libraries/commit/d25e7eb631b1f6c00ef1ee27c5b1d16a1381d298)) 

## [[0.2.0](https://github.com/Devolutions/now-libraries/compare/now-policy-server-template-v0.1.0...now-policy-server-template-v0.2.0)] - 2026-07-27

### <!-- 1 -->Features

- Advertise the new package managers (Apt, Bun, Cargo, Chocolatey, Dnf, Dotnet, Flatpak, Homebrew, Npm, Pacman, Pip, Scoop, Snap, Vcpkg) in the mock broker capabilities and sample documents ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))

## [[0.1.0](https://github.com/Devolutions/now-libraries/releases)] - 2026-06-30

### <!-- 1 -->Features

- Initial release of the Devolutions NOW policy package broker server template and mock broker
