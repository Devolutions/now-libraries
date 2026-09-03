//! Implementation-agnostic package broker server facade, HTTP router, and OpenAPI generator.

use std::sync::Arc;

use aide::axum::ApiRouter;
use aide::axum::routing::{get_with, post_with, put_with};
use aide::openapi::OpenApi;
use aide::transform::TransformOperation;
use async_trait::async_trait;
use axum::Json;
use axum::extract::State;
use axum::extract::rejection::JsonRejection;
use axum::http::StatusCode;
use axum::response::{IntoResponse, Response};
use serde::Serialize;

use now_policy_api::{
    API_VERSION_STR, CancelRequest, CancelResponse, CapabilitiesResponse, ErrorCode, ErrorResponse, EvaluationResponse,
    ExecutionResponse, HealthResponse, PackageRequest, PolicyManagementResponse, PolicyReplacementRequest,
    PolicyReplacementResponse, PolicyResponse, PolicyValidationRequest, PolicyValidationResponse, StatusRequest,
    StatusResponse,
};
use schemars::SchemaGenerator;

pub const MAX_REQUEST_BODY_BYTES: usize = 256 * 1024;
pub const MAX_POLICY_MANAGEMENT_BODY_BYTES: usize = 16 * 1024 * 1024;

/// Implementation-neutral contract exposed by a package broker server.
#[async_trait]
pub trait PackageBrokerServer: Send + Sync {
    async fn health(&self) -> HealthResponse;
    async fn capabilities(&self) -> CapabilitiesResponse;
    async fn active_policy(&self) -> Result<PolicyResponse, ErrorResponse>;
    async fn policy_management(&self) -> Result<PolicyManagementResponse, ErrorResponse>;
    async fn validate_policy(
        &self,
        request: PolicyValidationRequest,
    ) -> Result<PolicyValidationResponse, ErrorResponse>;
    async fn replace_policy(
        &self,
        request: PolicyReplacementRequest,
    ) -> Result<PolicyReplacementResponse, ErrorResponse>;
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
    ApiRouter::new()
        .api_route("/v1/health", get_with(health_handler, health_docs))
        .api_route("/v1/capabilities", get_with(capabilities_handler, capabilities_docs))
        .api_route("/v1/policy", get_with(policy_handler, policy_docs))
        .api_route(
            "/v1/policy/management",
            get_with(policy_management_handler, policy_management_docs),
        )
        .api_route(
            "/v1/policy/validate",
            post_with(policy_validation_handler, policy_validation_docs)
                .layer(axum::extract::DefaultBodyLimit::max(MAX_POLICY_MANAGEMENT_BODY_BYTES)),
        )
        .api_route(
            "/v1/policy",
            put_with(policy_replacement_handler, policy_replacement_docs)
                .layer(axum::extract::DefaultBodyLimit::max(MAX_POLICY_MANAGEMENT_BODY_BYTES)),
        )
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
    register_policy_management_body_limits(&mut api);
    register_policy_schema(&mut api);
    api
}

fn openapi_schema_generator() -> SchemaGenerator {
    use schemars::generate::SchemaSettings;

    SchemaSettings::openapi3().into()
}

fn register_policy_management_body_limits(api: &mut OpenApi) {
    const EXTENSION: &str = "x-max-request-body-bytes";

    let paths = api.paths.as_mut().expect("BUG: API routes should generate paths");
    for (path, method) in [("/v1/policy/validate", "post"), ("/v1/policy", "put")] {
        let path_item = paths
            .paths
            .get_mut(path)
            .and_then(aide::openapi::ReferenceOr::as_item_mut)
            .unwrap_or_else(|| panic!("BUG: missing generated OpenAPI path {path}"));
        let operation = match method {
            "post" => path_item.post.as_mut(),
            "put" => path_item.put.as_mut(),
            _ => unreachable!("BUG: unsupported policy management method"),
        }
        .unwrap_or_else(|| panic!("BUG: missing generated OpenAPI operation {method} {path}"));
        operation.extensions.insert(
            EXTENSION.to_owned(),
            serde_json::json!(MAX_POLICY_MANAGEMENT_BODY_BYTES),
        );
    }
}

fn register_policy_schema(api: &mut OpenApi) {
    use std::collections::BTreeMap;

    use aide::openapi::{Components, SchemaObject};
    use now_policy::{PolicyDocument, PolicyDraftDocument};

    let mut generator = openapi_schema_generator();
    let _ = generator.subschema_for::<PolicyDocument>();
    let _ = generator.subschema_for::<PolicyDraftDocument>();
    let definitions = generator.take_definitions(true);
    let renames: BTreeMap<_, _> = definitions
        .keys()
        .map(|name| {
            let component_name = if matches!(name.as_str(), "PolicyDocument" | "PolicyDraftDocument") {
                name.clone()
            } else {
                format!("PolicyModel{name}")
            };
            (name.clone(), component_name)
        })
        .collect();

    let components = api.components.get_or_insert_with(Components::default);

    for (name, schema) in definitions {
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

fn rewrite_policy_schema_refs(
    schema: serde_json::Value,
    renames: &std::collections::BTreeMap<String, String>,
) -> schemars::Schema {
    fn rewrite(value: &mut serde_json::Value, renames: &std::collections::BTreeMap<String, String>) {
        match value {
            serde_json::Value::String(reference) => {
                for prefix in ["#/components/schemas/", "#/$defs/", "#/definitions/"] {
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

    let mut value = schema;
    rewrite(&mut value, renames);
    schemars::Schema::try_from(value).expect("BUG: rewritten policy schema should remain valid")
}

async fn health_handler(State(server): State<SharedPackageBrokerServer>) -> Json<HealthResponse> {
    Json(server.health().await)
}

async fn capabilities_handler(State(server): State<SharedPackageBrokerServer>) -> Json<CapabilitiesResponse> {
    Json(server.capabilities().await)
}

async fn policy_handler(State(server): State<SharedPackageBrokerServer>) -> Response {
    broker_result(server.active_policy().await)
}

async fn policy_management_handler(State(server): State<SharedPackageBrokerServer>) -> Response {
    broker_result(server.policy_management().await)
}

async fn policy_validation_handler(
    State(server): State<SharedPackageBrokerServer>,
    request: Result<Json<PolicyValidationRequest>, JsonRejection>,
) -> Response {
    match request {
        Ok(Json(request)) => broker_result(server.validate_policy(request).await),
        Err(rejection) => request_rejection(&server, rejection, ErrorCode::MalformedDraft).await,
    }
}

async fn policy_replacement_handler(
    State(server): State<SharedPackageBrokerServer>,
    request: Result<Json<PolicyReplacementRequest>, JsonRejection>,
) -> Response {
    match request {
        Ok(Json(request)) => broker_result(server.replace_policy(request).await),
        Err(rejection) => request_rejection(&server, rejection, ErrorCode::MalformedDraft).await,
    }
}

async fn evaluate_handler(
    State(server): State<SharedPackageBrokerServer>,
    request: Result<Json<PackageRequest>, JsonRejection>,
) -> Response {
    match request {
        Ok(Json(request)) => broker_result(server.evaluate(request).await),
        Err(rejection) => request_rejection(&server, rejection, ErrorCode::BadRequest).await,
    }
}

async fn execute_handler(
    State(server): State<SharedPackageBrokerServer>,
    request: Result<Json<PackageRequest>, JsonRejection>,
) -> Response {
    match request {
        Ok(Json(request)) => broker_result(server.execute(request).await),
        Err(rejection) => request_rejection(&server, rejection, ErrorCode::BadRequest).await,
    }
}

async fn status_handler(
    State(server): State<SharedPackageBrokerServer>,
    request: Result<Json<StatusRequest>, JsonRejection>,
) -> Response {
    match request {
        Ok(Json(request)) => broker_result(server.status(request).await),
        Err(rejection) => request_rejection(&server, rejection, ErrorCode::BadRequest).await,
    }
}

async fn cancel_handler(
    State(server): State<SharedPackageBrokerServer>,
    request: Result<Json<CancelRequest>, JsonRejection>,
) -> Response {
    match request {
        Ok(Json(request)) => broker_result(server.cancel(request).await),
        Err(rejection) => request_rejection(&server, rejection, ErrorCode::BadRequest).await,
    }
}

async fn request_rejection(
    server: &SharedPackageBrokerServer,
    rejection: JsonRejection,
    malformed_code: ErrorCode,
) -> Response {
    let status = rejection.status();
    let (code, message) = match status {
        StatusCode::PAYLOAD_TOO_LARGE => (ErrorCode::PayloadTooLarge, "request body exceeds the broker limit"),
        StatusCode::UNSUPPORTED_MEDIA_TYPE => (
            ErrorCode::UnsupportedMediaType,
            "request Content-Type must be application/json",
        ),
        _ => (malformed_code, "request body is not a valid broker document"),
    };
    let error = ErrorResponse {
        response_kind: now_policy_api::ErrorResponseKind,
        response_version: API_VERSION_STR.into(),
        server: server.capabilities().await.server,
        code,
        message: message.to_owned(),
        details: Vec::new(),
        validation: None,
        management: None,
    };
    (error_status(error.code), Json(error)).into_response()
}

fn broker_result<T: Serialize>(result: Result<T, ErrorResponse>) -> Response {
    match result {
        Ok(response) => (StatusCode::OK, Json(response)).into_response(),
        Err(error) => (error_status(error.code), Json(error)).into_response(),
    }
}

fn error_status(code: ErrorCode) -> StatusCode {
    match code {
        ErrorCode::BadRequest | ErrorCode::MalformedDraft => StatusCode::BAD_REQUEST,
        ErrorCode::Unauthorized | ErrorCode::Unauthenticated => StatusCode::UNAUTHORIZED,
        ErrorCode::Forbidden | ErrorCode::AdministratorRequired => StatusCode::FORBIDDEN,
        ErrorCode::NotFound => StatusCode::NOT_FOUND,
        ErrorCode::Conflict
        | ErrorCode::WarningConfirmationRequired
        | ErrorCode::UnsafePolicyPath
        | ErrorCode::StalePolicyStoreToken => StatusCode::CONFLICT,
        ErrorCode::PayloadTooLarge => StatusCode::PAYLOAD_TOO_LARGE,
        ErrorCode::UnsupportedMediaType => StatusCode::UNSUPPORTED_MEDIA_TYPE,
        ErrorCode::ValidationFailed
        | ErrorCode::InvalidPolicy
        | ErrorCode::UnsupportedPolicyFormat
        | ErrorCode::UnsupportedPolicyFilesystem => StatusCode::UNPROCESSABLE_ENTITY,
        ErrorCode::BrokerPaused => StatusCode::SERVICE_UNAVAILABLE,
        ErrorCode::InternalError | ErrorCode::PolicyPersistenceFailed | ErrorCode::PolicyActivationFailed => {
            StatusCode::INTERNAL_SERVER_ERROR
        }
        ErrorCode::Timeout => StatusCode::GATEWAY_TIMEOUT,
        ErrorCode::UnsupportedEndpoint => StatusCode::NOT_IMPLEMENTED,
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

fn policy_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Get active policy")
        .description("Returns the active parsed policy document. A 404 response means no active policy is configured.")
        .response::<200, Json<PolicyResponse>>()
        .response::<404, Json<ErrorResponse>>()
        .default_response::<Json<ErrorResponse>>()
}

fn policy_management_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Get policy management state")
        .description(
            "Atomically returns configured policy state and advisory write capability. \
             Capability fields are UX guidance and are rechecked during replacement.",
        )
        .response::<200, Json<PolicyManagementResponse>>()
        .default_response::<Json<ErrorResponse>>()
}

fn policy_validation_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Validate a policy draft")
        .description(
            "Authoritatively validates raw draft JSON without discarding unknown fields. \
             Validation findings are returned with HTTP 200; malformed envelopes use ErrorResponse. \
             The complete HTTP request body, including the envelope, is limited to 16 MiB \
             (16,777,216 bytes). This operational transport cap is below the schema's theoretical maximum.",
        )
        .response::<200, Json<PolicyValidationResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<413, Json<ErrorResponse>>()
        .default_response::<Json<ErrorResponse>>()
}

fn policy_replacement_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Replace the configured policy")
        .description(
            "Reparses and revalidates the raw draft inside the write transaction, then atomically \
             commits it only when the expected opaque store token and validation receipt still match. \
             The complete HTTP request body, including the envelope, is limited to 16 MiB \
             (16,777,216 bytes). This operational transport cap is below the schema's theoretical maximum.",
        )
        .response::<200, Json<PolicyReplacementResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<401, Json<ErrorResponse>>()
        .response::<403, Json<ErrorResponse>>()
        .response::<409, Json<ErrorResponse>>()
        .response::<413, Json<ErrorResponse>>()
        .response::<415, Json<ErrorResponse>>()
        .response::<422, Json<ErrorResponse>>()
        .response::<500, Json<ErrorResponse>>()
        .response::<501, Json<ErrorResponse>>()
}

fn evaluate_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Evaluate package operation")
        .description("Evaluates a package operation against policy without requiring elevated execution.")
        .response::<200, Json<EvaluationResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<413, Json<ErrorResponse>>()
        .response::<415, Json<ErrorResponse>>()
        .response::<422, Json<ErrorResponse>>()
        .response::<503, Json<ErrorResponse>>()
}

fn execute_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Execute package operation")
        .description("Evaluates a package operation and submits it to the implementation for execution when allowed.")
        .response::<200, Json<ExecutionResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<409, Json<ErrorResponse>>()
        .response::<413, Json<ErrorResponse>>()
        .response::<415, Json<ErrorResponse>>()
        .response::<422, Json<ErrorResponse>>()
        .response::<503, Json<ErrorResponse>>()
}

fn status_docs(op: TransformOperation<'_>) -> TransformOperation<'_> {
    op.summary("Query package operation status")
        .description("Returns the current status of a previously submitted package operation.")
        .response::<200, Json<StatusResponse>>()
        .response::<400, Json<ErrorResponse>>()
        .response::<413, Json<ErrorResponse>>()
        .response::<415, Json<ErrorResponse>>()
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
        .response::<413, Json<ErrorResponse>>()
        .response::<415, Json<ErrorResponse>>()
        .response::<404, Json<ErrorResponse>>()
}

#[cfg(test)]
mod tests {
    use super::{MAX_POLICY_MANAGEMENT_BODY_BYTES, openapi};

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

    #[test]
    fn policy_replacement_openapi_documents_unsupported_media_type() {
        let api = serde_json::to_value(openapi()).expect("OpenAPI should serialize");
        let response = &api["paths"]["/v1/policy"]["put"]["responses"]["415"];

        assert_eq!(
            response["content"]["application/json"]["schema"]["$ref"],
            "#/components/schemas/ErrorResponse"
        );
    }

    #[test]
    fn package_operation_openapi_documents_transport_rejections() {
        let api = serde_json::to_value(openapi()).expect("OpenAPI should serialize");

        for path in [
            "/v1/package-operations/evaluate",
            "/v1/package-operations/execute",
            "/v1/package-operations/get-status",
            "/v1/package-operations/cancel",
        ] {
            for status in ["413", "415"] {
                assert_eq!(
                    api["paths"][path]["post"]["responses"][status]["content"]["application/json"]["schema"]["$ref"],
                    "#/components/schemas/ErrorResponse",
                    "missing structured {status} response for {path}"
                );
            }
        }
    }

    #[test]
    fn policy_openapi_preserves_nullable_optional_document_fields() {
        let api = serde_json::to_value(openapi()).expect("OpenAPI should serialize");
        for pointer in [
            "/components/schemas/PolicyManagementSnapshotFields/properties/Policy/anyOf",
            "/components/schemas/PolicyValidationResultFields/properties/CanonicalDraft/anyOf",
        ] {
            let variants = api
                .pointer(pointer)
                .and_then(serde_json::Value::as_array)
                .unwrap_or_else(|| panic!("missing nullable variants at {pointer}"));
            assert!(
                variants.iter().any(|variant| {
                    variant.get("nullable") == Some(&serde_json::Value::Bool(true))
                        || variant.get("type").and_then(serde_json::Value::as_str) == Some("null")
                        || variant
                            .get("enum")
                            .and_then(serde_json::Value::as_array)
                            .is_some_and(|values| values.iter().any(serde_json::Value::is_null))
                }),
                "{pointer} should retain an explicit null variant"
            );
        }
    }

    #[test]
    fn policy_openapi_exposes_management_request_body_limit() {
        let api = serde_json::to_value(openapi()).expect("OpenAPI should serialize");

        for pointer in [
            "/paths/~1v1~1policy~1validate/post/x-max-request-body-bytes",
            "/paths/~1v1~1policy/put/x-max-request-body-bytes",
        ] {
            assert_eq!(
                api.pointer(pointer),
                Some(&serde_json::json!(MAX_POLICY_MANAGEMENT_BODY_BYTES)),
                "missing management request-body limit at {pointer}"
            );
        }
    }
}
