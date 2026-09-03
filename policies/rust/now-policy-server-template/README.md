Devolutions NOW policy server template
======================================

`now-policy-server-template` is the reusable server facade, HTTP route binding, and OpenAPI generator for the Devolutions NOW package broker API.

Purpose
-------

This crate bridges the implementation-agnostic `now-policy-api` model and concrete package
 broker implementations. It does not perform package operations itself. Instead, it defines the
 server trait and reusable router that a real broker can plug into.

The crate is used to:

- expose the `PackageBrokerServer` trait implemented by runtime brokers;
- bind that trait to the canonical HTTP endpoints;
- keep runtime routing and OpenAPI generation based on the same route definitions;

Architecture
------------

The crate has two main components:

- `server.rs` defines `PackageBrokerServer`, router builders, endpoint handlers, error-to-HTTP status mapping, and OpenAPI generation.
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
    async fn active_policy(&self) -> Result<PolicyResponse, ErrorResponse>;
    async fn policy_management(&self) -> Result<PolicyManagementResponse, ErrorResponse>;
    async fn validate_policy(&self, request: PolicyValidationRequest) -> Result<PolicyValidationResponse, ErrorResponse>;
    async fn replace_policy(&self, request: PolicyReplacementRequest) -> Result<PolicyReplacementResponse, ErrorResponse>;
    async fn evaluate(&self, request: PackageRequest) -> Result<EvaluationResponse, ErrorResponse>;
    async fn execute(&self, request: PackageRequest) -> Result<ExecutionResponse, ErrorResponse>;
    async fn status(&self, request: StatusRequest) -> Result<StatusResponse, ErrorResponse>;
}
```

Implementations return the active policy from `active_policy`. A broker with no active policy may return a structured `NotFound` error.

Then they pass the implementation to `api_router` or `api_router_from_shared`. The template owns the HTTP paths:

- `GET /v1/health`
- `GET /v1/capabilities`
- `GET /v1/policy`
- `GET /v1/policy/management`
- `POST /v1/policy/validate`
- `PUT /v1/policy`
- `POST /v1/package-operations/evaluate`
- `POST /v1/package-operations/execute`
- `POST /v1/package-operations/get-status`

This keeps route dispatch, error responses, and OpenAPI operation metadata in one place.

`POST /v1/policy/validate` and `PUT /v1/policy` accept complete HTTP request bodies up to
`MAX_POLICY_MANAGEMENT_BODY_BYTES` (16 MiB / 16,777,216 bytes), including their JSON envelopes.
This is an operational transport limit for realistic policies within the 1,024-rule editor model;
it does not attempt to accommodate the policy schema's pathological theoretical maximum. Package
operation endpoints continue to use the separate `MAX_REQUEST_BODY_BYTES` limit (256 KiB), which
implementations advertise through `CapabilitiesResponse.MaxRequestBodyBytes`.

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

The generated route and components include the policy response and policy document schema from `now-policy`.

Validation
----------

Run the OpenAPI generator after model or route changes, then run the .NET policy client tests to verify the generated schema still matches the C# DTO/client layer.
