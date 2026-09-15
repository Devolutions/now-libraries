# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [[0.4.0](https://github.com/Devolutions/now-libraries/compare/now-policy-v0.3.0...now-policy-v0.4.0)] - 2026-09-15

### <!-- 1 -->Features

- Add `PolicyFormatVersion` and `CURRENT_POLICY_FORMAT_VERSION` for software-managed document-format compatibility; new documents use `1.0.0`, while readers accept canonical three-component numeric 1.x versions whose components fit in `u64` ([#106](https://github.com/Devolutions/now-libraries/issues/106)) ([cc841bf6e5](https://github.com/Devolutions/now-libraries/commit/cc841bf6e59547ddefbb6287f7a0ef8466aedb10))
- [**breaking**] Replace `PolicyDocument::policy_version` and `PolicyDraftDocument::policy_version` with `policy_format_version: PolicyFormatVersion`, serialized as `PolicyFormatVersion` ([#106](https://github.com/Devolutions/now-libraries/issues/106)) ([cc841bf6e5](https://github.com/Devolutions/now-libraries/commit/cc841bf6e59547ddefbb6287f7a0ef8466aedb10))
- [**breaking**] Remove the `_schema` field and serialized `$schema` member from `PolicyDocument` and `PolicyDraftDocument`; strict deserialization now rejects documents that still contain `$schema` ([#106](https://github.com/Devolutions/now-libraries/issues/106)) ([cc841bf6e5](https://github.com/Devolutions/now-libraries/commit/cc841bf6e59547ddefbb6287f7a0ef8466aedb10))
- [**breaking**] Remove `PolicySchemaUri`, `PolicyDraftSchemaUri`, `POLICY_SCHEMA_URI`, and `POLICY_DRAFT_SCHEMA_URI` ([#106](https://github.com/Devolutions/now-libraries/issues/106)) ([cc841bf6e5](https://github.com/Devolutions/now-libraries/commit/cc841bf6e59547ddefbb6287f7a0ef8466aedb10))

## [[0.3.0](https://github.com/Devolutions/now-libraries/compare/now-policy-v0.2.0...now-policy-v0.3.0)] - 2026-09-03

### <!-- 1 -->Features

- [**breaking**] Make policy documents JSON-only by removing `parse_policy_yaml` ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add `PolicyDraftDocument` for authored policy content without server-managed metadata ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add the versioned `now-policy-draft` JSON Schema ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add `PolicyDocument::to_draft()` for creating editable policy drafts ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Add `PolicyDraftDocument::into_policy_document()` for committing drafts with server-managed metadata ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- [**breaking**] Reject boolean match arrays containing multiple values ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))

### Fixed

- Count Unicode scalar values for `StringPattern` length bounds ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Count Unicode scalar values for `VersionString` length bounds ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))
- Count Unicode scalar values for `CustomParameterString` length bounds ([#99](https://github.com/Devolutions/now-libraries/issues/99)) ([cd7f3ba741](https://github.com/Devolutions/now-libraries/commit/cd7f3ba7416358f9cc137dcd1774511e9aab0e9b))

## [[0.2.0](https://github.com/Devolutions/now-libraries/compare/now-policy-v0.1.0...now-policy-v0.2.0)] - 2026-07-27

### <!-- 1 -->Features

- [**breaking**] Add 14 new package manager variants to `ManagerName` (Apt, Bun, Cargo, Chocolatey, Dnf, Dotnet, Flatpak, Homebrew, Npm, Pacman, Pip, Scoop, Snap, Vcpkg) ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))

## [[0.1.0](https://github.com/Devolutions/now-libraries/releases)] - 2026-06-23

### <!-- 1 -->Features

- Initial release of the Devolutions NOW policy model crate
