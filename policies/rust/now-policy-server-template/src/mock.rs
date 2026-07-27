//! Fixture-backed mock implementation of the package broker server facade.

use std::collections::BTreeMap;

use async_trait::async_trait;

use crate::server::{MAX_REQUEST_BODY_BYTES, PackageBrokerServer};
use now_policy_api::{
    API_VERSION_STR, Architecture, CapabilitiesResponse, CapabilitiesResponseKind, ErrorCode, ErrorResponse,
    ErrorResponseKind, EvaluationResponse, ExecutionResponse, HealthResponse, HealthResponseKind, HealthStatus,
    ManagerCapability, ManagerName, Operation, PackageRequest, Scope, ServerContext, StatusRequest, StatusResponse,
    Transport,
};

/// Deterministic mock broker backed by caller-provided sample responses.
#[derive(Debug, Clone)]
pub struct MockPackageBrokerServer {
    health: HealthResponse,
    capabilities: CapabilitiesResponse,
    evaluation_responses: BTreeMap<String, EvaluationResponse>,
    execution_responses: BTreeMap<String, ExecutionResponse>,
    status_responses: BTreeMap<String, StatusResponse>,
}

impl MockPackageBrokerServer {
    pub fn new(_pipe_name: impl Into<String>) -> Self {
        let server = server_context();
        Self {
            health: HealthResponse {
                response_kind: HealthResponseKind,
                response_version: API_VERSION_STR.into(),
                server: server.clone(),
                status: HealthStatus::Ready,
                policy_id: "mock.policy".to_owned(),
            },
            capabilities: CapabilitiesResponse {
                response_kind: CapabilitiesResponseKind,
                response_version: API_VERSION_STR.into(),
                server,
                transports: vec![Transport::HttpNamedPipe],
                managers: default_manager_capabilities(),
                max_request_body_bytes: MAX_REQUEST_BODY_BYTES as u64,
            },
            evaluation_responses: BTreeMap::new(),
            execution_responses: BTreeMap::new(),
            status_responses: BTreeMap::new(),
        }
    }

    #[must_use]
    pub fn with_evaluation_response(mut self, response: EvaluationResponse) -> Self {
        self.evaluation_responses
            .insert(response.request_id.to_string(), response);
        self
    }

    #[must_use]
    pub fn with_execution_response(mut self, response: ExecutionResponse) -> Self {
        self.execution_responses
            .insert(response.request_id.to_string(), response);
        self
    }

    #[must_use]
    pub fn with_status_response(mut self, response: StatusResponse) -> Self {
        self.status_responses
            .insert(response.operation_id.to_string(), response);
        self
    }

    fn missing_response(&self, id: &str) -> ErrorResponse {
        ErrorResponse {
            response_kind: ErrorResponseKind,
            response_version: API_VERSION_STR.into(),
            server: self.capabilities.server.clone(),
            code: ErrorCode::NotFound,
            message: format!("no mock response registered for '{id}'"),
            details: Vec::new(),
        }
    }
}

#[async_trait]
impl PackageBrokerServer for MockPackageBrokerServer {
    async fn health(&self) -> HealthResponse {
        self.health.clone()
    }

    async fn capabilities(&self) -> CapabilitiesResponse {
        self.capabilities.clone()
    }

    async fn evaluate(&self, request: PackageRequest) -> Result<EvaluationResponse, ErrorResponse> {
        self.evaluation_responses
            .get(&request.request_id.to_string())
            .cloned()
            .ok_or_else(|| self.missing_response(&request.request_id))
    }

    async fn execute(&self, request: PackageRequest) -> Result<ExecutionResponse, ErrorResponse> {
        self.execution_responses
            .get(&request.request_id.to_string())
            .cloned()
            .ok_or_else(|| self.missing_response(&request.request_id))
    }

    async fn status(&self, request: StatusRequest) -> Result<StatusResponse, ErrorResponse> {
        self.status_responses
            .get(&request.operation_id.to_string())
            .cloned()
            .ok_or_else(|| self.missing_response(&request.operation_id))
    }
}

fn server_context() -> ServerContext {
    ServerContext {
        server_version: env!("CARGO_PKG_VERSION").to_owned(),
        transport: Transport::HttpNamedPipe,
    }
}

fn default_manager_capabilities() -> Vec<ManagerCapability> {
    fn manager(
        manager: ManagerName,
        scopes: Vec<Scope>,
        architectures: Vec<Architecture>,
        supports_custom_install_location: bool,
        supports_details: bool,
    ) -> ManagerCapability {
        ManagerCapability {
            manager,
            operations: vec![Operation::Install, Operation::Update, Operation::Uninstall],
            scopes,
            architectures,
            supports_custom_parameters: true,
            supports_custom_install_location,
            supports_capture_output: true,
            supports_details,
            max_operation_timeout_seconds: Some(1800),
        }
    }

    let all_architectures = || {
        vec![
            Architecture::X86,
            Architecture::X64,
            Architecture::Arm64,
            Architecture::Neutral,
        ]
    };

    vec![
        manager(
            ManagerName::Winget,
            vec![Scope::User, Scope::Machine],
            all_architectures(),
            true,
            true,
        ),
        manager(
            ManagerName::PowerShell,
            vec![Scope::User, Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::PowerShell7,
            vec![Scope::User, Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Apt,
            vec![Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Bun,
            vec![Scope::User],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Cargo,
            vec![Scope::User],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Chocolatey,
            vec![Scope::Machine],
            all_architectures(),
            true,
            false,
        ),
        manager(
            ManagerName::Dnf,
            vec![Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Dotnet,
            vec![Scope::User, Scope::Machine],
            vec![Architecture::Neutral],
            true,
            false,
        ),
        manager(
            ManagerName::Flatpak,
            vec![Scope::User, Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Homebrew,
            vec![Scope::User],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Npm,
            vec![Scope::User, Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Pacman,
            vec![Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Pip,
            vec![Scope::User, Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Scoop,
            vec![Scope::User, Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(
            ManagerName::Snap,
            vec![Scope::Machine],
            vec![Architecture::Neutral],
            false,
            false,
        ),
        manager(ManagerName::Vcpkg, vec![Scope::User], all_architectures(), false, false),
    ]
}
