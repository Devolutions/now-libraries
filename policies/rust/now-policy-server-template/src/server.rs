//! Implementation-agnostic package broker server facade, HTTP router, and OpenAPI generator.

use std::sync::Arc;

use aide::axum::ApiRouter;
use aide::axum::routing::{get_with, post_with};
use aide::openapi::OpenApi;
use aide::transform::TransformOperation;
use async_trait::async_trait;
use axum::Json;
use axum::extract::State;
use axum::http::StatusCode;
use axum::response::{IntoResponse, Response};
use serde::Serialize;

use now_policy_api::{
    API_VERSION_STR, CancelRequest, CancelResponse, CapabilitiesResponse, ErrorCode, ErrorResponse, EvaluationResponse,
    ExecutionResponse, HealthResponse, PackageRequest, StatusRequest, StatusResponse,
};
#[cfg(feature = "policy-compat")]
use now_policy_api::{ErrorResponseKind, PolicyResponse};
use schemars::SchemaGenerator;

pub const MAX_REQUEST_BODY_BYTES: usize = 256 * 1024;

/// Implementation-neutral contract exposed by a package broker server.
#[async_trait]
pub trait PackageBrokerServer: Send + Sync {
    async fn health(&self) -> HealthResponse;
    async fn capabilities(&self) -> CapabilitiesResponse;
    #[cfg(feature = "policy-compat")]
    async fn policy(&self) -> Result<PolicyResponse, ErrorResponse> {
        Err(ErrorResponse {
            response_kind: ErrorResponseKind,
            response_version: API_VERSION_STR.into(),
            server: self.capabilities().await.server,
            code: ErrorCode::NotFound,
            message: "active policy inspection is not supported".to_owned(),
            details: Vec::new(),
        })
    }
    async fn evaluate(&self, request: PackageRequest) -> Result<EvaluationResponse, ErrorResponse>;
    async fn execute(&self, request: PackageRequest) -> Result<ExecutionResponse, ErrorResponse>;
    async fn status(&self, request: StatusRequest) -> Result<StatusResponse, ErrorResponse>;
    async fn cancel(&self, request: CancelRequest) -> Result<CancelResponse, ErrorResponse>;
}

/// Shared package broker server object used by the reusable HTTP router.
pub type SharedPackageBrokerServer = Arc<dyn PackageBrokerServer>;

/// Build the broker API router backed by a concrete [`PackageBrokerServer`].
///
/// Runtime implementations only need to implement [`PackageBrokerServer`]; this crate owns the
/// route binding and uses the same route definitions for OpenAPI generation.
pub fn api_router(server: impl PackageBrokerServer + 'static) -> ApiRouter<()> {
    api_routes().with_state::<()>(Arc::new(server))
}

/// Build the broker API router from an already shared server object.
pub fn api_router_from_shared(server: SharedPackageBrokerServer) -> ApiRouter<()> {
    api_routes().with_state::<()>(server)
}

fn api_routes() -> ApiRouter<SharedPackageBrokerServer> {
    let router = ApiRouter::new()
        .api_route("/v1/health", get_with(health_handler, health_docs))
        .api_route("/v1/capabilities", get_with(capabilities_handler, capabilities_docs));

    #[cfg(feature = "policy-compat")]
    let router = router.api_route("/v1/policy", get_with(policy_handler, policy_docs));

    router
        .api_route(
            "/v1/package-operations/evaluate",
            post_with(evaluate_handler, evaluate_docs)
                .layer(axum::extract::DefaultBodyLimit::max(MAX_REQUEST_BODY_BYTES)),
        )
        .api_route(
            "/v1/package-operations/execute",
            post_with(execute_handler, execute_docs)
                .layer(axum::extract::DefaultBodyLimit::max(MAX_REQUEST_BODY_BYTES)),
        )
        .api_route(
            "/v1/package-operations/get-status",
            post_with(status_handler, status_docs).layer(axum::extract::DefaultBodyLimit::max(MAX_REQUEST_BODY_BYTES)),
        )
        .api_route(
            "/v1/package-operations/cancel",
            post_with(cancel_handler, cancel_docs).layer(axum::extract::DefaultBodyLimit::max(MAX_REQUEST_BODY_BYTES)),
        )
}

/// Build the OpenAPI 3 document for the package broker API from the Rust types.
pub fn openapi() -> OpenApi {
    use aide::openapi::Info;

    let mut api = OpenApi {
        info: Info {
            title: "Devolutions NOW Package Broker API".to_owned(),
            version: API_VERSION_STR.to_owned(),
            description: Some(
                "HTTP API exposed by a Devolutions NOW package broker facade over a Windows named pipe.".to_owned(),
            ),
            ..Info::default()
        },
        ..OpenApi::default()
    };

    aide::generate::in_context(|ctx| {
        ctx.schema = openapi_schema_generator();
    });

    let _ = api_routes().finish_api(&mut api);
    #[cfg(feature = "policy-compat")]
    register_policy_schema(&mut api);
    api
}

fn openapi_schema_generator() -> SchemaGenerator {
    use schemars::r#gen::SchemaSettings;

    SchemaSettings::openapi3().into()
}

#[cfg(feature = "policy-compat")]
fn register_policy_schema(api: &mut OpenApi) {
    use std::collections::BTreeMap;

    use aide::openapi::{Components, SchemaObject};
    use now_policy::PolicyDocument;
    use schemars::schema::Schema;

    let root = openapi_schema_generator().into_root_schema_for::<PolicyDocument>();
    let renames: BTreeMap<_, _> = root
        .definitions
        .keys()
        .map(|name| (name.clone(), format!("PolicyModel{name}")))
        .collect();
    let root_schema = rewrite_policy_schema_refs(Schema::Object(root.schema), &renames);

    let components = api.components.get_or_insert_with(Components::default);

    components
        .schemas
        .entry("PolicyDocument".to_owned())
        .or_insert_with(|| SchemaObject {
            json_schema: root_schema,
            external_docs: None,
            example: None,
        });

    for (name, schema) in root.definitions {
        let component_name = renames
            .get(&name)
            .expect("BUG: every policy schema definition should have a namespaced component");
        components
            .schemas
            .entry(component_name.clone())
            .or_insert_with(|| SchemaObject {
                json_schema: rewrite_policy_schema_refs(schema, &renames),
                external_docs: None,
                example: None,
            });
    }
}

#[cfg(feature = "policy-compat")]
fn rewrite_policy_schema_refs(
    schema: schemars::schema::Schema,
    renames: &std::collections::BTreeMap<String, String>,
) -> schemars::schema::Schema {
    fn rewrite(value: &mut serde_json::Value, renames: &std::collections::BTreeMap<String, String>) {
        match value {
            serde_json::Value::String(reference) => {
                for prefix in ["#/components/schemas/", "#/definitions/"] {
                    if let Some(name) = reference.strip_prefix(prefix)
                        && let Some(replacement) = renames.get(name)
                    {
                        *reference = format!("#/components/schemas/{replacement}");
                        break;
                    }
                }
            }
            serde_json::Value::Array(values) => {
                for value in values {
                    rewrite(value, renames);
                }
            }
            serde_json::Value::Object(values) => {
                for value in values.values_mut() {
                    rewrite(value, renames);
                }
            }
            _ => {}
        }
    }

    let mut value = serde_json::to_value(schema).expect("BUG: policy schema should serialize");
    rewrite(&mut value, renames);
    serde_json::from_value(value).expect("BUG: rewritten policy schema should deserialize")
}

async fn health_handler(State(server): State<SharedPackageBrokerServer>) -> Json<HealthResponse> {
    Json(server.health().await)
}

async fn capabilities_handler(State(server): State<SharedPackageBrokerServer>) -> Json<CapabilitiesResponse> {
    Json(server.capabilities().await)
}

#[cfg(feature = "policy-compat")]
async fn policy_handler(State(server): State<SharedPackageBrokerServer>) -> Response {
    broker_result(server.policy().await)
}

async fn evaluate_handler(
    State(server): State<SharedPackageBrokerServer>,
    Json(request): Json<PackageRequest>,
) -> Response {
    broker_result(server.evaluate(request).await)
}

async fn execute_handler(
    State(server): State<SharedPackageBrokerServer>,
    Json(request): Json<PackageRequest>,
) -> Response {
    broker_result(server.execute(request).await)
}

async fn status_handler(
    State(server): State<SharedPackageBrokerServer>,
    Json(request): Json<StatusRequest>,
) -> Response {
    broker_result(server.status(request).await)
}

async fn cancel_handler(
    State(server): State<SharedPackageBrokerServer>,
    Json(request): Json<CancelRequest>,
) -> Response {
    broker_result(server.cancel(request).await)
}

fn broker_result<T: Serialize>(result: Result<T, ErrorResponse>) -> Response {
    match result {
        Ok(response) => (StatusCode::OK, Json(response)).into_response(),
        Err(error) => (error_status(error.code), Json(error)).into_response(),
    }
}

fn error_status(code: ErrorCode) -> StatusCode {
    match code {
        ErrorCode::BadRequest => StatusCode::BAD_REQUEST,
        ErrorCode::Unauthorized => StatusCode::UNAUTHORIZED,
        ErrorCode::Forbidden => StatusCode::FORBIDDEN,
        ErrorCode::NotFound => StatusCode::NOT_FOUND,
        ErrorCode::Conflict => StatusCode::CONFLICT,
        ErrorCode::PayloadTooLarge => StatusCode::PAYLOAD_TOO_LARGE,
        ErrorCode::UnsupportedMediaType => StatusCode::UNSUPPORTED_MEDIA_TYPE,
        ErrorCode::ValidationFailed => StatusCode::UNPROCESSABLE_ENTITY,
        ErrorCode::BrokerPaused => StatusCode::SERVICE_UNAVAILABLE,
        ErrorCode::InternalError => StatusCode::INTERNAL_SERVER_ERROR,
        ErrorCode::Timeout => StatusCode::GATEWAY_TIMEOUT,
    }
}

fn health_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Health check")
        .description("Returns broker readiness state.")
        .response::<200, Json<HealthResponse>>()
}

fn capabilities_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Get capabilities")
        .description("Returns transports, managers, and operations supported by the broker.")
        .response::<200, Json<CapabilitiesResponse>>()
}

#[cfg(feature = "policy-compat")]
fn policy_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Get active policy")
        .description(
            "Returns the active parsed policy document. A 404 response means policy inspection is unsupported.",
        )
        .response::<200, Json<PolicyResponse>>()
        .response::<404, Json<ErrorResponse>>()
        .default_response::<Json<ErrorResponse>>()
}

fn evaluate_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Evaluate package operation")
        .description("Evaluates a package operation against policy without requiring elevated execution.")
        .response::<200, Json<EvaluationResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<422, Json<ErrorResponse>>()
        .response::<503, Json<ErrorResponse>>()
}

fn execute_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Execute package operation")
        .description("Evaluates a package operation and submits it to the implementation for execution when allowed.")
        .response::<200, Json<ExecutionResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<409, Json<ErrorResponse>>()
        .response::<422, Json<ErrorResponse>>()
        .response::<503, Json<ErrorResponse>>()
}

fn status_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Query package operation status")
        .description("Returns the current status of a previously submitted package operation.")
        .response::<200, Json<StatusResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<404, Json<ErrorResponse>>()
}

fn cancel_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Cancel package operation")
        .description(
            "Requests cancelation of a previously submitted package operation. \
             Cancelation is asynchronous: poll the status endpoint until the operation reaches a terminal status.",
        )
        .response::<200, Json<CancelResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<404, Json<ErrorResponse>>()
}

#[cfg(all(test, feature = "policy-compat"))]
mod tests {
    use super::openapi;

    #[test]
    fn policy_schemas_do_not_rename_existing_api_components() {
        let api = openapi();
        let schemas = &api.components.expect("OpenAPI components should exist").schemas;

        for name in [
            "Architecture",
            "CustomParameterString",
            "Decision",
            "Elevation",
            "ManagerName",
            "Operation",
            "ResourceId",
            "Scope",
            "SemanticVersion",
            "VersionString",
        ] {
            assert!(
                schemas.contains_key(name),
                "existing API component {name} should remain"
            );
            assert!(
                schemas.contains_key(&format!("PolicyModel{name}")),
                "embedded policy component {name} should be namespaced"
            );
            assert!(
                !schemas.contains_key(&format!("{name}2")),
                "component collision must not rename {name}"
            );
        }
    }

    #[test]
    fn policy_openapi_documents_structured_errors_for_other_statuses() {
        let api = serde_json::to_value(openapi()).expect("OpenAPI should serialize");
        let responses = &api["paths"]["/v1/policy"]["get"]["responses"];

        assert_eq!(
            responses["default"]["content"]["application/json"]["schema"]["$ref"],
            "#/components/schemas/ErrorResponse"
        );
        assert_eq!(
            responses["404"]["content"]["application/json"]["schema"]["$ref"],
            "#/components/schemas/ErrorResponse"
        );
    }
}
