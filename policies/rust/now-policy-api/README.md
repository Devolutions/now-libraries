Devolutions NOW policy API model
================================

`now-policy-api` is the implementation-agnostic Rust model crate for the Devolutions NOW package broker API. It defines the wire contract shared by Rust and C# client libraries/clients and is used as a origin for OpenAPI document generation.

Purpose
-------

This crate exists so the package broker protocol has one strongly typed source of truth that is independent from any concrete broker implementation. Runtime package managers and elevation logic are intentionally out of scope here.

The model crate is used to:

- describe package broker request and response payloads;
- generate the published OpenAPI document under `openapi/now-policy-api.yaml`;
- validate Rust/C# API via sample documents provided at `tests/samples`;
- keep Rust and .NET package broker contracts synchronized (shared OpenAPI specification and
  sample files).

Architecture
------------
Library structure overview:

- `api.rs` contains shared API DTOs used by multiple endpoints, including `PackageRequest`, client/server context, request summaries, decision details, diagnostics, and error responses.
- `execute.rs` contains execution response models for `POST /v1/package-operations/execute`.
- `evaluate.rs` contains evaluation response models for `POST /v1/package-operations/evaluate`.
- `status.rs` contains status request/response models for `POST /v1/package-operations/get-status`.
- `health.rs` contains health endpoint models for `GET /v1/health`.
- `capabilities.rs` contains capability endpoint models for `GET /v1/capabilities`.
- `enums.rs` contains shared protocol enums.
- `lib.rs` contains constrained string newtypes, validation helpers, etc.
- `policy_compat.rs` is enabled by the `policy-compat` feature and maps selected API model types to the `now-policy` crate's package policy types.

Top-level requests carry `RequestKind` and `RequestVersion`; top-level responses carry `ResponseKind` and `ResponseVersion`. Kind fields are marker types that serialize to fixed strings and reject mismatched values during deserialization; this is
required for further protocol evolution and allows the client to switch transport from HTTP to other
mechanisms without changing the wire schema.

OpenAPI ownership
-----------------

The generated OpenAPI artifact is published from this crate:

```text
openapi/now-policy-api.yaml
```

The route-aware generator lives in `now-policy-server-template`, because OpenAPI needs both the model types and the HTTP route binding. The output is written back here so the model crate owns the published schema artifact.

Regenerate it with:

```powershell
cargo run -p now-policy-server-template --bin generate-now-policy-api-openapi --locked
```

Validation
----------

The .NET policy client tests consume the generated OpenAPI schema and the shared sample documents to validate the C# API/client DTOs against this Rust contract.
