#![allow(clippy::unwrap_used, unused_crate_dependencies)]

use std::path::{Path, PathBuf};

use axum::body::{Body, to_bytes};
use axum::http::{Request, StatusCode};
use now_policy_server_template::{
    API_VERSION_STR, CapabilitiesResponse, CapabilitiesResponseKind, DEFAULT_PIPE_NAME, EvaluationResponse,
    ExecutionResponse, HealthResponse, HealthResponseKind, HealthStatus, MAX_REQUEST_BODY_BYTES, ManagerName,
    MockPackageBrokerServer, Operation, PackageBrokerServer, PackageRequest, Scope, StatusRequest, StatusRequestKind,
    StatusResponse, Transport, api_router,
};
use tower::ServiceExt;

fn samples_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("assets/samples")
}

fn load_text_file(path: &Path) -> String {
    std::fs::read_to_string(path).unwrap_or_else(|e| panic!("failed to read {}: {e}", path.display()))
}

fn load_json_file(path: &Path) -> serde_json::Value {
    let content = load_text_file(path);
    serde_json::from_str(&content).unwrap_or_else(|e| panic!("failed to parse {}: {e}", path.display()))
}

fn json_files(dir: &Path) -> Vec<PathBuf> {
    std::fs::read_dir(dir)
        .unwrap()
        .filter_map(|entry| {
            let entry = entry.ok()?;
            let path = entry.path();
            (path.extension().and_then(|e| e.to_str()) == Some("json")).then_some(path)
        })
        .collect()
}

fn sample_document_files(dir: &Path) -> Vec<PathBuf> {
    std::fs::read_dir(dir)
        .unwrap()
        .filter_map(|entry| {
            let entry = entry.ok()?;
            let path = entry.path();
            matches!(path.extension().and_then(|e| e.to_str()), Some("json" | "yaml" | "yml")).then_some(path)
        })
        .collect()
}

fn load_package_request(path: &Path) -> Result<PackageRequest, String> {
    let content = load_text_file(path);
    match path.extension().and_then(|e| e.to_str()) {
        Some("yaml" | "yml") => serde_yaml::from_str(&content).map_err(|e| e.to_string()),
        _ => serde_json::from_str(&content).map_err(|e| e.to_string()),
    }
}

fn assert_response_sample_deserializes(path: &Path) {
    let name = path.file_name().unwrap().to_string_lossy();
    if name.starts_with("status-") {
        let _: StatusResponse = serde_json::from_value(load_json_file(path))
            .unwrap_or_else(|e| panic!("failed to deserialize {}: {e}", path.display()));
    } else if name.starts_with("execution-") {
        let _: ExecutionResponse = serde_json::from_value(load_json_file(path))
            .unwrap_or_else(|e| panic!("failed to deserialize {}: {e}", path.display()));
    } else if name.starts_with("health-") {
        let _: HealthResponse = serde_json::from_value(load_json_file(path))
            .unwrap_or_else(|e| panic!("failed to deserialize {}: {e}", path.display()));
    } else if name.starts_with("capabilities") {
        let _: CapabilitiesResponse = serde_json::from_value(load_json_file(path))
            .unwrap_or_else(|e| panic!("failed to deserialize {}: {e}", path.display()));
    } else {
        let _: EvaluationResponse = serde_json::from_value(load_json_file(path))
            .unwrap_or_else(|e| panic!("failed to deserialize {}: {e}", path.display()));
    }
}

fn is_invalid_request_fixture(path: &Path) -> bool {
    path.file_name().and_then(|name| name.to_str()) == Some("missing-package-id.request.json")
}

fn response_sample_path(name: &str) -> PathBuf {
    samples_dir().join("responses").join(name)
}

#[test]
fn all_sample_requests_deserialize() {
    for path in sample_document_files(&samples_dir().join("requests")) {
        let name = path.file_name().unwrap().to_string_lossy();
        if name.starts_with("status-") {
            let _: StatusRequest = serde_json::from_value(load_json_file(&path))
                .unwrap_or_else(|e| panic!("failed to deserialize {}: {e}", path.display()));
        } else if is_invalid_request_fixture(&path) {
            assert!(
                load_package_request(&path).is_err(),
                "{} is expected to be an invalid request fixture",
                path.display()
            );
        } else {
            load_package_request(&path).unwrap_or_else(|e| panic!("failed to deserialize {}: {e}", path.display()));
        }
    }
}

#[test]
fn all_sample_responses_deserialize() {
    for path in json_files(&samples_dir().join("responses")) {
        assert_response_sample_deserializes(&path);
    }
}

#[test]
fn health_response_sample_matches_api_contract() {
    let content = load_text_file(&response_sample_path("health-ready.response.json"));
    let health: HealthResponse = serde_json::from_str(&content).unwrap();

    assert_eq!(health.status, HealthStatus::Ready);
    assert_eq!(health.policy_id, "mock.policy");
    assert_eq!(health.response_kind, HealthResponseKind);
    assert_eq!(&*health.response_version, API_VERSION_STR);
    assert_eq!(health.server.transport, Transport::HttpNamedPipe);
}

#[test]
fn capabilities_response_sample_matches_api_contract() {
    let content = load_text_file(&response_sample_path("capabilities.response.json"));
    let capabilities: CapabilitiesResponse = serde_json::from_str(&content).unwrap();

    assert_eq!(capabilities.response_kind, CapabilitiesResponseKind);
    assert_eq!(&*capabilities.response_version, API_VERSION_STR);
    assert_eq!(capabilities.server.transport, Transport::HttpNamedPipe);
    assert_eq!(capabilities.transports, vec![Transport::HttpNamedPipe]);
    assert_eq!(capabilities.max_request_body_bytes, MAX_REQUEST_BODY_BYTES as u64);

    let winget = capabilities
        .managers
        .iter()
        .find(|manager| manager.manager == ManagerName::Winget)
        .expect("winget capabilities should be declared");
    assert_eq!(
        winget.operations,
        vec![Operation::Install, Operation::Update, Operation::Uninstall]
    );
    assert_eq!(winget.scopes, vec![Scope::User, Scope::Machine]);
    assert!(winget.supports_capture_output);
    assert!(winget.supports_details);
}

#[test]
fn invalid_request_missing_package_id_fails_deserialization() {
    let path = samples_dir().join("requests/missing-package-id.request.json");
    let content = std::fs::read_to_string(&path).unwrap();
    let result: Result<PackageRequest, _> = serde_json::from_str(&content);
    assert!(
        result.is_err(),
        "request with missing package.id should fail deserialization"
    );
}

#[test]
fn semantic_version_rejects_invalid_values() {
    let result: Result<now_policy_server_template::SemanticVersion, _> = serde_json::from_str(r#""not-a-semver""#);
    assert!(result.is_err(), "semantic versions should retain SemVer validation");
}

#[test]
fn request_kind_marker_rejects_wrong_value() {
    let result: Result<now_policy_server_template::PackageRequestKind, _> = serde_json::from_str(r#""StatusRequest""#);

    assert!(
        result.is_err(),
        "request kind markers should reject mismatched discriminators"
    );
}

#[tokio::test]
async fn mock_server_returns_registered_fixture_responses() {
    let request_path = samples_dir().join("requests/winget-vscode-install.request.json");
    let response_path = samples_dir().join("responses/winget-vscode-install.allowed.response.json");
    let execution_path = samples_dir().join("responses/execution-winget-vscode-install.response.json");
    let status_path = samples_dir().join("responses/status-completed.response.json");

    let request: PackageRequest = serde_json::from_value(load_json_file(&request_path)).unwrap();
    let response: EvaluationResponse = serde_json::from_value(load_json_file(&response_path)).unwrap();
    let execution: ExecutionResponse = serde_json::from_value(load_json_file(&execution_path)).unwrap();
    let status: StatusResponse = serde_json::from_value(load_json_file(&status_path)).unwrap();

    let server = MockPackageBrokerServer::new(DEFAULT_PIPE_NAME)
        .with_evaluation_response(response.clone())
        .with_execution_response(execution.clone())
        .with_status_response(status.clone());

    let evaluated = server.evaluate(request.clone()).await.unwrap();
    assert_eq!(evaluated.request_id, response.request_id);

    let executed = server.execute(request.clone()).await.unwrap();
    assert_eq!(
        executed.operation.as_ref().map(|op| &op.operation_id),
        execution.operation.as_ref().map(|op| &op.operation_id)
    );

    let status_request = StatusRequest {
        request_kind: StatusRequestKind,
        request_version: API_VERSION_STR.into(),
        operation_id: status.operation_id.clone(),
        client: request.client.clone(),
    };
    let status = server.status(status_request).await.unwrap();
    assert_eq!(status.status, now_policy_server_template::OperationStatus::Completed);
}

#[tokio::test]
async fn mock_health_and_capabilities_match_response_samples() {
    let health_content = load_text_file(&response_sample_path("health-ready.response.json"));
    let expected_health: HealthResponse = serde_json::from_str(&health_content).unwrap();

    let capabilities_content = load_text_file(&response_sample_path("capabilities.response.json"));
    let expected_capabilities: CapabilitiesResponse = serde_json::from_str(&capabilities_content).unwrap();

    let server = MockPackageBrokerServer::new(DEFAULT_PIPE_NAME);
    let actual_health = server.health().await;
    let actual_capabilities = server.capabilities().await;

    assert_eq!(actual_health.status, expected_health.status);
    assert_eq!(actual_health.policy_id, expected_health.policy_id);
    assert_eq!(actual_health.response_kind, expected_health.response_kind);
    assert_eq!(&*actual_health.response_version, &*expected_health.response_version);
    assert_eq!(actual_health.server.transport, expected_health.server.transport);

    assert_eq!(actual_capabilities.transports, expected_capabilities.transports);
    assert_eq!(
        actual_capabilities.max_request_body_bytes,
        expected_capabilities.max_request_body_bytes
    );
    assert_eq!(actual_capabilities.managers.len(), expected_capabilities.managers.len());
}

#[tokio::test]
async fn api_router_dispatches_to_package_broker_server() {
    let request_path = samples_dir().join("requests/winget-vscode-install.request.json");
    let response_path = samples_dir().join("responses/winget-vscode-install.allowed.response.json");

    let request: PackageRequest = serde_json::from_value(load_json_file(&request_path)).unwrap();
    let expected_response: EvaluationResponse = serde_json::from_value(load_json_file(&response_path)).unwrap();
    let app =
        api_router(MockPackageBrokerServer::new(DEFAULT_PIPE_NAME).with_evaluation_response(expected_response.clone()));

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/v1/package-operations/evaluate")
                .header("content-type", "application/json")
                .body(Body::from(serde_json::to_vec(&request).unwrap()))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::OK);

    let body = to_bytes(response.into_body(), usize::MAX).await.unwrap();
    let actual_response: EvaluationResponse = serde_json::from_slice(&body).unwrap();
    assert_eq!(actual_response.request_id, expected_response.request_id);
    assert_eq!(actual_response.decision.decision, expected_response.decision.decision);
}

#[tokio::test]
async fn api_router_dispatches_status_request_body_to_package_broker_server() {
    let request_path = samples_dir().join("requests/status-query-completed.request.json");
    let status_path = samples_dir().join("responses/status-completed.response.json");

    let request: StatusRequest = serde_json::from_value(load_json_file(&request_path)).unwrap();
    let expected_status: StatusResponse = serde_json::from_value(load_json_file(&status_path)).unwrap();
    let app = api_router(MockPackageBrokerServer::new(DEFAULT_PIPE_NAME).with_status_response(expected_status.clone()));

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/v1/package-operations/get-status")
                .header("content-type", "application/json")
                .body(Body::from(serde_json::to_vec(&request).unwrap()))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::OK);

    let body = to_bytes(response.into_body(), usize::MAX).await.unwrap();
    let actual_status: StatusResponse = serde_json::from_slice(&body).unwrap();
    assert_eq!(actual_status.operation_id, expected_status.operation_id);
    assert_eq!(actual_status.status, expected_status.status);
}

#[tokio::test]
async fn api_router_maps_broker_errors_to_http_status() {
    let request_path = samples_dir().join("requests/winget-vscode-install.request.json");
    let request: PackageRequest = serde_json::from_value(load_json_file(&request_path)).unwrap();
    let app = api_router(MockPackageBrokerServer::new(DEFAULT_PIPE_NAME));

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/v1/package-operations/evaluate")
                .header("content-type", "application/json")
                .body(Body::from(serde_json::to_vec(&request).unwrap()))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

#[tokio::test]
async fn api_router_rejects_request_bodies_larger_than_capability_limit() {
    let app = api_router(MockPackageBrokerServer::new(DEFAULT_PIPE_NAME));
    let oversized_json = format!("\"{}\"", "x".repeat(MAX_REQUEST_BODY_BYTES + 1));

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/v1/package-operations/evaluate")
                .header("content-type", "application/json")
                .body(Body::from(oversized_json))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::PAYLOAD_TOO_LARGE);
}
