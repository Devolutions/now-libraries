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
    API_VERSION_STR, CapabilitiesResponse, ErrorCode, ErrorResponse, EvaluationResponse, ExecutionResponse,
    HealthResponse, PackageRequest, StatusRequest, StatusResponse,
};
use schemars::SchemaGenerator;

pub const MAX_REQUEST_BODY_BYTES: usize = 256 * 1024;

/// Implementation-neutral contract exposed by a package broker server.
#[async_trait]
pub trait PackageBrokerServer: Send + Sync {
    async fn health(&self) -> HealthResponse;
    async fn capabilities(&self) -> CapabilitiesResponse;
    async fn evaluate(&self, request: PackageRequest) -> Result<EvaluationResponse, ErrorResponse>;
    async fn execute(&self, request: PackageRequest) -> Result<ExecutionResponse, ErrorResponse>;
    async fn status(&self, request: StatusRequest) -> Result<StatusResponse, ErrorResponse>;
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
    ApiRouter::new()
        .api_route("/v1/health", get_with(health_handler, health_docs))
        .api_route("/v1/capabilities", get_with(capabilities_handler, capabilities_docs))
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
    use aide::openapi::{Components, SchemaObject};
    use now_policy::PolicyDocument;
    use schemars::schema::Schema;

    let root = openapi_schema_generator().into_root_schema_for::<PolicyDocument>();

    let components = api.components.get_or_insert_with(Components::default);

    components
        .schemas
        .entry("PolicyDocument".to_owned())
        .or_insert_with(|| SchemaObject {
            json_schema: Schema::Object(root.schema),
            external_docs: None,
            example: None,
        });

    for (name, schema) in root.definitions {
        components.schemas.entry(name).or_insert_with(|| SchemaObject {
            json_schema: schema,
            external_docs: None,
            example: None,
        });
    }
}

async fn health_handler(State(server): State<SharedPackageBrokerServer>) -> Json<HealthResponse> {
    Json(server.health().await)
}

async fn capabilities_handler(State(server): State<SharedPackageBrokerServer>) -> Json<CapabilitiesResponse> {
    Json(server.capabilities().await)
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
