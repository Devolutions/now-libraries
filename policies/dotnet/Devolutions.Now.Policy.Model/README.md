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
- `Enums.cs` defines policy-level enums such as operation, manager, scope, architecture, elevation, decision, and rule precedence.
- `PolicySerializer.cs` defines shared `JsonSerializerOptions`, including strict parsing that rejects unknown JSON members and JSON null for non-nullable policy members or collection elements.

`PolicyDocument.Create` constructs a committed policy and `PolicyDraftDocument.Create` constructs an editable draft. `PolicyDocument.ToDraft` removes server-managed `Revision` and `PublishedAt`; `PolicyDraftDocument.ToPolicyDocument` requires those values when committing. `ParseJson` is the only policy parsing entry point.

Breaking change
---------------

Policy documents are JSON-only. `PolicyDocument.ParseYaml`, which was public in `Devolutions.Now.Policy.Model` 2026.8.13, has been removed intentionally. Consumers must migrate stored policies to JSON before upgrading; OpenAPI YAML and unrelated YAML documents are unaffected.

Validation
----------

Run the Rust policy schema/tests as well when changing shared policy semantics.
