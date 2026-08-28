Devolutions NOW policy model
============================

This crate provides the JSON-only Rust policy model and JSON Schema helpers for Devolutions Agent NOW policy documents.

It contains committed `PolicyDocument` and editable `PolicyDraftDocument` types, explicit conversions that add or remove server-managed metadata, and schema generation utilities.
Broker request, response, server, transport, and execution types are intentionally out of scope.

`parse_policy_yaml` was intentionally removed as a breaking change. OpenAPI YAML generation and unrelated YAML inputs are unaffected.
