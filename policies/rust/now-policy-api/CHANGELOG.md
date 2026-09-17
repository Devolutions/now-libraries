# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

## [[0.6.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.5.0...now-policy-api-v0.6.0)] - 2026-09-17

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



## [[0.5.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.4.0...now-policy-api-v0.5.0)] - 2026-09-15

### <!-- 1 -->Features

- [**breaking**] Replace `ResponsePolicyInfo::policy_version` with `policy_format_version: now_policy::PolicyFormatVersion`, serialized as `PolicyFormatVersion` ([#106](https://github.com/Devolutions/now-libraries/issues/106)) ([cc841bf6e5](https://github.com/Devolutions/now-libraries/commit/cc841bf6e59547ddefbb6287f7a0ef8466aedb10))
- [**breaking**] Update embedded `PolicyDocument` and `PolicyDraftDocument` payloads to omit `$schema` and use `PolicyFormatVersion` instead of `PolicyVersion` ([#106](https://github.com/Devolutions/now-libraries/issues/106)) ([cc841bf6e5](https://github.com/Devolutions/now-libraries/commit/cc841bf6e59547ddefbb6287f7a0ef8466aedb10))
- [**breaking**] Replace `PolicyFindingCode::UnsupportedPolicyVersion` with `UnsupportedPolicyFormatVersion` and remove `UnsupportedSchema` ([#106](https://github.com/Devolutions/now-libraries/issues/106)) ([cc841bf6e5](https://github.com/Devolutions/now-libraries/commit/cc841bf6e59547ddefbb6287f7a0ef8466aedb10))

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
