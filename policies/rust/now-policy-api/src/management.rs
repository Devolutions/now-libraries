//! Policy management, validation, and replacement endpoint models.

#![allow(
    unused_qualifications,
    reason = "schemars schema_with expansion triggers this lint for a qualified function name"
)]

use std::collections::BTreeMap;

use now_policy::{PolicyDocument, PolicyDraftDocument};
use schemars::{JsonSchema, Schema, SchemaGenerator, json_schema};
use serde::{Deserialize, Serialize, Serializer};

use super::api::ServerContext;
use super::{
    ApiVersion, PolicyManagementResponseKind, PolicyReplacementRequestKind, PolicyReplacementResponseKind,
    PolicyValidationRequestKind, PolicyValidationResponseKind, ResourceId, validate_bounded_string,
};

/// Current configured-policy state.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyManagementState")]
pub enum PolicyManagementState {
    Active,
    Missing,
    Invalid,
}

/// Origin of the resolved policy path.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyConfigurationSource")]
pub enum PolicyConfigurationSource {
    DefaultPath,
    ConfiguredPath,
}

/// Advisory ability to write the configured policy through the management API.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyWriteCapability")]
pub enum PolicyWriteCapability {
    Writable,
    ReadOnly,
    Unsupported,
}

/// Stable reason why the configured policy cannot be written.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyReadOnlyReason")]
pub enum PolicyReadOnlyReason {
    ManagementDisabled,
    PathNotConfigured,
    UnsafePath,
    InsufficientPermissions,
    UnsupportedFileSystem,
}

/// Requested identity/revision behavior for a policy replacement.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyReplacementOperation")]
pub enum PolicyReplacementOperation {
    /// Update the active policy while retaining its identity and incrementing its revision.
    Update,
    /// Replace the active policy with an explicitly different identity at revision 1.
    ReplaceIdentity,
    /// Create the first policy at revision 1.
    Create,
    /// Replace an invalid configured document at revision 1.
    Repair,
}

/// Optimistic-conflict behavior for policy replacement.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyConflictHandling")]
pub enum PolicyConflictHandling {
    Reject,
    /// Overwrite only the exact newly observed store token carried by this request.
    ConfirmOverwrite,
}

/// Severity of a policy validation finding.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyFindingSeverity")]
pub enum PolicyFindingSeverity {
    Error,
    Warning,
}

/// Stable policy validation finding code.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyFindingCode")]
pub enum PolicyFindingCode {
    SchemaViolation,
    UnknownField,
    MissingRequiredField,
    InvalidFieldType,
    InvalidFieldValue,
    DuplicateRuleId,
    IneffectiveBooleanMatch,
    InvalidVersionRange,
    EmptyVersionRange,
    InvalidWildcardPattern,
    ContradictoryConstraints,
    InvalidValidityInterval,
    UnsupportedSchema,
    UnsupportedPolicyType,
    UnsupportedPolicyVersion,
    AuditModeEnabled,
    DefaultAllow,
    SensitiveOptionAllowed,
}

/// Opaque token identifying the exact state observed in the policy store.
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    JsonSchema,
    derive_more::AsRef,
    derive_more::Deref,
    derive_more::Display,
    derive_more::From,
)]
#[as_ref(str)]
#[deref(forward)]
#[display("{_0}")]
pub struct PolicyStoreToken(
    #[schemars(
        length(min = 1, max = 512),
        regex(pattern = r"^[A-Za-z0-9][A-Za-z0-9._~:\-]{0,511}$")
    )]
    pub String,
);

impl<'de> Deserialize<'de> for PolicyStoreToken {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let value = String::deserialize(deserializer)?;
        validate_opaque_ascii(&value, 512, "PolicyStoreToken").map_err(serde::de::Error::custom)?;
        Ok(Self(value))
    }
}

impl Serialize for PolicyStoreToken {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        validate_opaque_ascii(&self.0, 512, "PolicyStoreToken").map_err(serde::ser::Error::custom)?;
        serializer.serialize_str(&self.0)
    }
}

impl From<&str> for PolicyStoreToken {
    fn from(value: &str) -> Self {
        Self(value.to_owned())
    }
}

/// Opaque receipt bound to a canonical draft, validator version, and exact warning set.
#[derive(
    Debug,
    Clone,
    PartialEq,
    Eq,
    JsonSchema,
    derive_more::AsRef,
    derive_more::Deref,
    derive_more::Display,
    derive_more::From,
)]
#[as_ref(str)]
#[deref(forward)]
#[display("{_0}")]
pub struct PolicyValidationReceipt(
    #[schemars(
        length(min = 1, max = 2048),
        regex(pattern = r"^[A-Za-z0-9][A-Za-z0-9._~:\-]{0,2047}$")
    )]
    pub String,
);

impl<'de> Deserialize<'de> for PolicyValidationReceipt {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let value = String::deserialize(deserializer)?;
        validate_opaque_ascii(&value, 2048, "PolicyValidationReceipt").map_err(serde::de::Error::custom)?;
        Ok(Self(value))
    }
}

impl Serialize for PolicyValidationReceipt {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        validate_opaque_ascii(&self.0, 2048, "PolicyValidationReceipt").map_err(serde::ser::Error::custom)?;
        serializer.serialize_str(&self.0)
    }
}

impl From<&str> for PolicyValidationReceipt {
    fn from(value: &str) -> Self {
        Self(value.to_owned())
    }
}

fn validate_opaque_ascii(
    value: &str,
    max_length: usize,
    type_name: &'static str,
) -> Result<(), super::ModelValidationError> {
    validate_bounded_string(value, 1, max_length, type_name)?;
    if !value.bytes().enumerate().all(|(index, byte)| {
        (index == 0 && byte.is_ascii_alphanumeric())
            || (index > 0 && (byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'_' | b'~' | b':' | b'-')))
    }) {
        return Err(super::ModelValidationError::Invalid {
            type_name,
            reason: "must use safe printable ASCII characters and start with an ASCII alphanumeric character"
                .to_owned(),
        });
    }
    Ok(())
}

/// Versioned, structured policy validation finding.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyFinding")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyFinding {
    /// Finding shape version.
    pub finding_version: ApiVersion,

    pub severity: PolicyFindingSeverity,
    pub code: PolicyFindingCode,

    /// RFC 6901 JSON Pointer into the submitted draft.
    #[schemars(length(max = 2048))]
    pub path: String,

    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub rule_id: Option<ResourceId>,

    /// Machine-readable message arguments for localization.
    #[serde(default, skip_serializing_if = "BTreeMap::is_empty")]
    pub arguments: BTreeMap<String, serde_json::Value>,

    /// Human-readable fallback for clients that do not recognize the code.
    #[schemars(length(min = 1, max = 2048))]
    pub message: String,
}

/// Authoritative validation output.
#[derive(Debug, Clone, Deserialize)]
#[serde(try_from = "PolicyValidationResultWire")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyValidationResult {
    /// Validation result shape version.
    pub result_version: ApiVersion,

    /// Version of the implementation validator that produced the receipt.
    pub validator_version: String,

    pub is_valid: bool,

    /// Canonical typed draft, present only when validation succeeds.
    #[serde(default)]
    pub canonical_draft: Option<PolicyDraftDocument>,

    /// Receipt bound to the canonical draft, validator version, and exact warning set.
    #[serde(default)]
    pub validation_receipt: Option<PolicyValidationReceipt>,

    pub findings: Vec<PolicyFinding>,
}

#[derive(Deserialize, JsonSchema)]
#[schemars(rename = "PolicyValidationResultFields")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
struct PolicyValidationResultWire {
    pub result_version: ApiVersion,
    #[schemars(length(min = 1, max = 128))]
    pub validator_version: String,
    pub is_valid: bool,
    #[serde(default)]
    #[schemars(schema_with = "super::policy::optional_policy_draft_document_schema")]
    pub canonical_draft: Option<PolicyDraftDocument>,
    #[serde(default)]
    pub validation_receipt: Option<PolicyValidationReceipt>,
    pub findings: Vec<PolicyFinding>,
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct PolicyValidationResultRef<'a> {
    result_version: &'a ApiVersion,
    validator_version: &'a str,
    is_valid: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    canonical_draft: Option<&'a PolicyDraftDocument>,
    #[serde(skip_serializing_if = "Option::is_none")]
    validation_receipt: Option<&'a PolicyValidationReceipt>,
    findings: &'a [PolicyFinding],
}

impl PolicyValidationResult {
    fn validate(&self) -> Result<(), &'static str> {
        let has_success_artifacts = self.canonical_draft.is_some() && self.validation_receipt.is_some();
        let has_any_success_artifact = self.canonical_draft.is_some() || self.validation_receipt.is_some();
        let has_error = self
            .findings
            .iter()
            .any(|finding| finding.severity == PolicyFindingSeverity::Error);
        if self.is_valid && !has_success_artifacts {
            return Err("valid policy validation results require CanonicalDraft and ValidationReceipt");
        }
        if self.is_valid && has_error {
            return Err("valid policy validation results must not contain Error findings");
        }
        if !self.is_valid && has_any_success_artifact {
            return Err("invalid policy validation results must not contain CanonicalDraft or ValidationReceipt");
        }
        if !self.is_valid && !has_error {
            return Err("invalid policy validation results require at least one Error finding");
        }
        Ok(())
    }
}

impl TryFrom<PolicyValidationResultWire> for PolicyValidationResult {
    type Error = &'static str;

    fn try_from(value: PolicyValidationResultWire) -> Result<Self, Self::Error> {
        let result = Self {
            result_version: value.result_version,
            validator_version: value.validator_version,
            is_valid: value.is_valid,
            canonical_draft: value.canonical_draft,
            validation_receipt: value.validation_receipt,
            findings: value.findings,
        };
        result.validate()?;
        Ok(result)
    }
}

impl Serialize for PolicyValidationResult {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        self.validate().map_err(serde::ser::Error::custom)?;
        PolicyValidationResultRef {
            result_version: &self.result_version,
            validator_version: &self.validator_version,
            is_valid: self.is_valid,
            canonical_draft: self.canonical_draft.as_ref(),
            validation_receipt: self.validation_receipt.as_ref(),
            findings: &self.findings,
        }
        .serialize(serializer)
    }
}

impl JsonSchema for PolicyValidationResult {
    fn schema_name() -> std::borrow::Cow<'static, str> {
        "PolicyValidationResult".into()
    }

    fn json_schema(generator: &mut SchemaGenerator) -> Schema {
        let fields = generator.subschema_for::<PolicyValidationResultWire>();
        json_schema!({
            "allOf": [fields],
            "oneOf": [
                {
                    "properties": {
                        "IsValid": { "const": true },
                        "CanonicalDraft": {
                            "$ref": "#/components/schemas/PolicyDraftDocument"
                        },
                        "ValidationReceipt": {
                            "$ref": "#/components/schemas/PolicyValidationReceipt"
                        },
                        "Findings": {
                            "items": {
                                "properties": {
                                    "Severity": { "const": "Warning" }
                                },
                                "required": ["Severity"]
                            }
                        }
                    },
                    "required": ["CanonicalDraft", "ValidationReceipt"]
                },
                {
                    "properties": {
                        "IsValid": { "const": false },
                        "CanonicalDraft": { "type": "null" },
                        "ValidationReceipt": { "type": "null" },
                        "Findings": {
                            "minItems": 1,
                            "not": {
                                "items": {
                                    "properties": {
                                        "Severity": { "const": "Warning" }
                                    },
                                    "required": ["Severity"]
                                }
                            }
                        }
                    }
                }
            ]
        })
    }
}

/// Sanitized diagnostics for an invalid configured policy.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "InvalidPolicyDiagnostics")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct InvalidPolicyDiagnostics {
    pub diagnostics_version: ApiVersion,
    pub findings: Vec<PolicyFinding>,
}

/// Atomic view of configured policy state and management guidance.
#[derive(Debug, Clone, Deserialize)]
#[serde(try_from = "PolicyManagementSnapshotWire")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyManagementSnapshot {
    pub state: PolicyManagementState,

    /// Fully resolved configured path.
    pub configured_path: String,

    pub store_token: PolicyStoreToken,
    pub source: PolicyConfigurationSource,
    pub write_capability: PolicyWriteCapability,

    #[serde(default)]
    pub read_only_reason: Option<PolicyReadOnlyReason>,

    pub elevation_required: bool,

    #[serde(default)]
    pub policy: Option<PolicyDocument>,

    #[serde(default)]
    pub invalid_diagnostics: Option<InvalidPolicyDiagnostics>,
}

#[derive(Deserialize, JsonSchema)]
#[schemars(rename = "PolicyManagementSnapshotFields")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
struct PolicyManagementSnapshotWire {
    pub state: PolicyManagementState,
    #[schemars(length(min = 1, max = 32767))]
    pub configured_path: String,
    pub store_token: PolicyStoreToken,
    pub source: PolicyConfigurationSource,
    pub write_capability: PolicyWriteCapability,
    #[serde(default)]
    pub read_only_reason: Option<PolicyReadOnlyReason>,
    pub elevation_required: bool,
    #[serde(default)]
    #[schemars(schema_with = "super::policy::optional_policy_document_schema")]
    pub policy: Option<PolicyDocument>,
    #[serde(default)]
    pub invalid_diagnostics: Option<InvalidPolicyDiagnostics>,
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct PolicyManagementSnapshotRef<'a> {
    state: PolicyManagementState,
    configured_path: &'a str,
    store_token: &'a PolicyStoreToken,
    source: PolicyConfigurationSource,
    write_capability: PolicyWriteCapability,
    #[serde(skip_serializing_if = "Option::is_none")]
    read_only_reason: Option<PolicyReadOnlyReason>,
    elevation_required: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    policy: Option<&'a PolicyDocument>,
    #[serde(skip_serializing_if = "Option::is_none")]
    invalid_diagnostics: Option<&'a InvalidPolicyDiagnostics>,
}

impl PolicyManagementSnapshot {
    fn validate(&self) -> Result<(), &'static str> {
        match self.state {
            PolicyManagementState::Active if self.policy.is_none() || self.invalid_diagnostics.is_some() => {
                return Err("Active management snapshots require Policy and forbid InvalidDiagnostics");
            }
            PolicyManagementState::Missing if self.policy.is_some() || self.invalid_diagnostics.is_some() => {
                return Err("Missing management snapshots forbid Policy and InvalidDiagnostics");
            }
            PolicyManagementState::Invalid => {
                let Some(diagnostics) = &self.invalid_diagnostics else {
                    return Err("Invalid management snapshots require InvalidDiagnostics");
                };
                if self.policy.is_some() {
                    return Err("Invalid management snapshots forbid Policy");
                }
                if diagnostics.findings.is_empty()
                    || !diagnostics
                        .findings
                        .iter()
                        .any(|finding| finding.severity == PolicyFindingSeverity::Error)
                {
                    return Err("Invalid management snapshots require nonempty diagnostics with an Error finding");
                }
            }
            _ => {}
        }

        match self.write_capability {
            PolicyWriteCapability::Writable if self.read_only_reason.is_some() => {
                return Err("Writable management snapshots forbid ReadOnlyReason");
            }
            PolicyWriteCapability::ReadOnly | PolicyWriteCapability::Unsupported if self.read_only_reason.is_none() => {
                return Err("ReadOnly and Unsupported management snapshots require ReadOnlyReason");
            }
            _ => {}
        }

        Ok(())
    }
}

impl TryFrom<PolicyManagementSnapshotWire> for PolicyManagementSnapshot {
    type Error = &'static str;

    fn try_from(value: PolicyManagementSnapshotWire) -> Result<Self, Self::Error> {
        let snapshot = Self {
            state: value.state,
            configured_path: value.configured_path,
            store_token: value.store_token,
            source: value.source,
            write_capability: value.write_capability,
            read_only_reason: value.read_only_reason,
            elevation_required: value.elevation_required,
            policy: value.policy,
            invalid_diagnostics: value.invalid_diagnostics,
        };
        snapshot.validate()?;
        Ok(snapshot)
    }
}

impl Serialize for PolicyManagementSnapshot {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        self.validate().map_err(serde::ser::Error::custom)?;
        PolicyManagementSnapshotRef {
            state: self.state,
            configured_path: &self.configured_path,
            store_token: &self.store_token,
            source: self.source,
            write_capability: self.write_capability,
            read_only_reason: self.read_only_reason,
            elevation_required: self.elevation_required,
            policy: self.policy.as_ref(),
            invalid_diagnostics: self.invalid_diagnostics.as_ref(),
        }
        .serialize(serializer)
    }
}

impl JsonSchema for PolicyManagementSnapshot {
    fn schema_name() -> std::borrow::Cow<'static, str> {
        "PolicyManagementSnapshot".into()
    }

    fn json_schema(generator: &mut SchemaGenerator) -> Schema {
        let fields = generator.subschema_for::<PolicyManagementSnapshotWire>();
        json_schema!({
            "allOf": [
                fields,
                {
                    "oneOf": [
                        {
                            "properties": {
                                "State": { "const": "Active" },
                                "Policy": {
                                    "$ref": "#/components/schemas/PolicyDocument"
                                },
                                "InvalidDiagnostics": { "type": "null" }
                            },
                            "required": ["Policy"]
                        },
                        {
                            "properties": {
                                "State": { "const": "Missing" },
                                "Policy": { "type": "null" },
                                "InvalidDiagnostics": { "type": "null" }
                            }
                        },
                        {
                            "properties": {
                                "State": { "const": "Invalid" },
                                "InvalidDiagnostics": {
                                    "allOf": [{
                                        "$ref": "#/components/schemas/InvalidPolicyDiagnostics"
                                    }],
                                    "properties": {
                                        "Findings": {
                                            "minItems": 1,
                                            "not": {
                                                "items": {
                                                    "properties": {
                                                        "Severity": { "const": "Warning" }
                                                    },
                                                    "required": ["Severity"]
                                                }
                                            }
                                        }
                                    }
                                },
                                "Policy": { "type": "null" }
                            },
                            "required": ["InvalidDiagnostics"]
                        }
                    ]
                },
                {
                    "oneOf": [
                        {
                            "properties": {
                                "WriteCapability": { "const": "Writable" },
                                "ReadOnlyReason": { "type": "null" }
                            }
                        },
                        {
                            "properties": {
                                "WriteCapability": {
                                    "enum": ["ReadOnly", "Unsupported"]
                                },
                                "ReadOnlyReason": {
                                    "$ref": "#/components/schemas/PolicyReadOnlyReason"
                                }
                            },
                            "required": ["ReadOnlyReason"]
                        }
                    ]
                }
            ]
        })
    }
}

/// Response body for `GET /v1/policy/management`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyManagementResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyManagementResponse {
    pub response_kind: PolicyManagementResponseKind,
    pub response_version: ApiVersion,
    pub server: ServerContext,
    pub management: PolicyManagementSnapshot,
}

/// Request body for `POST /v1/policy/validate`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyValidationRequest")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyValidationRequest {
    pub request_kind: PolicyValidationRequestKind,
    pub request_version: ApiVersion,

    /// Raw draft JSON retained without dropping unknown members.
    pub draft: serde_json::Value,
}

/// Response body for `POST /v1/policy/validate`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyValidationResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyValidationResponse {
    pub response_kind: PolicyValidationResponseKind,
    pub response_version: ApiVersion,
    pub server: ServerContext,
    pub validation: PolicyValidationResult,
}

/// Request body for `PUT /v1/policy`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyReplacementRequest")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyReplacementRequest {
    pub request_kind: PolicyReplacementRequestKind,
    pub request_version: ApiVersion,
    pub expected_store_token: PolicyStoreToken,
    pub operation: PolicyReplacementOperation,
    pub conflict_handling: PolicyConflictHandling,

    /// Explicit acknowledgement of every warning bound into the validation receipt.
    pub warnings_acknowledged: bool,

    /// Raw draft JSON retained for transaction-time reparsing and revalidation.
    pub draft: serde_json::Value,

    pub validation_receipt: PolicyValidationReceipt,
}

/// Response body for `PUT /v1/policy`.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyReplacementResponse")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyReplacementResponse {
    pub response_kind: PolicyReplacementResponseKind,
    pub response_version: ApiVersion,
    pub server: ServerContext,

    /// Exact committed active policy, including server-assigned metadata.
    #[schemars(schema_with = "super::policy::policy_document_schema")]
    pub policy: PolicyDocument,

    /// Transaction-time validation result for the exact committed draft.
    pub validation: PolicyValidationResult,

    /// Newly observed management state and store token.
    pub management: PolicyManagementSnapshot,
}

#[cfg(test)]
mod tests {
    use super::{PolicyManagementSnapshot, PolicyStoreToken, PolicyValidationReceipt, PolicyValidationResult};

    #[test]
    fn opaque_tokens_and_receipts_reject_non_ascii_values() {
        assert!(serde_json::from_str::<PolicyStoreToken>("\"store:activé:7\"").is_err());
        assert!(serde_json::from_str::<PolicyValidationReceipt>("\"receipt:é\"").is_err());
        assert!(serde_json::to_value(PolicyStoreToken("store:activé:7".to_owned())).is_err());
        assert!(serde_json::to_value(PolicyValidationReceipt("receipt:é".to_owned())).is_err());
    }

    #[test]
    fn validation_result_requires_success_artifacts_exactly_when_valid() {
        let valid_without_artifacts = serde_json::json!({
            "ResultVersion": "1.0",
            "ValidatorVersion": "validator/1",
            "IsValid": true,
            "Findings": []
        });
        assert!(serde_json::from_value::<PolicyValidationResult>(valid_without_artifacts).is_err());

        let invalid_with_receipt = serde_json::json!({
            "ResultVersion": "1.0",
            "ValidatorVersion": "validator/1",
            "IsValid": false,
            "ValidationReceipt": "receipt",
            "Findings": []
        });
        assert!(serde_json::from_value::<PolicyValidationResult>(invalid_with_receipt).is_err());
    }

    #[test]
    fn validation_result_requires_findings_consistent_with_validity() {
        let valid_with_error = serde_json::json!({
            "ResultVersion": "1.0",
            "ValidatorVersion": "validator/1",
            "IsValid": true,
            "CanonicalDraft": {
                "$schema": "https://devolutions.net/schemas/now-policy.schema.1.0.json",
                "PolicyVersion": "1.0.0",
                "PolicyType": "PackageBrokerPolicy",
                "Metadata": { "Id": "test", "Publisher": "test" },
                "Enforcement": { "DefaultDecision": "Deny", "RulePrecedence": "PriorityThenDeny" },
                "Rules": []
            },
            "ValidationReceipt": "receipt",
            "Findings": [{
                "FindingVersion": "1.0",
                "Severity": "Error",
                "Code": "InvalidFieldValue",
                "Path": "",
                "Message": "error"
            }]
        });
        assert!(serde_json::from_value::<PolicyValidationResult>(valid_with_error).is_err());

        for findings in [
            serde_json::json!([]),
            serde_json::json!([{
                "FindingVersion": "1.0",
                "Severity": "Warning",
                "Code": "DefaultAllow",
                "Path": "/Enforcement/DefaultDecision",
                "Message": "warning"
            }]),
        ] {
            let invalid_without_error = serde_json::json!({
                "ResultVersion": "1.0",
                "ValidatorVersion": "validator/1",
                "IsValid": false,
                "Findings": findings
            });
            assert!(serde_json::from_value::<PolicyValidationResult>(invalid_without_error).is_err());
        }

        let mut invalid_for_serialization: PolicyValidationResult = serde_json::from_value(serde_json::json!({
            "ResultVersion": "1.0",
            "ValidatorVersion": "validator/1",
            "IsValid": false,
            "Findings": [{
                "FindingVersion": "1.0",
                "Severity": "Error",
                "Code": "InvalidFieldValue",
                "Path": "",
                "Message": "error"
            }]
        }))
        .expect("valid invalid-result fixture");
        invalid_for_serialization.findings.clear();
        assert!(serde_json::to_value(invalid_for_serialization).is_err());
    }

    #[test]
    fn management_snapshot_rejects_contradictory_state_and_capability() {
        let active_without_policy = serde_json::json!({
            "State": "Active",
            "ConfiguredPath": "C:\\policy.json",
            "StoreToken": "store:1",
            "Source": "ConfiguredPath",
            "WriteCapability": "Writable",
            "ElevationRequired": true
        });
        assert!(serde_json::from_value::<PolicyManagementSnapshot>(active_without_policy).is_err());

        let readonly_without_reason = serde_json::json!({
            "State": "Missing",
            "ConfiguredPath": "C:\\policy.json",
            "StoreToken": "store:1",
            "Source": "ConfiguredPath",
            "WriteCapability": "ReadOnly",
            "ElevationRequired": true
        });
        assert!(serde_json::from_value::<PolicyManagementSnapshot>(readonly_without_reason).is_err());

        let mut invalid_for_serialization: PolicyManagementSnapshot = serde_json::from_value(serde_json::json!({
            "State": "Missing",
            "ConfiguredPath": "C:\\policy.json",
            "StoreToken": "store:1",
            "Source": "ConfiguredPath",
            "WriteCapability": "Writable",
            "ElevationRequired": true
        }))
        .expect("valid missing snapshot fixture");
        invalid_for_serialization.state = super::PolicyManagementState::Active;
        assert!(serde_json::to_value(invalid_for_serialization).is_err());
    }
}
