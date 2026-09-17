# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [[0.5.0](https://github.com/Devolutions/now-libraries/compare/now-policy-v0.4.0...now-policy-v0.5.0)] - 2026-09-17

### <!-- 1 -->Features

- [**breaking**] Harden and finalize the policy contract ([#109](https://github.com/Devolutions/now-libraries/issues/109)) ([3ab49ad765](https://github.com/Devolutions/now-libraries/commit/3ab49ad76590c325293f50ac4129230ec2b2191a)) 

  ## Summary
  
  - reject duplicate JSON property names before typed .NET policy/broker
  deserialization, including nested and Unicode-escape-equivalent names;
  all policy input paths also reject unknown members
  - expose one strict-by-definition .NET policy serializer surface:
  `PolicySerializer.Options`, `Deserialize<T>`, and the document
  `ParseJson` helpers; remove redundant strict/non-strict model contexts
  and entry points
  - replace the eight boolean match arrays with optional scalar booleans
  (`null`/omitted = unrestricted, canonical output omitted)
  - canonicalize empty collection filters to omitted output; reject
  duplicate collection values consistently across schemas/Rust/.NET;
  preserve effective-nonempty persisted rules
  - enforce operational validity windows: when both metadata bounds are
  present, `ValidFrom` must be strictly earlier than `ValidUntil` by
  normalized instant
  - make `Constraints` valid only on Allow rules
  - remove serialized `PolicyType`, `RulePrecedence`, and `PackageNames`;
  precedence is fixed as lower priority first, Deny wins Allow/Deny ties,
  then document order
  - rename `Elevation` to `ExecutionElevation` and `Sources` to exact
  `SourceNames`; runtime collection bounds match the published schemas,
  and nonempty source names require exactly one manager
  - make package identifiers explicit `Exact` or `Patterns` modes and
  versions explicit `Exact` or semantic `Range` modes; provide atomic .NET
  mode-switch APIs
  - regenerate policy/draft schemas and OpenAPI 3.1 schemas with JSON
  Schema null unions (no legacy `nullable`)
  
  `PolicyFormatVersion` remains in the compatible 1.x line. This is an
  intentional pre-release package/API break with no legacy aliases or
  conversion.
  
  ## Canonical semantics
  
  - boolean condition omitted or `null`: does not narrow; `false`/`true`:
  exact request characteristic
  - collection omitted or `[]`: does not narrow; canonical output omits
  empty collections; duplicate values are invalid
  - validity bound omitted or `null`: that side is unbounded; canonical
  output omits it; equal or inverted two-sided windows are invalid
  - brokers reject operations before `ValidFrom` or after `ValidUntil` on
  every request, with no fallback policy
  - `PackageIdentifiers.Exact`: validated stable identifiers; wildcard
  characters rejected
  - `PackageIdentifiers.Patterns`: explicit wildcard patterns that may
  authorize multiple identifiers
  - `Version.Exact`: one or more arbitrary real package version strings;
  `Version.Range`: semantic versions only
  - `SourceNames`: exact configured source names, no URLs/pattern
  matching, exactly one selected manager, at most 128 names
  - `Managers`: at most 16 distinct manager values
  - `ExecutionElevation`: `Elevated` when scope is Machine or requested
  elevation is Elevated; `Standard` otherwise
  
  ## Consumer migration
  
  **Gateway**
  - remove construction/references for `PolicyType`, `RulePrecedence`,
  `PackageNames`, `Sources`, `Elevation`, `Versions`, and `VersionRange`
  - replace removed model serializer APIs with
  `PolicySerializer.Deserialize<T>` or the document `ParseJson` helpers
  - preserve fixed evaluator precedence and remove the old PackageNames
  fail-closed branch/tests
  - assume constraints are absent on Deny rules
  - compute `ExecutionElevation` exactly from Machine scope or requested
  elevation
  - implement `SourceNames`, package identifier modes, and version modes
  - retain per-request UTC validity enforcement with no fallback and add
  exact `ValidFrom`/`ValidUntil` boundary tests if missing
  
  **UniGetUI**
  - remove old field display/edit/help; show constraints only for Allow
  rules
  - replace removed model serializer APIs with
  `PolicySerializer.Deserialize<T>` or the document `ParseJson` helpers
  - keep friendly-name search only as a UI lookup that resolves stable
  identifiers
  - auto-generate unique priorities by visible order
  - use date/time controls with inline `ValidFrom < ValidUntil` validation
  - incomplete blank rules may exist transiently in the editor but must
  never be serialized/saved
  
  ## Validation
  
  - .NET 9: Model **174/174**, Client **262/262**
  - .NET 10: Model **174/174**, Client **262/262**
  - Rust: `now-policy` **36/36**, `now-policy-api` **18/18**, server
  **7/7**, server samples **28/28**
  - clippy `-D warnings`, dotnet/cargo formatting, deterministic
  schema/OpenAPI regeneration
  - Cargo package verification for model/API/server and NuGet pack for
  Model/API/Client
  - reflection-disabled NativeAOT smoke covering duplicate rejection,
  validity windows, and canonical contract paths
  - GPT-6 Astra full review and follow-ups; all material findings
  addressed, final confirmation clean
  
  ## Release impact
  
  Required coordinated Rust releases: `now-policy` **0.5.0**,
  `now-policy-api` **0.6.0** (updating its `now-policy` dependency), and
  `now-policy-server-template` **0.6.0**. Publish the Model/API/Client
  NuGets together using the next date-based workflow version (validated
  with **2026.09.17.0**, normalized to **2026.9.17**). No release, package
  publication, merge, or auto-merge is included.
  
  ---------



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
