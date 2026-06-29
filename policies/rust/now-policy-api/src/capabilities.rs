//! Capabilities endpoint models.

use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

use super::api::ServerContext;
use super::enums::{Architecture, ManagerName, Operation, Scope, Transport};
use super::{ApiVersion, CapabilitiesResponseKind};

/// Response body for `GET /v1/capabilities`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "CapabilitiesResponse")]
#[serde(rename_all = "PascalCase")]
pub struct CapabilitiesResponse {
    /// Response discriminator.
    pub response_kind: CapabilitiesResponseKind,

    /// Server-side API version used to construct the response.
    pub response_version: ApiVersion,

    /// Server context.
    pub server: ServerContext,

    /// Supported transports.
    pub transports: Vec<Transport>,

    /// Package-manager-specific capabilities.
    pub managers: Vec<ManagerCapability>,

    /// Maximum accepted request body size, in bytes.
    pub max_request_body_bytes: u64,
}

/// Package-manager-specific capability declaration.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ManagerCapability")]
#[serde(rename_all = "PascalCase")]
pub struct ManagerCapability {
    /// Package manager name.
    pub manager: ManagerName,

    /// Operations supported for this manager.
    pub operations: Vec<Operation>,

    /// Installation scopes supported for this manager.
    pub scopes: Vec<Scope>,

    /// Architectures supported for this manager.
    pub architectures: Vec<Architecture>,

    /// Whether arbitrary custom command-line parameters are supported.
    pub supports_custom_parameters: bool,

    /// Whether a custom install location is supported.
    pub supports_custom_install_location: bool,

    /// Whether operation output capture is supported.
    pub supports_capture_output: bool,

    /// Whether operation status may include manager-specific JSON details.
    pub supports_details: bool,

    /// Maximum operation runtime before the broker may time out the process.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub max_operation_timeout_seconds: Option<u64>,
}
