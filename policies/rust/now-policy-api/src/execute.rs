//! Package operation execution models.
//!
//! Execution uses the shared [`PackageRequest`](crate::PackageRequest) request model.

use chrono::{DateTime, Utc};
use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::api::{DecisionInfo, OperationDiagnostics, RequestSummary, ResponsePolicyInfo, ServerContext};
use super::enums::OperationStatus;
use super::{ApiVersion, ExecutionResponseKind, ResourceId};

/// Response returned after an execute request is evaluated and, when allowed, submitted.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ExecutionResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct ExecutionResponse {
    /// Response discriminator.
    pub response_kind: ExecutionResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Echoed request id.
    pub request_id: ResourceId,

    /// UTC timestamp when broker received the request (RFC 3339).
    pub received_at: DateTime<Utc>,

    /// UTC timestamp when broker completed evaluation/submission (RFC 3339).
    pub completed_at: DateTime<Utc>,

    /// Parsed request summary.
    pub request: RequestSummary,

    /// Policy decision details.
    pub decision: DecisionInfo,

    /// Summary of the policy used.
    pub policy: ResponsePolicyInfo,

    /// Submitted operation. Omitted when the decision is deny.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub operation: Option<OperationSubmission>,

    /// Optional diagnostics. Command preview is omitted unless explicitly requested.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub diagnostics: Option<OperationDiagnostics>,
}

/// Execution submission returned for allowed execute requests.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "OperationSubmission")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct OperationSubmission {
    /// Server-issued stable operation identifier.
    pub operation_id: ResourceId,

    /// Initial operation status.
    pub status: OperationStatus,

    /// UTC timestamp when the operation was accepted.
    pub submitted_at: DateTime<Utc>,
}
