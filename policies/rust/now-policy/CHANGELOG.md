# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [[0.3.0](https://github.com/Devolutions/now-libraries/compare/now-policy-v0.2.0...now-policy-v0.3.0)] - 2026-09-03

### <!-- 1 -->Features

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



### Fixed

- Count Unicode scalar values for `StringPattern`, `VersionString`, and `CustomParameterString` length bounds, matching JSON Schema and .NET policy validation semantics.

### Changed

- [**breaking**] Make policy documents JSON-only and remove `parse_policy_yaml`.
- Add editable `PolicyDraftDocument` and explicit named committed/draft conversion methods.
- Reject boolean match arrays containing more than one value.


## [[0.2.0](https://github.com/Devolutions/now-libraries/compare/now-policy-v0.1.0...now-policy-v0.2.0)] - 2026-07-27

### <!-- 1 -->Features

- [**breaking**] Add 14 new package manager variants to `ManagerName` (Apt, Bun, Cargo, Chocolatey, Dnf, Dotnet, Flatpak, Homebrew, Npm, Pacman, Pip, Scoop, Snap, Vcpkg) ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))

## [[0.1.0](https://github.com/Devolutions/now-libraries/releases)] - 2026-06-23

### <!-- 1 -->Features

- Initial release of the Devolutions NOW policy model crate
