# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [[0.4.0](https://github.com/Devolutions/now-libraries/compare/now-policy-server-template-v0.3.0...now-policy-server-template-v0.4.0)] - 2026-09-03

### <!-- 1 -->Features

- [**breaking**] Add the required `PackageBrokerServer::active_policy` method ([#93](https://github.com/Devolutions/now-libraries/issues/93)) ([cd5a6e9f8e](https://github.com/Devolutions/now-libraries/commit/cd5a6e9f8eeb3c70cc9ef9003ffeb46716ceced6))
- Expose active policy inspection through `GET /v1/policy` ([#93](https://github.com/Devolutions/now-libraries/issues/93)) ([cd5a6e9f8e](https://github.com/Devolutions/now-libraries/commit/cd5a6e9f8eeb3c70cc9ef9003ffeb46716ceced6))
- Return `404 Not Found` when no active policy is configured ([#93](https://github.com/Devolutions/now-libraries/issues/93)) ([cd5a6e9f8e](https://github.com/Devolutions/now-libraries/commit/cd5a6e9f8eeb3c70cc9ef9003ffeb46716ceced6))
- [**breaking**] Add the required `PackageBrokerServer::policy_management` method and expose it through `GET /v1/policy/management` ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- [**breaking**] Add the required `PackageBrokerServer::validate_policy` method and expose it through `POST /v1/policy/validate` ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- [**breaking**] Add the required `PackageBrokerServer::replace_policy` method and expose it through `PUT /v1/policy` ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Map policy management failures to structured HTTP status responses ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add the public 16 MiB `MAX_POLICY_MANAGEMENT_BODY_BYTES` limit for policy validation and replacement requests ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))

### <!-- 4 -->Bug Fixes

- [**breaking**] Remove the public `MockPackageBrokerServer` test double from the crate API ([#97](https://github.com/Devolutions/now-libraries/issues/97)) ([2f8f425def](https://github.com/Devolutions/now-libraries/commit/2f8f425def03720189a96a657571551918e1034d))

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
