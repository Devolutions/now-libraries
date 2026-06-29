//! Health endpoint models.

use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::api::ServerContext;
use super::{ApiVersion, HealthResponseKind};

/// Broker readiness state.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "HealthStatus")]
pub enum HealthStatus {
    /// Broker has a valid policy and is serving requests.
    Ready,
    /// Broker is paused (policy file missing or corrupted).
    Paused,
}

/// Response body for `GET /v1/health`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "HealthResponse")]
#[serde(rename_all = "PascalCase")]
pub struct HealthResponse {
    /// Response discriminator.
    pub response_kind: HealthResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Whether the broker is ready or paused.
    pub status: HealthStatus,

    /// Identifier of the active policy (empty when paused).
    pub policy_id: String,
}
