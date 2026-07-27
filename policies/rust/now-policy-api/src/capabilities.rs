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

/// Result of checking whether a package manager can be used with a broker,
/// distinguishing protocol-version gaps from missing capability advertisement.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ManagerSupport {
    /// The broker advertises a capability entry for the manager.
    Supported,
    /// The broker's API version predates the manager; sending it would fail
    /// request validation on the broker. `required` is the minimum broker API
    /// version that understands the manager.
    RequiresNewerApiVersion { required: ApiVersion },
    /// The broker speaks a recent-enough API version but does not advertise
    /// the manager in its capabilities.
    NotAdvertised,
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

    /// Classify support for `manager`, distinguishing a broker that is too old
    /// to understand the manager from one that merely does not advertise it.
    pub fn manager_support(&self, manager: ManagerName) -> ManagerSupport {
        let required = manager.minimum_api_version();
        if !self.response_version.supports(&required) {
            return ManagerSupport::RequiresNewerApiVersion { required };
        }

        if self.supports_manager(manager) {
            ManagerSupport::Supported
        } else {
            ManagerSupport::NotAdvertised
        }
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
                server_version: "0.2.0".to_owned(),
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
    fn managers_added_in_1_1_are_version_gated_behind_older_brokers() {
        let old_broker = capabilities("1.0", vec![manager_capability(ManagerName::Winget)]);

        assert_eq!(
            old_broker.manager_support(ManagerName::Winget),
            ManagerSupport::Supported
        );
        assert_eq!(
            old_broker.manager_support(ManagerName::Chocolatey),
            ManagerSupport::RequiresNewerApiVersion { required: "1.1".into() }
        );
        // Version gap is reported even if an old broker somehow advertised the manager.
        assert!(!old_broker.supports_manager(ManagerName::Chocolatey));
    }

    #[test]
    fn current_broker_distinguishes_not_advertised_from_version_gap() {
        let broker = capabilities(API_VERSION_STR, vec![manager_capability(ManagerName::Chocolatey)]);

        assert_eq!(
            broker.manager_support(ManagerName::Chocolatey),
            ManagerSupport::Supported
        );
        assert_eq!(
            broker.manager_support(ManagerName::Scoop),
            ManagerSupport::NotAdvertised
        );
        assert_eq!(
            broker.manager_support(ManagerName::Winget),
            ManagerSupport::NotAdvertised
        );
    }

    #[test]
    fn all_managers_have_a_valid_minimum_api_version() {
        for &manager in ManagerName::ALL {
            let required = manager.minimum_api_version();
            assert!(required.major_minor().is_some(), "{manager} minimum version must parse");
            assert!(
                ApiVersion::from(API_VERSION_STR).supports(&required),
                "{manager} must be usable with the current API version"
            );
        }
    }

    #[test]
    fn api_version_supports_compares_major_and_minor() {
        let v1_1 = ApiVersion::from("1.1");
        assert!(v1_1.supports(&"1.0".into()));
        assert!(v1_1.supports(&"1.1".into()));
        assert!(!v1_1.supports(&"1.2".into()));
        assert!(!v1_1.supports(&"2.0".into()));
        assert!(!ApiVersion::from("2.0").supports(&"1.1".into()));
    }
}
