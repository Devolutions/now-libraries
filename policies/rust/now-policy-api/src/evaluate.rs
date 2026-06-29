//! Package operation evaluation models.
//!
//! Evaluation uses the shared [`PackageRequest`](crate::PackageRequest) request model.

use chrono::{DateTime, Utc};
use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::api::{DecisionInfo, OperationDiagnostics, RequestSummary, ResponsePolicyInfo, ServerContext};
use super::{ApiVersion, EvaluationResponseKind, ResourceId};

/// Canonical response returned by the broker after evaluating a request.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "EvaluationResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct EvaluationResponse {
    /// Response discriminator.
    pub response_kind: EvaluationResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Echoed request id.
    pub request_id: ResourceId,

    /// UTC timestamp when broker received the request (RFC 3339).
    pub received_at: DateTime<Utc>,

    /// UTC timestamp when broker completed evaluation (RFC 3339).
    pub completed_at: DateTime<Utc>,

    /// Parsed request summary.
    pub request: RequestSummary,

    /// Policy decision details.
    pub decision: DecisionInfo,

    /// Whether the broker would execute a command for this decision.
    pub would_execute: bool,

    /// Summary of the policy used.
    pub policy: ResponsePolicyInfo,

    /// Optional diagnostics. Command preview is omitted unless explicitly requested.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub diagnostics: Option<OperationDiagnostics>,
}
