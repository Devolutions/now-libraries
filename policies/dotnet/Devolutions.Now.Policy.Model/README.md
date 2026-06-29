Devolutions NOW policy model for .NET
=====================================

`Devolutions.Now.Policy.Model` contains the .NET policy document model for Devolutions NOW package broker policies. It is the .NET counterpart to the Rust `now-policy` crate and represents the policy files used to decide whether package operations are allowed or denied.

Purpose
-------

This package is focused on policy documents, not broker transport. It is used by .NET code that needs to create, parse, inspect, or serialize Devolutions NOW policy files.

The model is used to:

- represent package broker policy documents in C#;
- parse strict JSON policy documents;
- parse YAML policy documents by converting them to the same JSON model;
- serialize policy documents with the canonical JSON shape;
- share policy enums and document types with the package broker API compatibility layer.

Architecture
------------

- `PolicyModels.cs` defines `PolicyDocument`, metadata, enforcement, rules, match criteria, constraints, and version range types.
- `Enums.cs` defines policy-level enums such as operation, manager, scope, architecture, elevation, decision, and rule precedence.
- `PolicyJson.cs` defines shared `JsonSerializerOptions`, including strict parsing that rejects unknown JSON members.

`PolicyDocument.Create` provides a simple helper for constructing a new policy document with metadata and default enforcement. `PolicyDocument.ParseJson` and `PolicyDocument.ParseYaml` are the main entry points for reading policy documents.

Validation
----------

Run the Rust policy schema/tests as well when changing shared policy semantics.
