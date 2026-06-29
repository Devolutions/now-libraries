//! Status query request and response models.

use chrono::{DateTime, Utc};
use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::api::{ClientContext, ServerContext};
use super::enums::OperationStatus;
use super::{ApiVersion, Base64Utf8Data, ResourceId, StatusRequestKind, StatusResponseKind};

/// Request body for querying an operation status.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "StatusRequest")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct StatusRequest {
    /// Request discriminator.
    pub request_kind: StatusRequestKind,

    /// Client-side API version used to construct the request.
    pub request_version: ApiVersion,

    /// Server-issued stable operation identifier.
    pub operation_id: ResourceId,

    /// Client context used to authenticate the status query.
    pub client: ClientContext,
}

/// Response to a status query.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "StatusResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct StatusResponse {
    /// Response discriminator.
    pub response_kind: StatusResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Server-issued stable operation identifier.
    pub operation_id: ResourceId,

    /// The original request id associated with the operation.
    pub request_id: ResourceId,

    /// Current status of the operation.
    pub status: OperationStatus,

    /// UTC timestamp when the process was actually launched (null if not yet started).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub started_at: Option<DateTime<Utc>>,

    /// UTC timestamp when the operation completed or failed (null if still running).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub completed_at: Option<DateTime<Utc>>,

    /// Process exit code (present when status is `completed`, or `failed` due to non-zero exit).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub exit_code: Option<i32>,

    /// Human-readable message about the status. For failures this carries the short error
    /// summary (e.g. "winget.exe exited with code 0x8A150011", or a process-launch error).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 2048))]
    pub message: Option<String>,

    /// Manager-specific structured status details.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub details: Option<serde_json::Value>,

    /// Captured combined stdout+stderr as base64-encoded UTF-8 data (tail-truncated to ~10 KiB before encoding).
    /// Only present when the original request opted in via `CaptureOutput`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub stdout: Option<Base64Utf8Data>,
}
