Devolutions NOW policy model
============================

This crate provides the JSON-only Rust policy model and JSON Schema helpers for Devolutions Agent NOW policy documents.

It contains committed `PolicyDocument` and editable `PolicyDraftDocument` types, explicit conversions that add or remove server-managed metadata, and schema generation utilities.
Broker request, response, server, transport, and execution types are intentionally out of scope.

`PolicyFormatVersion` is a software-managed document-format marker, not a publisher release version. Applications stamp the current `1.0.0` value when creating documents, accept and preserve compatible SemVer values in the 1.x line, reject malformed or unsupported-major values, and must not expose it as publisher-authored editable metadata. Policy documents do not contain a `$schema` member; documents that contain it are rejected as unknown-field input under the strict contract.

`parse_policy_yaml` was intentionally removed as a breaking change. OpenAPI YAML generation and unrelated YAML inputs are unaffected.
