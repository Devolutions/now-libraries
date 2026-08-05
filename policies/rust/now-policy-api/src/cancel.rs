//! Operation cancelation request and response models.

use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::api::{ClientContext, ServerContext};
use super::enums::OperationStatus;
use super::{ApiVersion, CancelRequestKind, CancelResponseKind, ResourceId};

/// Request body for canceling a previously submitted operation.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "CancelRequest")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct CancelRequest {
    /// Request discriminator.
    pub request_kind: CancelRequestKind,

    /// Client-side API version used to construct the request.
    pub request_version: ApiVersion,

    /// Server-issued stable operation identifier.
    pub operation_id: ResourceId,

    /// Client context used to authenticate the cancel request.
    pub client: ClientContext,
}

/// Response to a cancel request.
///
/// Cancelation is asynchronous and idempotent: the broker acknowledges the request by
/// moving a non-terminal operation to `Canceling` and reports the resulting status.
/// Clients should poll the status endpoint until the operation reaches a terminal
/// status (`Canceled`, or `Completed`/`Failed` when the process ends first).
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "CancelResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct CancelResponse {
    /// Response discriminator.
    pub response_kind: CancelResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Server-issued stable operation identifier.
    pub operation_id: ResourceId,

    /// The original request id associated with the operation.
    pub request_id: ResourceId,

    /// Status of the operation after the cancel request was applied.
    ///
    /// `Canceling` when the cancelation was accepted for an in-flight operation;
    /// the terminal status when the operation already finished.
    pub status: OperationStatus,

    /// Human-readable message about the cancelation outcome.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 2048))]
    pub message: Option<String>,
}
