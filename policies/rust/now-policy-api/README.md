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
- `event_channel.rs` contains the per-operation event channel descriptor returned in execution responses and the `NOW_BROKER` binary frame protocol codec (see `policies/docs/event-channel-protocol.md`).
- `health.rs` contains health endpoint models for `GET /v1/health`.
- `capabilities.rs` contains capability endpoint models for `GET /v1/capabilities`.
- `policy.rs` contains the active `PolicyDocument` response for the canonical `GET /v1/policy` endpoint.
- `management.rs` contains atomic management snapshots, raw-draft validation, versioned findings and receipts, optimistic replacement intents, and management responses.
- `enums.rs` contains shared protocol enums.
- `lib.rs` contains constrained string newtypes, validation helpers, etc.

`now-policy` owns the canonical committed and draft policy documents and schema. This API crate composes that domain contract into inspection and management responses; runtime-specific validation, persistence, and policy-evaluation logic belongs to the consuming broker implementation.

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

The generated document contains the unchanged policy inspection route, the management/validation/replacement routes, and canonical committed and draft policy schemas.

`StalePolicyStoreToken` errors include the atomic current `Management` snapshot so a client can explicitly confirm an overwrite against that exact newly observed token. `UnsafePolicyPath` is a 409 state/write-capability conflict, not an authentication failure. An Agent that does not expose a newer route may still return an ordinary unstructured 404; `UnsupportedEndpoint` is only an optional explicit implementation response.

Opaque store tokens and validation receipts use safe printable ASCII (`A-Z`, `a-z`, `0-9`, `.`, `_`, `~`, `:`, `-`) and begin with an ASCII alphanumeric character. This keeps length and validation behavior identical across Rust UTF-8 and .NET UTF-16 implementations.

Validation
----------

The .NET policy client tests consume the generated OpenAPI schema and the shared sample documents to validate the C# API/client DTOs against this Rust contract.
