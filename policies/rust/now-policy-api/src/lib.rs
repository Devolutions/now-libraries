//! Data models for the Devolutions NOW package broker API.

use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

pub mod api;
pub mod cancel;
pub mod capabilities;
pub mod enums;
pub mod evaluate;
pub mod event_channel;
pub mod execute;
pub mod health;
pub mod management;
pub mod policy;
pub mod status;

pub use api::*;
pub use cancel::*;
pub use capabilities::*;
pub use enums::*;
pub use evaluate::*;
pub use event_channel::*;
pub use execute::*;
pub use health::*;
pub use management::*;
pub use policy::*;
pub use status::*;

pub const API_VERSION_STR: &str = "1.0";
pub const DEFAULT_PIPE_NAME: &str = "Devolutions.Now.PackageBroker.v1";

pub const PACKAGE_REQUEST_KIND: &str = "PackageRequest";
pub const STATUS_REQUEST_KIND: &str = "StatusRequest";
pub const CANCEL_REQUEST_KIND: &str = "CancelRequest";
pub const POLICY_VALIDATION_REQUEST_KIND: &str = "PolicyValidationRequest";
pub const POLICY_REPLACEMENT_REQUEST_KIND: &str = "PolicyReplacementRequest";

pub const HEALTH_RESPONSE_KIND: &str = "HealthResponse";
pub const CAPABILITIES_RESPONSE_KIND: &str = "CapabilitiesResponse";
pub const EVALUATION_RESPONSE_KIND: &str = "EvaluationResponse";
pub const EXECUTION_RESPONSE_KIND: &str = "ExecutionResponse";
pub const STATUS_RESPONSE_KIND: &str = "StatusResponse";
pub const CANCEL_RESPONSE_KIND: &str = "CancelResponse";
pub const POLICY_RESPONSE_KIND: &str = "PolicyResponse";
pub const POLICY_MANAGEMENT_RESPONSE_KIND: &str = "PolicyManagementResponse";
pub const POLICY_VALIDATION_RESPONSE_KIND: &str = "PolicyValidationResponse";
pub const POLICY_REPLACEMENT_RESPONSE_KIND: &str = "PolicyReplacementResponse";
pub const ERROR_RESPONSE_KIND: &str = "ErrorResponse";

macro_rules! fixed_string_marker {
    ($name:ident, $value:expr) => {
        #[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
        pub struct $name;

        impl Serialize for $name {
            fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
            where
                S: serde::Serializer,
            {
                serializer.serialize_str($value)
            }
        }

        impl<'de> Deserialize<'de> for $name {
            fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
            where
                D: serde::Deserializer<'de>,
            {
                let value = String::deserialize(deserializer)?;

                if value == $value {
                    Ok(Self)
                } else {
                    Err(serde::de::Error::custom(format!(
                        "expected {}, got {value}",
                        $value
                    )))
                }
            }
        }

        impl JsonSchema for $name {
            fn schema_name() -> std::borrow::Cow<'static, str> {
                stringify!($name).into()
            }

            fn json_schema(_gen: &mut schemars::SchemaGenerator) -> schemars::Schema {
                schemars::json_schema!({
                    "type": "string",
                    "pattern": format!("^{}$", $value),
                })
            }
        }
    };
}

fixed_string_marker!(PackageRequestKind, PACKAGE_REQUEST_KIND);
fixed_string_marker!(StatusRequestKind, STATUS_REQUEST_KIND);
fixed_string_marker!(CancelRequestKind, CANCEL_REQUEST_KIND);
fixed_string_marker!(PolicyValidationRequestKind, POLICY_VALIDATION_REQUEST_KIND);
fixed_string_marker!(PolicyReplacementRequestKind, POLICY_REPLACEMENT_REQUEST_KIND);
fixed_string_marker!(HealthResponseKind, HEALTH_RESPONSE_KIND);
fixed_string_marker!(CapabilitiesResponseKind, CAPABILITIES_RESPONSE_KIND);
fixed_string_marker!(EvaluationResponseKind, EVALUATION_RESPONSE_KIND);
fixed_string_marker!(ExecutionResponseKind, EXECUTION_RESPONSE_KIND);
fixed_string_marker!(StatusResponseKind, STATUS_RESPONSE_KIND);
fixed_string_marker!(CancelResponseKind, CANCEL_RESPONSE_KIND);
fixed_string_marker!(PolicyResponseKind, POLICY_RESPONSE_KIND);
fixed_string_marker!(PolicyManagementResponseKind, POLICY_MANAGEMENT_RESPONSE_KIND);
fixed_string_marker!(PolicyValidationResponseKind, POLICY_VALIDATION_RESPONSE_KIND);
fixed_string_marker!(PolicyReplacementResponseKind, POLICY_REPLACEMENT_RESPONSE_KIND);
fixed_string_marker!(ErrorResponseKind, ERROR_RESPONSE_KIND);

/// Error returned when a broker protocol newtype fails deserialization validation.
#[derive(Debug, thiserror::Error)]
pub enum ModelValidationError {
    #[error("{type_name}: {reason}")]
    Invalid { type_name: &'static str, reason: String },
}

fn validate_bounded_string(
    s: &str,
    min: usize,
    max: usize,
    type_name: &'static str,
) -> Result<(), ModelValidationError> {
    if s.len() < min {
        return Err(ModelValidationError::Invalid {
            type_name,
            reason: format!("length {} is below minimum {min}", s.len()),
        });
    }

    if s.len() > max {
        return Err(ModelValidationError::Invalid {
            type_name,
            reason: format!("length {} exceeds maximum {max}", s.len()),
        });
    }

    Ok(())
}

/// Resource identifier (operation IDs, request IDs).
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    PartialOrd,
    Ord,
    Serialize,
    JsonSchema,
    derive_more::AsRef,
    derive_more::Deref,
    derive_more::Display,
    derive_more::From,
)]
#[as_ref(str)]
#[deref(forward)]
#[display("{_0}")]
pub struct ResourceId(
    #[schemars(length(max = 128), regex(pattern = r"^[A-Za-z0-9][A-Za-z0-9._:\-]{0,127}$"))] pub String,
);

impl ResourceId {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        if s.len() > 128 {
            return Err(ModelValidationError::Invalid {
                type_name: "ResourceId",
                reason: format!("length {} exceeds maximum 128", s.len()),
            });
        }

        if !is_valid_resource_id(s) {
            return Err(ModelValidationError::Invalid {
                type_name: "ResourceId",
                reason:
                    "must start with an alphanumeric character and contain only letters, digits, '.', '_', ':' or '-'"
                        .to_owned(),
            });
        }

        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for ResourceId {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

impl From<&str> for ResourceId {
    fn from(s: &str) -> Self {
        Self(s.to_owned())
    }
}

/// Semantic version string (SemVer 2.0.0).
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    Serialize,
    JsonSchema,
    derive_more::AsRef,
    derive_more::Deref,
    derive_more::Display,
    derive_more::From,
)]
#[as_ref(str)]
#[deref(forward)]
#[display("{_0}")]
pub struct SemanticVersion(
    #[schemars(
        length(max = 128),
        regex(
            pattern = r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
        )
    )]
    pub String,
);

impl SemanticVersion {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        if s.len() > 128 {
            return Err(ModelValidationError::Invalid {
                type_name: "SemanticVersion",
                reason: format!("length {} exceeds maximum 128", s.len()),
            });
        }

        semver::Version::parse(s).map_err(|e| ModelValidationError::Invalid {
            type_name: "SemanticVersion",
            reason: e.to_string(),
        })?;

        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for SemanticVersion {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

impl From<&str> for SemanticVersion {
    fn from(s: &str) -> Self {
        Self(s.to_owned())
    }
}

/// A short constrained string for version values.
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    PartialOrd,
    Ord,
    Serialize,
    JsonSchema,
    derive_more::AsRef,
    derive_more::Deref,
    derive_more::From,
)]
#[as_ref(str)]
#[deref(forward)]
pub struct VersionString(#[schemars(length(min = 1, max = 128))] pub String);

impl VersionString {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        validate_bounded_string(s, 1, 128, "VersionString")?;
        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for VersionString {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

impl From<&str> for VersionString {
    fn from(s: &str) -> Self {
        Self(s.to_owned())
    }
}

/// A custom parameter string.
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    PartialOrd,
    Ord,
    Serialize,
    JsonSchema,
    derive_more::AsRef,
    derive_more::Deref,
    derive_more::From,
)]
#[as_ref(str)]
#[deref(forward)]
pub struct CustomParameterString(#[schemars(length(min = 1, max = 512))] pub String);

impl CustomParameterString {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        validate_bounded_string(s, 1, 512, "CustomParameterString")?;
        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for CustomParameterString {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

impl From<&str> for CustomParameterString {
    fn from(s: &str) -> Self {
        Self(s.to_owned())
    }
}

/// Rule ID in responses. Includes sentinel values not valid as policy rule IDs.
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    Serialize,
    JsonSchema,
    derive_more::AsRef,
    derive_more::Deref,
    derive_more::Display,
    derive_more::From,
)]
#[as_ref(str)]
#[deref(forward)]
#[display("{_0}")]
pub struct RuleId(
    #[schemars(
        length(max = 128),
        regex(pattern = r"^(<default>|<validation-failure>|[A-Za-z0-9][A-Za-z0-9._:\-]{0,127})$")
    )]
    pub String,
);

impl RuleId {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        if s.len() > 128 {
            return Err(ModelValidationError::Invalid {
                type_name: "RuleId",
                reason: format!("length {} exceeds maximum 128", s.len()),
            });
        }

        if s == "<default>" || s == "<validation-failure>" || is_valid_resource_id(s) {
            Ok(Self(s.to_owned()))
        } else {
            Err(ModelValidationError::Invalid {
                type_name: "RuleId",
                reason: "must be '<default>', '<validation-failure>', or start with an alphanumeric character and contain only letters, digits, '.', '_', ':' or '-'"
                    .to_owned(),
            })
        }
    }
}

impl<'de> Deserialize<'de> for RuleId {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

fn is_valid_resource_id(s: &str) -> bool {
    if s.is_empty() {
        return false;
    }

    let bytes = s.as_bytes();
    if !bytes[0].is_ascii_alphanumeric() {
        return false;
    }

    bytes[1..]
        .iter()
        .all(|&b| b.is_ascii_alphanumeric() || b == b'.' || b == b'_' || b == b':' || b == b'-')
}

impl From<&str> for RuleId {
    fn from(s: &str) -> Self {
        Self(s.to_owned())
    }
}

/// Package identifier string.
///
/// Validated against an explicit allowlist of characters: ASCII alphanumerics
/// plus `. - _ + @ / : [ ] , # $ % { }`.
///
/// - `.`, `-`, `_`, `+`: winget (`Notepad++.Notepad++`), chocolatey, pip, cargo,
///   dotnet, apt/dnf/pacman (`g++`, `libstdc++6`), PowerShell modules;
///
/// - `@`, `/`: scoped npm/Bun packages (`@scope/package`), homebrew and scoop
///   `tap/formula` paths, versioned formulas (`python@3.11`);
///
/// - `:`: npm aliases (`alias:@scope/package@1.0.0`), vcpkg triplets
///   (`curl:x64-windows`);
///
/// - `[`, `]`, `,`: vcpkg features (`curl[ssl,http2]:x64-windows`), pip extras
///   (`requests[socks]`);
///
/// - `#`, `$`, `%`, `{`, `}`: additional identifier punctuation (accepted by
///   product decision for forward compatibility). Caveat: these characters
///   carry expansion semantics in some shells (`${VAR}`, `%VAR%`, brace
///   expansion), so downstream command builders must pass identifiers as
///   discrete process arguments and never interpolate them into a shell
///   command line.
///
/// Version range/pin operators (`<`, `>`, `=`, `!`, `|`, `^`, `~`) are
/// rejected: the broker matches against a specific, exact version carried in
/// the request's separate `Package.Version` field, so range expressions do
/// not belong in the identifier (npm aliases must use exact versions, e.g.
/// `alias:pkg@7.20.0`). The wildcards `*` and `?` are also rejected:
/// policy-side package identifier matching is wildcard-based, so wildcards in
/// request identifiers would be ambiguous. Everything else — whitespace,
/// control characters, `"`, `\`, backtick, `& ' ( ) ;`, and non-ASCII — is
/// rejected as well.
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    PartialOrd,
    Ord,
    Serialize,
    JsonSchema,
    derive_more::Deref,
    derive_more::Display,
    derive_more::From,
)]
#[deref(forward)]
#[display("{_0}")]
pub struct PackageIdentifier(
    #[schemars(length(min = 1, max = 256), regex(pattern = r"^[A-Za-z0-9._+@/:\[\],#$%{}-]+$"))] pub String,
);

impl PackageIdentifier {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        if s.is_empty() {
            return Err(ModelValidationError::Invalid {
                type_name: "PackageIdentifier",
                reason: "must not be empty".to_owned(),
            });
        }

        if s.len() > 256 {
            return Err(ModelValidationError::Invalid {
                type_name: "PackageIdentifier",
                reason: format!("length {} exceeds maximum 256", s.len()),
            });
        }

        if !s.bytes().all(|b| {
            b.is_ascii_alphanumeric()
                || matches!(
                    b,
                    b'.' | b'-'
                        | b'_'
                        | b'+'
                        | b'@'
                        | b'/'
                        | b':'
                        | b'['
                        | b']'
                        | b','
                        | b'#'
                        | b'$'
                        | b'%'
                        | b'{'
                        | b'}'
                )
        }) {
            return Err(ModelValidationError::Invalid {
                type_name: "PackageIdentifier",
                reason: "must contain only ASCII alphanumerics or '. - _ + @ / : [ ] , # $ % { }'".to_owned(),
            });
        }

        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for PackageIdentifier {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

/// HTTP API version string.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, JsonSchema, derive_more::Deref, derive_more::Display)]
#[deref(forward)]
#[display("{_0}")]
pub struct ApiVersion(#[schemars(regex(pattern = r"^[0-9]+\.[0-9]+$"))] pub String);

impl ApiVersion {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        if !is_valid_api_version(s) {
            return Err(ModelValidationError::Invalid {
                type_name: "ApiVersion",
                reason: "must be in the form '<major>.<minor>' with digits only (e.g. '1.0')".to_owned(),
            });
        }

        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for ApiVersion {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

fn is_valid_api_version(s: &str) -> bool {
    let Some((major, minor)) = s.split_once('.') else {
        return false;
    };

    !major.is_empty()
        && !minor.is_empty()
        && major.bytes().all(|b| b.is_ascii_digit())
        && minor.bytes().all(|b| b.is_ascii_digit())
}

impl From<&str> for ApiVersion {
    fn from(s: &str) -> Self {
        Self(s.to_owned())
    }
}

/// A process name string.
#[derive(Debug, Clone, PartialEq, Eq, PartialOrd, Ord, Serialize, JsonSchema, derive_more::Deref)]
#[deref(forward)]
pub struct ProcessName(#[schemars(length(min = 1, max = 128))] pub String);

impl ProcessName {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        validate_bounded_string(s, 1, 128, "ProcessName")?;
        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for ProcessName {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

/// A command string.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, JsonSchema, derive_more::Deref)]
#[deref(forward)]
pub struct CommandString(#[schemars(length(min = 1, max = 2048))] pub String);

impl CommandString {
    pub fn parse(s: &str) -> Result<Self, ModelValidationError> {
        validate_bounded_string(s, 1, 2048, "CommandString")?;
        Ok(Self(s.to_owned()))
    }
}

impl<'de> Deserialize<'de> for CommandString {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let s = String::deserialize(deserializer)?;
        Self::parse(&s).map_err(serde::de::Error::custom)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn package_identifier_accepts_manager_specific_identifiers() {
        let valid = [
            // WinGet.
            "Microsoft.VisualStudioCode",
            "Notepad++.Notepad++",
            // Scoped npm/Bun packages.
            "@scope/package",
            "@babel/core",
            // npm aliases (exact versions only).
            "babel-core-legacy:@babel/core@7.20.0",
            "my-alias:lodash@4.17.21",
            "pinned:react@18.2.0",
            // vcpkg triplets and features.
            "curl:x64-windows",
            "curl[ssl]:x64-windows",
            "curl[ssl,http2]:x64-windows",
            // Homebrew/scoop tap paths and versioned formulas.
            "extras/vscode",
            "homebrew/core/python@3.11",
            // pip extras.
            "requests[socks]",
            // Chocolatey, dotnet, cargo, apt-style names.
            "git.install",
            "dotnet-ef",
            "serde_json",
            "g++",
            // Additional identifier punctuation.
            "foo#bar",
            "foo$bar",
            "foo%20bar",
            "foo{bar}",
        ];

        for id in valid {
            PackageIdentifier::parse(id).unwrap_or_else(|e| panic!("{id:?} should be valid: {e}"));

            let json = serde_json::to_string(id).expect("string should serialize to JSON");
            let deserialized: PackageIdentifier =
                serde_json::from_str(&json).unwrap_or_else(|e| panic!("{id:?} should deserialize: {e}"));
            assert_eq!(deserialized.0, id);
        }
    }

    #[test]
    fn package_identifier_rejects_forbidden_input() {
        let invalid = [
            "",
            "foo\\bar",
            "foo\"bar",
            "foo*",
            "foo?",
            "foo\r\nbar",
            "foo\tbar",
            "foo bar",
            "foo\u{0}bar",
            "foo\u{7f}bar",
            "foo&bar",
            "foo'bar",
            "foo(bar)",
            "foo;bar",
            "foo`bar",
            // Version range/pin operators (exact versions only, carried in Package.Version).
            "babel-core-legacy:@babel/core@^7.20.0",
            "my-alias:lodash@~4.17.21",
            "pinned:react@=18.2.0",
            "requests==2.32.0",
            "requests!=2.32.0",
            "ranged:react@>=18.2.0",
            "either:foo@1.0.0||2.0.0",
            "spec<2.0",
            "gr\u{fc}n",
            &"a".repeat(257),
        ];

        for id in invalid {
            PackageIdentifier::parse(id).expect_err(&format!("{id:?} should be rejected"));

            let json = serde_json::to_string(id).expect("string should serialize to JSON");
            serde_json::from_str::<PackageIdentifier>(&json)
                .expect_err(&format!("{id:?} should be rejected during deserialization"));
        }
    }

    #[test]
    fn package_request_deserializes_scoped_and_aliased_identifiers() {
        let identifiers = [
            "@scope/package",
            "babel-core-legacy:@babel/core@7.20.0",
            "curl:x64-windows",
            "curl[ssl]:x64-windows",
        ];

        for id in identifiers {
            let json = serde_json::json!({
                "RequestKind": "PackageRequest",
                "RequestVersion": "1.0",
                "RequestId": "req-scoped-install",
                "CreatedAt": "2026-05-05T12:00:00Z",
                "Operation": "Install",
                "Manager": "Npm",
                "Source": { "Name": "npm" },
                "Package": { "Id": id },
                "Options": {
                    "Interactive": false,
                    "SkipHashCheck": false,
                    "PreRelease": false
                },
                "Client": {
                    "RequestedElevation": "Elevated",
                    "EffectiveUser": "CONTOSO\\alice",
                    "ClientVersion": "3.2.0",
                    "Transport": "HttpNamedPipe",
                    "ClientExecutablePath": "C:\\Program Files\\Devolutions\\NOW\\now-client.exe"
                }
            });

            let request: PackageRequest = serde_json::from_value(json)
                .unwrap_or_else(|e| panic!("PackageRequest with id {id:?} should deserialize: {e}"));
            assert_eq!(request.package.id.0, id);
        }
    }

    #[test]
    fn package_request_rejects_invalid_identifier() {
        let json = serde_json::json!({
            "RequestKind": "PackageRequest",
            "RequestVersion": "1.0",
            "RequestId": "req-invalid-id",
            "CreatedAt": "2026-05-05T12:00:00Z",
            "Operation": "Install",
            "Manager": "Winget",
            "Source": { "Name": "winget" },
            "Package": { "Id": "evil;package\r\n" },
            "Options": {
                "Interactive": false,
                "SkipHashCheck": false,
                "PreRelease": false
            },
            "Client": {
                "RequestedElevation": "Elevated",
                "EffectiveUser": "CONTOSO\\alice",
                "ClientVersion": "3.2.0",
                "Transport": "HttpNamedPipe",
                "ClientExecutablePath": "C:\\Program Files\\Devolutions\\NOW\\now-client.exe"
            }
        });

        serde_json::from_value::<PackageRequest>(json)
            .expect_err("PackageRequest with forbidden identifier characters should be rejected");
    }
}
