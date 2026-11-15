//! Behavior of `POST /v1/package-operations/execute-grant` at the HTTP seam.
//!
//! Expected statuses, codes and header rules come from the Gateway package-management contract
//! sections `operations`, `broker-grant-response` and `broker-grant-errors`.

#![allow(clippy::unwrap_used, unused_crate_dependencies)]

use std::path::PathBuf;

use async_trait::async_trait;
use axum::body::{Body, to_bytes};
use axum::http::{HeaderName, HeaderValue, Request, StatusCode};
use now_policy_server_template::GrantExecutionResponse;
use now_policy_server_template::{
    CancelRequest, CancelResponse, CapabilitiesResponse, ErrorCode, ErrorDetail, ErrorResponse, EvaluationResponse,
    ExecutionResponse, HealthResponse, MAX_REQUEST_BODY_BYTES, PackageBrokerServer, PackageRequest,
    PolicyManagementResponse, PolicyReplacementRequest, PolicyReplacementResponse, PolicyResponse,
    PolicyValidationRequest, PolicyValidationResponse, StatusRequest, StatusResponse, api_router,
};
use rstest::rstest;
use serde_json::{Value, json};
use tower::ServiceExt;

use support::mock::MockPackageBrokerServer;

mod support;

const GRANT_PATH: &str = "/v1/package-operations/execute-grant";
const LEGACY_PATH: &str = "/v1/package-operations/execute";
const JSON: &str = "application/json";

fn fixture(relative: &str) -> Vec<u8> {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../test-data/package-broker")
        .join(relative);
    std::fs::read(&path).unwrap_or_else(|e| panic!("failed to read {}: {e}", path.display()))
}

fn request_body() -> Vec<u8> {
    fixture("requests/winget-vscode-install.request.json")
}

fn grant_response() -> GrantExecutionResponse {
    serde_json::from_value(json!({
        "ResponseKind": "GrantExecutionResponse",
        "ResponseVersion": "1.0",
        "Server": { "ServerVersion": "1.2.3", "Transport": "HttpNamedPipe" },
        "RequestId": "0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11",
        "ReceivedAt": "2026-11-15T05:07:42Z",
        "CompletedAt": "2026-11-15T05:07:43Z",
        "Request": { "Manager": "Winget", "PackageId": "Microsoft.VisualStudioCode", "Operation": "Install" },
        "Authorization": { "Kind": "RemoteGrant" },
        "Operation": { "OperationId": "op-7f3a", "Status": "Running", "SubmittedAt": "2026-11-15T05:07:42Z" }
    }))
    .unwrap()
}

fn grant_headers<'a>(grants: &[&'a [u8]], accepts: &[&'a str]) -> Vec<(&'static str, Vec<u8>)> {
    let grants = grants
        .iter()
        .map(|grant| ("x-devolutions-package-grant", grant.to_vec()));
    let accepts = accepts.iter().map(|accept| ("accept", accept.as_bytes().to_vec()));
    grants.chain(accepts).collect()
}

async fn post(
    server: impl PackageBrokerServer + 'static,
    path: &str,
    headers: Vec<(&str, Vec<u8>)>,
    content_type: &str,
    body: Vec<u8>,
) -> (StatusCode, Value, String) {
    let mut request = Request::builder()
        .method("POST")
        .uri(path)
        .header("content-type", content_type);
    for (name, value) in headers {
        request = request.header(
            HeaderName::from_bytes(name.as_bytes()).unwrap(),
            HeaderValue::from_bytes(&value).unwrap(),
        );
    }

    let response = api_router(server)
        .oneshot(request.body(Body::from(body)).unwrap())
        .await
        .unwrap();
    let status = response.status();
    let text = String::from_utf8(to_bytes(response.into_body(), usize::MAX).await.unwrap().to_vec()).unwrap();
    let json = serde_json::from_str(&text).unwrap_or(Value::Null);
    (status, json, text)
}

fn mock() -> MockPackageBrokerServer {
    MockPackageBrokerServer::new("grant-tests")
}

fn error_response(code: ErrorCode, message: &str) -> ErrorResponse {
    let mut error = serde_json::from_value::<ErrorResponse>(json!({
        "ResponseKind": "ErrorResponse",
        "ResponseVersion": "1.0",
        "Server": { "ServerVersion": "1.2.3", "Transport": "HttpNamedPipe" },
        "Code": "Forbidden",
        "Message": "x",
    }))
    .unwrap();
    error.code = code;
    error.message = message.to_owned();
    error
}

#[rstest]
#[case::missing_grant(&[] as &[&[u8]], &[JSON], 401, "Unauthenticated")]
#[case::duplicate_grant(&[b"secret-a" as &[u8], b"secret-b"], &[JSON], 400, "BadRequest")]
#[case::empty_grant(&[b"" as &[u8]], &[JSON], 400, "BadRequest")]
#[case::oversized_grant(&[&[b'a'; 2049] as &[u8]], &[JSON], 400, "BadRequest")]
#[case::leading_dot(&[b".secret" as &[u8]], &[JSON], 400, "BadRequest")]
#[case::contains_space(&[b"secret value" as &[u8]], &[JSON], 400, "BadRequest")]
#[case::contains_slash(&[b"secret/value" as &[u8]], &[JSON], 400, "BadRequest")]
#[case::non_ascii(&["secret-é".as_bytes() as &[u8]], &[JSON], 400, "BadRequest")]
#[case::non_utf8(&[b"secret-\xff" as &[u8]], &[JSON], 400, "BadRequest")]
#[case::missing_accept(&[b"secret" as &[u8]], &[], 400, "BadRequest")]
#[case::duplicate_accept(&[b"secret" as &[u8]], &[JSON, JSON], 400, "BadRequest")]
#[case::wildcard_accept(&[b"secret" as &[u8]], &["*/*"], 400, "BadRequest")]
#[case::unsupported_accept(&[b"secret" as &[u8]], &["application/xml"], 400, "BadRequest")]
#[tokio::test]
async fn grant_header_and_accept_rejections_stop_before_the_server(
    #[case] grants: &[&[u8]],
    #[case] accepts: &[&str],
    #[case] status: u16,
    #[case] code: &str,
) {
    let server = mock().with_grant_result(Ok(grant_response()));

    let (actual_status, body, text) = post(
        server.clone(),
        GRANT_PATH,
        grant_headers(grants, accepts),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(actual_status.as_u16(), status);
    assert_eq!(body["Code"], code);
    assert!(server.grants().is_empty(), "the server must not be invoked");
    assert!(!text.contains("secret"), "the body must not echo the grant");
}

#[rstest]
#[case::wrong_content_type("text/plain", request_body(), 415, "UnsupportedMediaType")]
#[case::malformed_json(JSON, b"{".to_vec(), 400, "BadRequest")]
#[case::unknown_field(JSON, with_unknown_field(), 400, "BadRequest")]
#[case::oversized_body(JSON, vec![b' '; MAX_REQUEST_BODY_BYTES + 1], 413, "PayloadTooLarge")]
#[tokio::test]
async fn body_rejections_stop_before_the_server(
    #[case] content_type: &str,
    #[case] body: Vec<u8>,
    #[case] status: u16,
    #[case] code: &str,
) {
    let server = mock().with_grant_result(Ok(grant_response()));

    let (actual_status, response, text) = post(
        server.clone(),
        GRANT_PATH,
        grant_headers(&[b"secret-grant" as &[u8]], &[JSON]),
        content_type,
        body,
    )
    .await;

    assert_eq!(actual_status.as_u16(), status);
    assert_eq!(response["Code"], code);
    assert!(server.grants().is_empty(), "the server must not be invoked");
    assert!(!text.contains("secret-grant"), "the body must not echo the grant");
}

fn with_unknown_field() -> Vec<u8> {
    let mut value: Value = serde_json::from_slice(&request_body()).unwrap();
    value["Unexpected"] = json!(true);
    serde_json::to_vec(&value).unwrap()
}

#[rstest]
#[case::one_character(b"a".to_vec())]
#[case::maximum_length(vec![b'a'; 2048])]
#[case::every_punctuation(b"a._~:-9".to_vec())]
#[case::uppercase(b"Grant".to_vec())]
#[tokio::test]
async fn valid_grant_reaches_the_server_unchanged(#[case] grant: Vec<u8>) {
    let server = mock().with_grant_result(Ok(grant_response()));

    let (status, body, _) = post(
        server.clone(),
        GRANT_PATH,
        grant_headers(&[&grant], &[JSON]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(status, StatusCode::OK);
    assert_eq!(body["ResponseKind"], "GrantExecutionResponse");
    assert_eq!(server.grants(), [String::from_utf8(grant).unwrap()]);
}

#[tokio::test]
async fn accept_value_is_trimmed_and_case_insensitive() {
    let server = mock().with_grant_result(Ok(grant_response()));

    let (status, _, _) = post(
        server.clone(),
        GRANT_PATH,
        grant_headers(&[b"grant" as &[u8]], &[" APPLICATION/JSON "]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(status, StatusCode::OK);
    assert_eq!(server.grants().len(), 1);
}

#[rstest]
#[case::valid_grant(b"secret-grant")]
#[case::empty_value(b"")]
#[tokio::test]
async fn grant_header_on_legacy_execute_is_rejected_before_the_server(#[case] grant: &[u8]) {
    let server = mock();

    let (status, body, text) = post(
        server.clone(),
        LEGACY_PATH,
        grant_headers(&[grant], &[]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert_eq!(body["Code"], "BadRequest");
    assert_eq!(server.execute_calls(), 0);
    assert!(!text.contains("secret-grant"));
}

#[tokio::test]
async fn legacy_execute_without_the_grant_header_is_unchanged() {
    let execution = serde_json::from_slice::<ExecutionResponse>(&fixture(
        "responses/execution-winget-vscode-install.response.json",
    ))
    .unwrap();
    let server = mock().with_execution_response(execution);

    let (status, body, _) = post(server.clone(), LEGACY_PATH, Vec::new(), JSON, request_body()).await;

    assert_eq!(status, StatusCode::OK);
    assert_eq!(body["ResponseKind"], "ExecutionResponse");
    assert_eq!(server.execute_calls(), 1);
}

#[tokio::test]
async fn brokers_without_grant_support_return_unsupported_endpoint() {
    let (status, body, _) = post(
        WithoutGrantSupport(mock()),
        GRANT_PATH,
        grant_headers(&[b"secret-grant" as &[u8]], &[JSON]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(status, StatusCode::NOT_IMPLEMENTED);
    assert_eq!(body["Code"], "UnsupportedEndpoint");
}

#[rstest]
#[case::forbidden(error_response(ErrorCode::Forbidden, "grant is not valid"), 403, "Forbidden")]
#[case::conflict(error_response(ErrorCode::Conflict, "grant already used"), 409, "Conflict")]
#[tokio::test]
async fn contract_conforming_server_errors_pass_through(
    #[case] error: ErrorResponse,
    #[case] status: u16,
    #[case] code: &str,
) {
    let message = error.message.clone();
    let server = mock().with_grant_result(Err(error));

    let (actual_status, body, _) = post(
        server,
        GRANT_PATH,
        grant_headers(&[b"grant" as &[u8]], &[JSON]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(actual_status.as_u16(), status);
    assert_eq!(body["Code"], code);
    assert_eq!(body["Message"], message);
}

fn detail(code: Option<String>, path: Option<String>, message: &str) -> ErrorDetail {
    ErrorDetail {
        code,
        path,
        message: message.to_owned(),
    }
}

fn error_with(edit: impl FnOnce(&mut ErrorResponse)) -> ErrorResponse {
    let mut error = error_response(ErrorCode::Forbidden, "grant is not valid");
    edit(&mut error);
    error
}

#[rstest]
#[case::validation(error_with(|e| {
    let response: PolicyValidationResponse =
        serde_json::from_slice(&fixture("responses/policy-validation.valid.response.json")).unwrap();
    e.validation = Some(response.validation);
}))]
#[case::management(error_with(|e| {
    let response: PolicyManagementResponse =
        serde_json::from_slice(&fixture("responses/policy-management.active.response.json")).unwrap();
    e.management = Some(response.management);
}))]
#[case::empty_message(error_with(|e| e.message.clear()))]
#[case::oversized_message(error_with(|e| e.message = "m".repeat(2049)))]
#[case::empty_detail_message(error_with(|e| e.details = vec![detail(None, None, "")]))]
#[case::oversized_detail_code(error_with(|e| e.details = vec![detail(Some("c".repeat(129)), None, "d")]))]
#[case::oversized_detail_path(error_with(|e| e.details = vec![detail(None, Some("p".repeat(513)), "d")]))]
#[case::not_found_reads_as_missing_route(error_with(|e| e.code = ErrorCode::NotFound))]
#[case::management_code(error_with(|e| e.code = ErrorCode::AdministratorRequired))]
#[tokio::test]
async fn server_errors_that_break_the_error_body_rules_become_internal_errors(#[case] error: ErrorResponse) {
    let server = mock().with_grant_result(Err(error));

    let (status, body, _) = post(
        server,
        GRANT_PATH,
        grant_headers(&[b"grant" as &[u8]], &[JSON]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
    assert_eq!(body["Code"], "InternalError");
    assert!(body.get("Validation").is_none() && body.get("Management").is_none());
}

#[tokio::test]
async fn error_details_at_the_length_limits_pass_through() {
    let error = error_with(|e| {
        e.message = "m".repeat(2048);
        e.details = vec![detail(Some("c".repeat(128)), Some("p".repeat(512)), &"d".repeat(2048))];
    });

    let (status, body, _) = post(
        mock().with_grant_result(Err(error)),
        GRANT_PATH,
        grant_headers(&[b"grant" as &[u8]], &[JSON]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(status, StatusCode::FORBIDDEN);
    assert_eq!(body["Details"][0]["Path"].as_str().unwrap().len(), 512);
}

#[tokio::test]
async fn success_response_that_fails_validation_becomes_an_internal_error() {
    let mut response = grant_response();
    response.server.server_version = "v".repeat(129);

    let (status, body, _) = post(
        mock().with_grant_result(Ok(response)),
        GRANT_PATH,
        grant_headers(&[b"grant" as &[u8]], &[JSON]),
        JSON,
        request_body(),
    )
    .await;

    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
    assert_eq!(body["Code"], "InternalError");
}

/// Broker that keeps the trait's default `execute_grant`.
struct WithoutGrantSupport(MockPackageBrokerServer);

#[async_trait]
impl PackageBrokerServer for WithoutGrantSupport {
    async fn health(&self) -> HealthResponse {
        self.0.health().await
    }

    async fn capabilities(&self) -> CapabilitiesResponse {
        self.0.capabilities().await
    }

    async fn active_policy(&self) -> Result<PolicyResponse, ErrorResponse> {
        self.0.active_policy().await
    }

    async fn policy_management(&self) -> Result<PolicyManagementResponse, ErrorResponse> {
        self.0.policy_management().await
    }

    async fn validate_policy(
        &self,
        request: PolicyValidationRequest,
    ) -> Result<PolicyValidationResponse, ErrorResponse> {
        self.0.validate_policy(request).await
    }

    async fn replace_policy(
        &self,
        request: PolicyReplacementRequest,
    ) -> Result<PolicyReplacementResponse, ErrorResponse> {
        self.0.replace_policy(request).await
    }

    async fn evaluate(&self, request: PackageRequest) -> Result<EvaluationResponse, ErrorResponse> {
        self.0.evaluate(request).await
    }

    async fn execute(&self, request: PackageRequest) -> Result<ExecutionResponse, ErrorResponse> {
        self.0.execute(request).await
    }

    async fn status(&self, request: StatusRequest) -> Result<StatusResponse, ErrorResponse> {
        self.0.status(request).await
    }

    async fn cancel(&self, request: CancelRequest) -> Result<CancelResponse, ErrorResponse> {
        self.0.cancel(request).await
    }
}
