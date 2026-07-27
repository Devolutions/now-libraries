//! Broker protocol enumerations.

use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

/// Package operation type.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema, strum::Display)]
#[schemars(rename = "Operation")]
pub enum Operation {
    Install,
    Update,
    Uninstall,
}

/// Package installation scope.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema, strum::Display)]
#[schemars(rename = "Scope")]
pub enum Scope {
    User,
    Machine,
}

/// Target architecture.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema, strum::Display)]
#[schemars(rename = "Architecture")]
pub enum Architecture {
    X86,
    X64,
    Arm64,
    Neutral,
}

/// Supported package manager names.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema, strum::Display)]
#[schemars(rename = "ManagerName")]
pub enum ManagerName {
    Winget,
    PowerShell,
    PowerShell7,
    Apt,
    Bun,
    Cargo,
    Chocolatey,
    Dnf,
    Dotnet,
    Flatpak,
    Homebrew,
    Npm,
    Pacman,
    Pip,
    Scoop,
    Snap,
    Vcpkg,
}

impl ManagerName {
    /// All known package manager names.
    pub const ALL: &[Self] = &[
        Self::Winget,
        Self::PowerShell,
        Self::PowerShell7,
        Self::Apt,
        Self::Bun,
        Self::Cargo,
        Self::Chocolatey,
        Self::Dnf,
        Self::Dotnet,
        Self::Flatpak,
        Self::Homebrew,
        Self::Npm,
        Self::Pacman,
        Self::Pip,
        Self::Scoop,
        Self::Snap,
        Self::Vcpkg,
    ];

    /// Minimum broker API version that understands this manager name.
    ///
    /// Clients must not send a manager to a broker whose advertised API
    /// version is older than this value: the older broker would reject the
    /// unknown enum value as a validation failure instead of reporting a
    /// meaningful capability error.
    pub fn minimum_api_version(self) -> crate::ApiVersion {
        match self {
            Self::Winget | Self::PowerShell | Self::PowerShell7 => crate::ApiVersion::from("1.0"),
            Self::Apt
            | Self::Bun
            | Self::Cargo
            | Self::Chocolatey
            | Self::Dnf
            | Self::Dotnet
            | Self::Flatpak
            | Self::Homebrew
            | Self::Npm
            | Self::Pacman
            | Self::Pip
            | Self::Scoop
            | Self::Snap
            | Self::Vcpkg => crate::ApiVersion::from("1.1"),
        }
    }
}

/// Policy decision.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema, strum::Display)]
#[schemars(rename = "Decision")]
pub enum Decision {
    Allow,
    Deny,
}

/// Requested elevation level.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema, strum::Display)]
#[schemars(rename = "Elevation")]
pub enum Elevation {
    Standard,
    Elevated,
}

/// Broker transport type.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "Transport")]
pub enum Transport {
    HttpNamedPipe,
    HttpLoopbackSimulator,
}

/// Status of an asynchronous package operation.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "OperationStatus")]
pub enum OperationStatus {
    /// Process is being prepared/started.
    Starting,
    /// Process is running.
    Running,
    /// Process exited successfully (exit code 0).
    Completed,
    /// Process failed (non-zero exit, timeout, or launch failure).
    Failed,
}

/// Structured machine-readable error code.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "ErrorCode")]
pub enum ErrorCode {
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PayloadTooLarge,
    UnsupportedMediaType,
    ValidationFailed,
    BrokerPaused,
    InternalError,
    Timeout,
}
