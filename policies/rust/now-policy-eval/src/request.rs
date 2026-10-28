//! The request as the client sends it ([§request-input]).
//!
//! Enumerated parts are strings, because a value outside the contract's set makes a request unreadable rather than unrepresentable ([§reading]).
//!
//! [§request-input]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#request-input
//! [§reading]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#reading

/// A request to install, update or uninstall one package through one manager.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Request {
    /// `Install`, `Update` or `Uninstall`.
    pub operation: String,
    /// A manager identifier, such as `Winget`.
    pub manager: String,
    pub source: Source,
    pub package: Package,
    pub options: Options,
    pub client: Client,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Source {
    pub name: String,
    pub url: Option<String>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Package {
    pub id: String,
    pub version: Option<String>,
    /// `X86`, `X64`, `Arm64` or `Neutral`.
    pub architecture: Option<String>,
    pub channel: Option<String>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Options {
    /// `User` or `Machine`.
    pub scope: Option<String>,
    pub interactive: bool,
    pub skip_hash_check: bool,
    /// Has no effect on any decision, because the command does not carry it.
    pub pre_release: bool,
    pub uninstall_previous: bool,
    pub no_upgrade: bool,
    pub custom_install_location: Option<String>,
    pub custom_parameters: Vec<String>,
    pub pre_operation_command: Option<String>,
    pub post_operation_command: Option<String>,
    pub kill_before_operation: Vec<String>,
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Client {
    /// `Standard` or `Elevated`.
    pub requested_elevation: String,
}
