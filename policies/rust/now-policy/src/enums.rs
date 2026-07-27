//! Policy-domain enumerations shared with broker requests.

use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

/// Package operation type.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "Operation")]
pub enum Operation {
    Install,
    Update,
    Uninstall,
}

/// Package installation scope.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "Scope")]
pub enum Scope {
    User,
    Machine,
}

/// Target architecture.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "Architecture")]
pub enum Architecture {
    X86,
    X64,
    Arm64,
    Neutral,
}

/// Supported package manager names.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema)]
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

/// Policy decision.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "Decision")]
pub enum Decision {
    Allow,
    Deny,
}

/// Requested elevation level.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "Elevation")]
pub enum Elevation {
    Standard,
    Elevated,
}

impl std::fmt::Display for Decision {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Allow => f.write_str("Allow"),
            Self::Deny => f.write_str("Deny"),
        }
    }
}

impl std::fmt::Display for Operation {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Install => f.write_str("Install"),
            Self::Update => f.write_str("Update"),
            Self::Uninstall => f.write_str("Uninstall"),
        }
    }
}

impl std::fmt::Display for ManagerName {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Winget => f.write_str("Winget"),
            Self::PowerShell => f.write_str("PowerShell"),
            Self::PowerShell7 => f.write_str("PowerShell7"),
            Self::Apt => f.write_str("Apt"),
            Self::Bun => f.write_str("Bun"),
            Self::Cargo => f.write_str("Cargo"),
            Self::Chocolatey => f.write_str("Chocolatey"),
            Self::Dnf => f.write_str("Dnf"),
            Self::Dotnet => f.write_str("Dotnet"),
            Self::Flatpak => f.write_str("Flatpak"),
            Self::Homebrew => f.write_str("Homebrew"),
            Self::Npm => f.write_str("Npm"),
            Self::Pacman => f.write_str("Pacman"),
            Self::Pip => f.write_str("Pip"),
            Self::Scoop => f.write_str("Scoop"),
            Self::Snap => f.write_str("Snap"),
            Self::Vcpkg => f.write_str("Vcpkg"),
        }
    }
}

impl std::fmt::Display for Scope {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::User => f.write_str("User"),
            Self::Machine => f.write_str("Machine"),
        }
    }
}

impl std::fmt::Display for Elevation {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Standard => f.write_str("Standard"),
            Self::Elevated => f.write_str("Elevated"),
        }
    }
}

impl std::fmt::Display for Architecture {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::X86 => f.write_str("X86"),
            Self::X64 => f.write_str("X64"),
            Self::Arm64 => f.write_str("Arm64"),
            Self::Neutral => f.write_str("Neutral"),
        }
    }
}
