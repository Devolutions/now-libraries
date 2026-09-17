Devolutions NOW policy model
============================

This crate provides the JSON-only Rust policy model and JSON Schema helpers for Devolutions Agent NOW policy documents.

It contains committed `PolicyDocument` and editable `PolicyDraftDocument` types, explicit conversions that add or remove server-managed metadata, and schema generation utilities.
Broker request, response, server, transport, and execution types are intentionally out of scope.

`PolicyFormatVersion` is a software-managed document-format marker, not a publisher release version. Applications stamp the current `1.0.0` value when creating documents, accept and preserve supported numeric versions in the 1.x line, reject malformed or unsupported-major values, and must not expose it as publisher-authored editable metadata. Policy documents do not contain a `$schema` member; documents that contain it are rejected as unknown-field input under the strict contract.

`Metadata.ValidFrom` and `Metadata.ValidUntil` are optional operational UTC instants, not informational labels. Omitted or explicit `null` leaves that side unbounded, and canonical output omits an absent bound. When both are present, `ValidFrom` must be strictly earlier than `ValidUntil`; comparisons normalize offsets by instant. Brokers fail closed for every request: reject operations when the current instant is before `ValidFrom` or after `ValidUntil`, with no fallback policy.

`PolicyMatch` boolean request characteristics (`Interactive`, `SkipHashCheck`, `PreRelease`, `HasCustomParameters`, `HasCustomInstallLocation`, `HasPrePostCommands`, `HasKillBeforeOperation`, and `HasUninstallPrevious`) are optional scalar booleans. Omitted or explicit `null` means the characteristic does not affect whether the rule matches; `false` and `true` require that exact request characteristic. Canonical serialization omits absent values. Legacy boolean arrays are rejected. A rule's `Match` must still contain at least one effective non-null, nonempty criterion.

Collection-valued match filters accept omission or an explicit empty array as unrestricted input, but canonical serialization omits empty collections. The former `PackageNames` filter is rejected as unknown input. `PackageIdentifiers` has exactly one explicit mode: `Exact` contains validated stable identifiers and rejects wildcard characters, while `Patterns` contains deliberately broad wildcard patterns that may authorize multiple identifiers. `SourceNames` contains exact configured package source names, not URLs or wildcard patterns. Source-name comparison follows the selected package manager's semantics, wildcard characters are literal, and nonempty `SourceNames` requires exactly one `Managers` value.

`Version` also has exactly one explicit mode: `Exact` contains one or more arbitrary package version strings, including non-SemVer versions, while `Range` contains the semantic-version range. Omitted or explicit `null` does not narrow matching. The former `Versions` and `VersionRange` match properties are rejected as unknown input.

`ExecutionElevation` matches the request's effective execution privilege, not only the client's requested elevation. Brokers must use `Elevated` when the package operation will run with administrator privileges—currently when `Scope` is `Machine` or `Client.RequestedElevation` is `Elevated`—and `Standard` otherwise. The former `Elevation` match property is rejected as unknown input.

Rule precedence is fixed by the policy format and is not represented by a JSON field: lower `Priority` values evaluate first, `Deny` wins equal-priority Allow/Deny ties, and remaining equal-priority ties retain document order. `Constraints` are additional safety limits for an `Allow` rule and are invalid on `Deny` rules. Editors may host incomplete blank rules transiently, but must not serialize or save a rule until `Match` contains an effective condition.

`parse_policy_yaml` was intentionally removed as a breaking change. OpenAPI YAML generation and unrelated YAML inputs are unaffected. Boolean match arrays and the former `PolicyType`, `RulePrecedence`, and `PackageNames` members are rejected rather than converted.

Gateway consumers must preserve the fixed priority/deny-tie/document-order evaluator semantics, omit constraints on Deny rules, compute `ExecutionElevation` from effective execution privilege, adopt the explicit package/version/source modes, retain per-request UTC validity enforcement, and add exact `ValidFrom`/`ValidUntil` boundary tests if missing. UniGetUI uses date/time controls with inline `ValidFrom < ValidUntil` validation. Editors may host incomplete rules transiently but must not serialize or save them; friendly-name search must resolve to stable identifiers rather than emit a `PackageNames` condition.
