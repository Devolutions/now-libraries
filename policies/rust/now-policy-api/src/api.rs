//! Shared package broker API models.

use chrono::{DateTime, Utc};
use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::enums::{Architecture, Decision, Elevation, ErrorCode, ManagerName, Operation, Scope, Transport};
use super::{
    ApiVersion, CommandString, CustomParameterString, ErrorResponseKind, PackageIdentifier, PackageRequestKind,
    ProcessName, ResourceId, RuleId, SemanticVersion, VersionString,
};

/// Canonical request sent by a package broker client to the elevated broker.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PackageRequest")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PackageRequest {
    /// Request discriminator.
    pub request_kind: PackageRequestKind,

    /// Client-side API version used to construct the request.
    pub request_version: ApiVersion,

    /// Unique client-generated request id for request/response correlation.
    ///
    /// Execute requests are idempotent by this identifier: retrying the same
    /// request id must return the existing submission rather than creating a
    /// second operation.
    pub request_id: ResourceId,

    /// UTC timestamp when the client created the request (RFC 3339).
    pub created_at: DateTime<Utc>,

    /// The package operation to perform.
    pub operation: Operation,

    /// Package manager type.
    pub manager: ManagerName,

    /// Source/repository information.
    pub source: RequestSource,

    /// Package information.
    pub package: RequestPackage,

    /// Operation options.
    pub options: RequestOptions,

    /// Client context.
    pub client: ClientContext,

    /// When true, evaluation and execution responses may include a command preview for diagnostics.
    /// Off by default because command previews can expose paths or arguments.
    #[serde(default)]
    pub include_command_preview: bool,

    /// When true, the broker captures the operation's combined stdout+stderr and returns
    /// it (tail-truncated) in the status response. Off by default to avoid the overhead
    /// when the client does not need the output.
    #[serde(default)]
    pub capture_output: bool,
}

/// Package source/repository information.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "RequestSource")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct RequestSource {
    /// Source name.
    #[schemars(length(min = 1, max = 128))]
    pub name: String,

    /// Optional source URL.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 2048))]
    pub url: Option<String>,
}

/// Package information.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "RequestPackage")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct RequestPackage {
    /// Package identifier (e.g., "Publisher.Package" for WinGet).
    pub id: PackageIdentifier,

    /// Target version (for update/install operations).
    ///
    /// A lenient version string rather than strict SemVer: real package versions
    /// are frequently not SemVer (e.g. PowerShell modules use 4-part .NET versions
    /// like `5.6.0.0`, and some winget packages use 2-part or date-based versions).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub version: Option<VersionString>,

    /// Target architecture.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub architecture: Option<Architecture>,

    /// Release channel.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(min = 1, max = 16))]
    pub channel: Option<String>,
}

/// Options controlling the package operation.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "RequestOptions")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct RequestOptions {
    /// Installation scope.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub scope: Option<Scope>,

    /// Run interactively (show installer UI).
    pub interactive: bool,

    /// Skip package hash verification.
    pub skip_hash_check: bool,

    /// Allow pre-release versions.
    pub pre_release: bool,

    /// Custom install directory path.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 2048))]
    pub custom_install_location: Option<String>,

    /// Additional command-line parameters.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[schemars(length(max = 64))]
    pub custom_parameters: Vec<CustomParameterString>,

    /// Command to execute before the package operation.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 2048))]
    pub pre_operation_command: Option<String>,

    /// Command to execute after the package operation.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 2048))]
    pub post_operation_command: Option<String>,

    /// Processes to kill before running the operation.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[schemars(length(max = 64))]
    pub kill_before_operation: Vec<ProcessName>,

    /// Whether to uninstall previous version before installing update.
    #[serde(default)]
    pub uninstall_previous: bool,

    /// Whether to skip upgrade if an existing version is detected (for install operations).
    #[serde(default)]
    pub no_upgrade: bool,
}

/// Context provided by the client.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ClientContext")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct ClientContext {
    /// Transport used by the client to access the broker.
    pub transport: Transport,

    /// Elevation level requested.
    pub requested_elevation: Elevation,

    /// Windows identity of the calling user.
    #[schemars(length(min = 1, max = 256))]
    pub effective_user: String,

    /// File path of the client executable authenticated by the broker.
    #[schemars(length(min = 1, max = 2048))]
    pub client_executable_path: String,

    /// Version of the package broker client binary.
    #[schemars(length(min = 1, max = 128))]
    pub client_version: String,
}

/// Server context included in responses.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ServerContext")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct ServerContext {
    /// Version of the package broker server binary.
    #[schemars(length(min = 1, max = 128))]
    pub server_version: String,

    /// Transport mechanism.
    pub transport: Transport,
}

/// Parsed request summary included in decision responses.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "RequestSummary")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct RequestSummary {
    /// Manager from the request (null if not parsed).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub manager: Option<ManagerName>,

    /// Source name from the request (null if not parsed).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(min = 1, max = 256))]
    pub source: Option<String>,

    /// Package identifier from the request (null if not parsed).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub package_id: Option<PackageIdentifier>,

    /// Operation from the request (null if not parsed).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub operation: Option<Operation>,
}

/// Policy decision information.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "DecisionInfo")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct DecisionInfo {
    /// The evaluation decision.
    pub decision: Decision,

    /// The rule that produced the decision.
    pub rule_id: RuleId,

    /// Human-readable reason for the decision.
    #[schemars(length(min = 1, max = 2048))]
    pub reason: String,
}

/// Summary of policy used for the decision.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ResponsePolicyInfo")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct ResponsePolicyInfo {
    /// Policy document identifier.
    pub id: ResourceId,

    /// Policy revision number.
    #[schemars(range(min = 1, max = 2147483647))]
    pub revision: u32,

    /// Policy syntax version.
    pub policy_version: SemanticVersion,
}

/// Optional operation diagnostics.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "OperationDiagnostics")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct OperationDiagnostics {
    /// Command that would be executed. Only present when requested by the client.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[schemars(length(max = 256))]
    pub command_preview: Vec<CommandString>,
}

/// Structured error detail, typically used for validation failures.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ErrorDetail")]
#[serde(rename_all = "PascalCase")]
pub struct ErrorDetail {
    /// Machine-stable detail code.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(min = 1, max = 128))]
    pub code: Option<String>,

    /// JSON pointer, header name, or other location for the error.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(min = 1, max = 512))]
    pub path: Option<String>,

    /// Human-readable detail message.
    #[schemars(length(min = 1, max = 2048))]
    pub message: String,
}

/// Generic error body returned for non-2xx responses.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ErrorResponse")]
#[serde(rename_all = "PascalCase")]
pub struct ErrorResponse {
    /// Response discriminator.
    pub response_kind: ErrorResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Machine-readable error code.
    pub code: ErrorCode,

    /// Human-readable summary.
    #[schemars(length(min = 1, max = 2048))]
    pub message: String,

    /// Structured error details.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub details: Vec<ErrorDetail>,
}
