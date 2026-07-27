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

impl CapabilitiesResponse {
    /// Capability entry advertised for `manager`, if any.
    pub fn manager_capability(&self, manager: ManagerName) -> Option<&ManagerCapability> {
        self.managers.iter().find(|capability| capability.manager == manager)
    }

    /// Whether the broker advertises support for `manager`.
    pub fn supports_manager(&self, manager: ManagerName) -> bool {
        self.manager_capability(manager).is_some()
    }
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{API_VERSION_STR, ServerContext, Transport};

    fn capabilities(response_version: &str, managers: Vec<ManagerCapability>) -> CapabilitiesResponse {
        CapabilitiesResponse {
            response_kind: CapabilitiesResponseKind,
            response_version: response_version.into(),
            server: ServerContext {
                server_version: "0.1.0".to_owned(),
                transport: Transport::HttpNamedPipe,
            },
            transports: vec![Transport::HttpNamedPipe],
            managers,
            max_request_body_bytes: 262144,
        }
    }

    fn manager_capability(manager: ManagerName) -> ManagerCapability {
        ManagerCapability {
            manager,
            operations: vec![Operation::Install],
            scopes: vec![Scope::Machine],
            architectures: vec![Architecture::Neutral],
            supports_custom_parameters: false,
            supports_custom_install_location: false,
            supports_capture_output: false,
            supports_details: false,
            max_operation_timeout_seconds: None,
        }
    }

    #[test]
    fn supports_manager_reflects_advertised_capabilities() {
        let broker = capabilities(
            API_VERSION_STR,
            vec![
                manager_capability(ManagerName::Winget),
                manager_capability(ManagerName::Chocolatey),
            ],
        );

        assert!(broker.supports_manager(ManagerName::Winget));
        assert!(broker.supports_manager(ManagerName::Chocolatey));
        assert!(!broker.supports_manager(ManagerName::Scoop));
        assert!(broker.manager_capability(ManagerName::Scoop).is_none());
        assert_eq!(
            broker
                .manager_capability(ManagerName::Chocolatey)
                .map(|capability| capability.manager),
            Some(ManagerName::Chocolatey)
        );
    }
}
