# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [[0.4.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.3.1...now-policy-api-v0.4.0)] - 2026-09-03

### <!-- 1 -->Features

- Add active package policy inspection contract ([#93](https://github.com/Devolutions/now-libraries/issues/93)) ([cd5a6e9f8e](https://github.com/Devolutions/now-libraries/commit/cd5a6e9f8eeb3c70cc9ef9003ffeb46716ceced6)) 

  - add canonical `GET /v1/policy` support across the Rust server
  contract, generated OpenAPI, C# DTOs, and
  `BrokerClient.GetPolicy(CancellationToken)`
  - return the existing canonical `PolicyDocument` inside a versioned
  `PolicyResponse`, with structured `404 NotFound` behavior when no active
  policy is configured
  - make policy inspection unconditional: remove `policy-compat` and its
  cross-model conversion impls, and keep runtime mapping ownership in
  broker implementations
  - name the required Rust server accessor `active_policy`, leaving clear
  room for a future explicit `replace_policy` operation backed by a
  separate policy-store abstraction
  - harden C# successful-response validation for required/null fields,
  collection elements, unknown members, and canonical enum casing
  - preserve existing OpenAPI component names by namespacing colliding
  embedded policy components as `PolicyModel…`
  - run workspace Rust tests and Clippy with all features through the
  standard xtask commands
  
  ## API changes
  
  ### Rust
  
  - `now-policy-api` adds public `PolicyResponse` and
  `PolicyResponseKind`; `now-policy` is now a normal dependency because
  `PolicyDocument` is part of the permanent wire contract
  - `PackageBrokerServer` adds the required method:
  
    ```rust
  async fn active_policy(&self) -> Result<PolicyResponse, ErrorResponse>;
    ```
  
  - `now-policy-server-template` registers the route unconditionally and
  extends its mock with policy response/error builders
  - `policy-compat` and its `From`/`TryFrom` conversions are removed
  
  ### C#
  
  - `Devolutions.Now.Policy.Api` adds `PolicyResponse`, embedding
  `Devolutions.Now.Policy.Model.PolicyDocument`
  - `Devolutions.Now.Policy.Client` adds `GetPolicy(CancellationToken)`
  with strict validation of successful response bodies
  - policy-model deserialization now enforces required members, non-null
  collection elements, and canonical enum casing
  
  ### HTTP
  
  - adds read-only `GET /v1/policy`
  - `200`: `PolicyResponse`
  - `404`: structured `ErrorResponse` when no active policy is configured
  - other failures use the existing `ErrorResponse` contract
  - no policy mutation endpoint is introduced
  
  ---------

- [**breaking**] Add package policy management contract ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b)) 

  Add a versioned package-policy management contract alongside the
  unchanged active-policy inspection API. New management, validation, and
  replacement endpoints expose atomic Active/Missing/Invalid snapshots,
  raw-draft authoritative validation with structured findings and
  warning-bound receipts, and exact-token optimistic replacement with
  explicit Update, ReplaceIdentity, Create, and Repair intents. Rust
  server implementations gain the corresponding required trait methods and
  routes, while .NET gains NativeAOT-safe DTOs and cancellation-aware
  client APIs.
  
  Make policy documents JSON-only, removing Rust `parse_policy_yaml` and
  .NET `PolicyDocument.ParseYaml`. Introduce `PolicyDraftDocument` for
  authored policy content without server-managed revision and publication
  metadata, with its own versioned JSON Schema identity and explicit named
  conversions to and from committed policies. Rename the public .NET
  serialization helpers to `PolicySerializer` and `BrokerSerializer`, and
  replace Rust’s lossy `From<&PolicyDocument>` draft projection with
  `PolicyDocument::to_draft()`.
  
  Tighten cross-language contract validation for boolean matches, revision
  bounds, Unicode text lengths, opaque ASCII tokens and receipts,
  validation results, management snapshots, stale-token errors, and
  nullable schema fields. Unsafe paths and stale state use conflict
  semantics, unsupported policy formats and filesystems use
  unprocessable-entity semantics, and absent newer routes remain ordinary
  404 responses. Validation and replacement accept complete HTTP request
  bodies up to 16 MiB through public Rust and .NET constants, while
  package-operation limits remain unchanged.



### Added

- Add versioned policy management, raw-draft validation, structured findings/receipts, optimistic replacement, and management error contracts, including the atomic current snapshot required on stale-token errors, explicit unsupported non-JSON path semantics, and generated 16 MiB full-request-body metadata for management write endpoints.


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
