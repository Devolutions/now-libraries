Devolutions NOW policy server template
======================================

`now-policy-server-template` is the reusable server facade, HTTP route binding, mock implementation, OpenAPI generator, and sample fixture owner for the Devolutions NOW package broker API.

Purpose
-------

This crate bridges the implementation-agnostic `now-policy-api` model and concrete package
 broker implementations. It does not perform package operations itself. Instead, it defines the
 server trait and reusable router that a real broker can plug into.

The crate is used to:

- expose the `PackageBrokerServer` trait implemented by runtime brokers;
- bind that trait to the canonical HTTP endpoints;
- keep runtime routing and OpenAPI generation based on the same route definitions;
- provide a deterministic mock server for tests and client development;
- store sample request/response documents used by Rust and .NET validation.

Architecture
------------

The crate has three main surfaces:

- `server.rs` defines `PackageBrokerServer`, router builders, endpoint handlers, error-to-HTTP status mapping, and OpenAPI generation.
- `mock.rs` provides `MockPackageBrokerServer`, a deterministic in-memory implementation backed by registered sample responses and default health/capability data.
- `tools/generate_openapi.rs` generates the OpenAPI YAML file into the sibling `now-policy-api/openapi/` directory.

The crate re-exports `now-policy-api`, so tests and consumers that need the server template can import both server utilities and API DTOs from `now_policy_server_template`.

HTTP facade
-----------

Runtime implementations implement:

```rust
#[async_trait::async_trait]
pub trait PackageBrokerServer: Send + Sync {
    async fn health(&self) -> HealthResponse;
    async fn capabilities(&self) -> CapabilitiesResponse;
    async fn evaluate(&self, request: PackageRequest) -> Result<EvaluationResponse, ErrorResponse>;
    async fn execute(&self, request: PackageRequest) -> Result<ExecutionResponse, ErrorResponse>;
    async fn status(&self, request: StatusRequest) -> Result<StatusResponse, ErrorResponse>;
}
```

Then they pass the implementation to `api_router` or `api_router_from_shared`. The template owns the HTTP paths:

- `GET /v1/health`
- `GET /v1/capabilities`
- `POST /v1/package-operations/evaluate`
- `POST /v1/package-operations/execute`
- `POST /v1/package-operations/get-status`

This keeps route dispatch, error responses, and OpenAPI operation metadata in one place.

Mock and fixtures
-----------------

`MockPackageBrokerServer` is intended for protocol tests, sample validation, and client development. It returns deterministic health/capabilities responses and can be configured with evaluation, execution, and status responses loaded from fixture files.

Sample documents live under:

```text
assets/samples/
```

They are treated as protocol fixtures rather than implementation fixtures. Rust tests deserialize them through the API model, exercise the mock/router path, and .NET tests validate equivalent DTOs against the generated OpenAPI schema.

OpenAPI generation
------------------

OpenAPI generation lives here because it requires the HTTP route binding from `server.rs`. The published schema file is written to `now-policy-api`:

```text
../now-policy-api/openapi/now-policy-api.yaml
```

Regenerate it with:

```powershell
cargo run -p now-policy-server-template --bin generate-now-policy-api-openapi --locked
```

With the `policy-compat` feature enabled, the generated components also include the policy document schema from `now-policy`.

Validation
----------

Run the OpenAPI generator after model or route changes, then run the .NET policy client tests to verify the generated schema still matches the C# DTO/client layer.
