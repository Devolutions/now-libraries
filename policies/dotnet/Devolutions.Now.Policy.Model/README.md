Devolutions NOW policy model for .NET
=====================================

`Devolutions.Now.Policy.Model` contains the .NET policy document model for Devolutions NOW package broker policies. It is the .NET counterpart to the Rust `now-policy` crate and represents the policy files used to decide whether package operations are allowed or denied.

Purpose
-------

This package is focused on policy documents, not broker transport. It is used by .NET code that needs to create, parse, inspect, or serialize Devolutions NOW policy files.

The model is used to:

- represent package broker policy documents in C#;
- parse strict JSON policy documents;
- represent editable drafts separately from committed policy documents;
- serialize policy documents with the canonical JSON shape;
- share policy enums and document types with the package broker API compatibility layer.

Architecture
------------

- `PolicyModels.cs` defines committed `PolicyDocument`, editable `PolicyDraftDocument`, their metadata, explicit conversions, enforcement, rules, match criteria, constraints, and version range types.
- `Enums.cs` defines policy-level enums such as operation, manager, scope, architecture, elevation, and decision.
- `PolicySerializer.cs` defines the single source-generated policy JSON contract. `PolicySerializer.Deserialize<T>`, `PolicySerializer.Options`, and both document `ParseJson` helpers all reject unknown members, duplicate property names, and JSON null for non-nullable policy members or collection elements.
- The duplicate check covers every nested object, decodes escaped names before comparing them, and uses ordinal, case-sensitive equality to match canonical property-name handling.

`PolicyDocument.Create` constructs a committed policy and `PolicyDraftDocument.Create` constructs an editable draft. `PolicyDocument.ToDraft` removes server-managed `Revision` and `PublishedAt`; `PolicyDraftDocument.ToPolicyDocument` requires those values when committing. `ParseJson` is the recommended strict policy parsing entry point.

`PolicyFormatVersion` is software-managed format compatibility metadata, not a publisher release version. New documents stamp `1.0.0`; readers accept and preserve supported numeric versions in the 1.x line and reject malformed or unsupported-major values. Applications must not expose it as authored metadata. Policy documents contain no `$schema` member, and strict readers reject documents that contain one as unknown-field input.

`Metadata.ValidFrom` and `Metadata.ValidUntil` are optional operational UTC instants, not informational labels. Omitted or explicit `null` leaves that side unbounded, and canonical output omits an absent bound. When both are present, `ValidFrom` must be strictly earlier than `ValidUntil`; comparisons normalize offsets by instant. Brokers fail closed for every request: reject operations when the current instant is before `ValidFrom` or after `ValidUntil`, with no fallback policy.

`PolicyMatch` boolean request characteristics (`Interactive`, `SkipHashCheck`, `PreRelease`, `HasCustomParameters`, `HasCustomInstallLocation`, `HasPrePostCommands`, `HasKillBeforeOperation`, and `HasUninstallPrevious`) are optional scalar booleans. Omitted or explicit `null` means the characteristic does not affect whether the rule matches; `false` and `true` require that exact request characteristic. Canonical serialization omits absent values. Legacy boolean arrays are rejected. A rule's `Match` must still contain at least one effective non-null, nonempty criterion.

Collection-valued match filters accept omission or an explicit empty array as unrestricted input, but canonical serialization omits empty collections. The former `PackageNames` filter is rejected as unknown input. `PackageIdentifiers` has exactly one explicit mode: `Exact` contains validated stable identifiers and rejects wildcard characters, while `Patterns` contains deliberately broad wildcard patterns that may authorize multiple identifiers. Use `UseExact` or `UsePatterns` to switch a mutable `PackageIdentifierCondition` atomically. `SourceNames` contains exact configured package source names, not URLs or wildcard patterns. Source-name comparison follows the selected package manager's semantics, wildcard characters are literal, and nonempty `SourceNames` requires exactly one `Managers` value.

`Version` also has exactly one explicit mode: `Exact` contains one or more arbitrary package version strings, including non-SemVer versions, while `Range` contains the semantic-version range. Use `UseExact` or `UseRange` to switch a mutable `VersionCondition` atomically. Omitted or explicit `null` does not narrow matching. The former `Versions` and `VersionRange` match properties are rejected as unknown input.

`ExecutionElevation` matches the request's effective execution privilege, not only the client's requested elevation. Brokers must use `Elevated` when the package operation will run with administrator privileges—currently when `Scope` is `Machine` or `Client.RequestedElevation` is `Elevated`—and `Standard` otherwise. The former `Elevation` match property is rejected as unknown input.

Rule precedence is fixed by the policy format and is not represented by a JSON field: lower `Priority` values evaluate first, `Deny` wins equal-priority Allow/Deny ties, and remaining equal-priority ties retain document order. `Constraints` are additional safety limits for an `Allow` rule and are invalid on `Deny` rules. Editors may host incomplete blank rules transiently, but must not serialize or save a rule until `Match` contains an effective condition.

Breaking change
---------------

Policy documents are JSON-only. `PolicyDocument.ParseYaml`, which was public in `Devolutions.Now.Policy.Model` 2026.8.13, has been removed intentionally. Consumers must migrate stored policies to JSON before upgrading; OpenAPI YAML and unrelated YAML documents are unaffected. Boolean match arrays and the former `PolicyType`, `RulePrecedence`, and `PackageNames` members are also rejected rather than converted.

Consumer migration
------------------

- Gateway removes `PolicyType`, `RulePrecedence`, `PackageNames`, and old match-field construction; keeps the fixed priority/deny-tie/document-order evaluator semantics; assumes Deny rules have no constraints; computes `ExecutionElevation` as `Elevated` when scope is `Machine` or requested elevation is `Elevated`, otherwise `Standard`; implements the explicit package/version/source modes; retains per-request UTC validity enforcement; and adds exact `ValidFrom`/`ValidUntil` boundary tests if missing.
- UniGetUI removes editors/help for `PolicyType`, `RulePrecedence`, `PackageNames`, and old match fields; shows constraints only for Allow rules; resolves friendly-name searches to stable identifiers; auto-generates unique priorities by visible order; uses date/time controls with inline `ValidFrom < ValidUntil` validation; and may host incomplete rules transiently but must not serialize or save them.
- Stored documents replace boolean arrays with scalar booleans, rename `Sources` to `SourceNames` and `Elevation` to `ExecutionElevation`, wrap package identifiers in `Exact` or `Patterns`, and replace `Versions`/`VersionRange` with one `Version` mode.

Validation
----------

Run the Rust policy schema/tests as well when changing shared policy semantics.
