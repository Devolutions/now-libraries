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
