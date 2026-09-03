//! Policy document models.

use std::collections::BTreeSet;

use chrono::{DateTime, Utc};
use schemars::{JsonSchema, Schema, SchemaGenerator, json_schema};
use serde::{Deserialize, Serialize};

use crate::{
    Architecture, CustomParameterString, Decision, Elevation, HttpUrl, ManagerName, ModelValidationError, Operation,
    PackageBrokerPolicy, PolicyDraftSchemaUri, PolicySchemaUri, ResourceId, Scope, SemanticVersion, StringPattern,
    VersionString,
};

const MAX_POLICY_REVISION: u32 = 2_147_483_647;

/// A policy document governing which package operations are allowed or denied.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyDocument")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyDocument {
    /// Policy schema URI constant.
    #[serde(rename = "$schema")]
    pub _schema: PolicySchemaUri,

    /// Policy syntax version (semver).
    pub policy_version: SemanticVersion,

    /// Must be `"PackageBrokerPolicy"`.
    pub policy_type: PackageBrokerPolicy,

    /// Policy metadata.
    pub metadata: PolicyMetadata,

    /// Enforcement configuration.
    pub enforcement: PolicyEnforcement,

    /// Ordered list of policy rules (may be empty; enforcement defaults apply).
    #[schemars(length(max = 1024))]
    pub rules: Vec<PolicyRule>,
}

impl PolicyDocument {
    /// Create an editable draft, intentionally omitting server-managed commit metadata.
    pub fn to_draft(&self) -> PolicyDraftDocument {
        PolicyDraftDocument {
            _schema: PolicyDraftSchemaUri,
            policy_version: self.policy_version.clone(),
            policy_type: self.policy_type,
            metadata: self.metadata.to_draft(),
            enforcement: self.enforcement.clone(),
            rules: self.rules.clone(),
        }
    }
}

/// An editable policy document without server-managed commit metadata.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyDraftDocument")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyDraftDocument {
    /// Policy draft schema URI constant.
    #[serde(rename = "$schema")]
    pub _schema: PolicyDraftSchemaUri,

    /// Policy syntax version (semver).
    pub policy_version: SemanticVersion,

    /// Must be `"PackageBrokerPolicy"`.
    pub policy_type: PackageBrokerPolicy,

    /// Editable policy metadata.
    pub metadata: PolicyDraftMetadata,

    /// Enforcement configuration.
    pub enforcement: PolicyEnforcement,

    /// Ordered list of policy rules (may be empty; enforcement defaults apply).
    #[schemars(length(max = 1024))]
    pub rules: Vec<PolicyRule>,
}

impl PolicyDraftDocument {
    /// Commit this draft with server-managed revision and publication metadata.
    pub fn into_policy_document(
        self,
        revision: u32,
        published_at: DateTime<Utc>,
    ) -> Result<PolicyDocument, ModelValidationError> {
        if !(1..=MAX_POLICY_REVISION).contains(&revision) {
            return Err(ModelValidationError::Invalid {
                type_name: "PolicyDocument",
                reason: format!("revision must be between 1 and {MAX_POLICY_REVISION}"),
            });
        }

        Ok(PolicyDocument {
            _schema: PolicySchemaUri,
            policy_version: self.policy_version,
            policy_type: self.policy_type,
            metadata: self.metadata.into_policy_metadata(revision, published_at),
            enforcement: self.enforcement,
            rules: self.rules,
        })
    }
}

/// Policy metadata.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyMetadata")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyMetadata {
    /// Unique policy identifier.
    pub id: ResourceId,

    /// Organization that published the policy.
    #[schemars(length(min = 1, max = 128))]
    pub publisher: String,

    /// Monotonically increasing revision number.
    #[serde(
        serialize_with = "serialize_policy_revision",
        deserialize_with = "deserialize_policy_revision"
    )]
    #[schemars(range(min = 1, max = 2147483647))]
    pub revision: u32,

    /// ISO 8601 publication timestamp (RFC 3339).
    pub published_at: DateTime<Utc>,

    /// Policy becomes active at this time.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub valid_from: Option<DateTime<Utc>>,

    /// Policy expires at this time.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub valid_until: Option<DateTime<Utc>>,

    /// Human-readable description.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 512))]
    pub description: Option<String>,

    /// URL for support or documentation.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub support_url: Option<HttpUrl>,
}

fn validate_policy_revision(revision: u32) -> Result<(), ModelValidationError> {
    if !(1..=MAX_POLICY_REVISION).contains(&revision) {
        return Err(ModelValidationError::Invalid {
            type_name: "PolicyMetadata",
            reason: format!("revision must be between 1 and {MAX_POLICY_REVISION}"),
        });
    }

    Ok(())
}

fn serialize_policy_revision<S: serde::Serializer>(revision: &u32, serializer: S) -> Result<S::Ok, S::Error> {
    validate_policy_revision(*revision).map_err(serde::ser::Error::custom)?;
    revision.serialize(serializer)
}

fn deserialize_policy_revision<'de, D: serde::Deserializer<'de>>(deserializer: D) -> Result<u32, D::Error> {
    let revision = u32::deserialize(deserializer)?;
    validate_policy_revision(revision).map_err(serde::de::Error::custom)?;
    Ok(revision)
}

/// Editable policy metadata without server-managed revision and publication time.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyDraftMetadata")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyDraftMetadata {
    /// Unique policy identifier.
    pub id: ResourceId,

    /// Organization that publishes the policy.
    #[schemars(length(min = 1, max = 128))]
    pub publisher: String,

    /// Policy becomes active at this time.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub valid_from: Option<DateTime<Utc>>,

    /// Policy expires at this time.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub valid_until: Option<DateTime<Utc>>,

    /// Human-readable description.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 512))]
    pub description: Option<String>,

    /// URL for support or documentation.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub support_url: Option<HttpUrl>,
}

impl PolicyMetadata {
    fn to_draft(&self) -> PolicyDraftMetadata {
        PolicyDraftMetadata {
            id: self.id.clone(),
            publisher: self.publisher.clone(),
            valid_from: self.valid_from,
            valid_until: self.valid_until,
            description: self.description.clone(),
            support_url: self.support_url.clone(),
        }
    }
}

impl PolicyDraftMetadata {
    fn into_policy_metadata(self, revision: u32, published_at: DateTime<Utc>) -> PolicyMetadata {
        PolicyMetadata {
            id: self.id,
            publisher: self.publisher,
            revision,
            published_at,
            valid_from: self.valid_from,
            valid_until: self.valid_until,
            description: self.description,
            support_url: self.support_url,
        }
    }
}

/// Enforcement configuration.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyEnforcement")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyEnforcement {
    /// Decision when no rule matches.
    pub default_decision: Decision,

    /// Rule precedence strategy (must be "PriorityThenDeny").
    pub rule_precedence: RulePrecedence,

    /// When true, broker logs decisions but does not enforce.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub audit_mode: Option<bool>,
}

/// Rule precedence strategy — always PriorityThenDeny.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "RulePrecedence")]
pub enum RulePrecedence {
    PriorityThenDeny,
}

/// A single policy rule.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyRule")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyRule {
    /// Unique rule identifier.
    pub id: ResourceId,

    /// Whether the rule is active.
    #[serde(default = "default_true")]
    pub enabled: bool,

    /// Priority (lower = higher precedence).
    #[schemars(range(min = 0, max = 2147483647))]
    pub priority: u32,

    /// Decision if this rule matches.
    pub decision: Decision,

    /// Reason reported to the client.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(max = 512))]
    pub reason: Option<String>,

    /// Match criteria — request must satisfy all specified fields.
    /// At least one criterion must be present.
    #[serde(rename = "Match", deserialize_with = "deserialize_non_empty_match")]
    #[schemars(with = "NonEmptyPolicyMatchSchema")]
    pub match_criteria: PolicyMatch,

    /// Additional constraints applied after matching.
    /// When absent, no constraints are enforced beyond the match criteria.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub constraints: Option<PolicyConstraints>,
}

fn default_true() -> bool {
    true
}

fn deserialize_non_empty_match<'de, D: serde::Deserializer<'de>>(deserializer: D) -> Result<PolicyMatch, D::Error> {
    let m = PolicyMatch::deserialize(deserializer)?;
    if m.is_empty() {
        return Err(serde::de::Error::custom("match must contain at least one criterion"));
    }
    Ok(m)
}

struct NonEmptyPolicyMatchSchema;

impl JsonSchema for NonEmptyPolicyMatchSchema {
    fn inline_schema() -> bool {
        true
    }

    fn schema_name() -> std::borrow::Cow<'static, str> {
        "NonEmptyPolicyMatch".into()
    }

    fn json_schema(generator: &mut SchemaGenerator) -> Schema {
        json_schema!({
            "minProperties": 1,
            "allOf": [generator.subschema_for::<PolicyMatch>()],
        })
    }
}

/// Match criteria for a policy rule. All specified fields must match.
/// At least one field must be present.
#[derive(Debug, Clone, Default, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyMatch")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyMatch {
    /// Allowed operations.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 3))]
    pub operations: BTreeSet<Operation>,

    /// Allowed managers.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 16))]
    pub managers: BTreeSet<ManagerName>,

    /// Source patterns (wildcard).
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 128))]
    pub sources: BTreeSet<StringPattern>,

    /// Package identifier patterns (wildcard).
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 1024))]
    pub package_identifiers: BTreeSet<StringPattern>,

    /// Package name patterns (wildcard).
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 1024))]
    pub package_names: BTreeSet<StringPattern>,

    /// Exact version list.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 256))]
    pub versions: BTreeSet<VersionString>,

    /// Semantic version range.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub version_range: Option<VersionRange>,

    /// Allowed scopes.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 2))]
    pub scopes: BTreeSet<Scope>,

    /// Allowed architectures.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 5))]
    pub architectures: BTreeSet<Architecture>,

    /// Allowed elevation levels.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 2))]
    pub elevation: BTreeSet<Elevation>,

    /// Allowed interactive values.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub interactive: BTreeSet<bool>,

    /// Allowed skipHashCheck values.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub skip_hash_check: BTreeSet<bool>,

    /// Allowed preRelease values.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub pre_release: BTreeSet<bool>,

    /// Whether request has custom parameters.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub has_custom_parameters: BTreeSet<bool>,

    /// Whether request has custom install location.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub has_custom_install_location: BTreeSet<bool>,

    /// Whether request has pre/post operation commands.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub has_pre_post_commands: BTreeSet<bool>,

    /// Whether request has kill-before-operation entries.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub has_kill_before_operation: BTreeSet<bool>,

    /// Whether request has uninstall-previous flag set.
    #[serde(
        default,
        skip_serializing_if = "BTreeSet::is_empty",
        serialize_with = "serialize_boolean_match",
        deserialize_with = "deserialize_boolean_match"
    )]
    #[schemars(length(max = 1))]
    pub has_uninstall_previous: BTreeSet<bool>,
}

fn deserialize_boolean_match<'de, D: serde::Deserializer<'de>>(deserializer: D) -> Result<BTreeSet<bool>, D::Error> {
    let values = Vec::<bool>::deserialize(deserializer)?;
    if values.len() > 1 {
        return Err(serde::de::Error::custom(
            "boolean match arrays must contain at most one value",
        ));
    }

    Ok(values.into_iter().collect())
}

fn serialize_boolean_match<S: serde::Serializer>(values: &BTreeSet<bool>, serializer: S) -> Result<S::Ok, S::Error> {
    if values.len() > 1 {
        return Err(serde::ser::Error::custom(
            "boolean match arrays must contain at most one value",
        ));
    }

    values.serialize(serializer)
}

impl PolicyMatch {
    /// Returns true if no criteria are specified.
    pub fn is_empty(&self) -> bool {
        self.operations.is_empty()
            && self.managers.is_empty()
            && self.sources.is_empty()
            && self.package_identifiers.is_empty()
            && self.package_names.is_empty()
            && self.versions.is_empty()
            && self.version_range.is_none()
            && self.scopes.is_empty()
            && self.architectures.is_empty()
            && self.elevation.is_empty()
            && self.interactive.is_empty()
            && self.skip_hash_check.is_empty()
            && self.pre_release.is_empty()
            && self.has_custom_parameters.is_empty()
            && self.has_custom_install_location.is_empty()
            && self.has_pre_post_commands.is_empty()
            && self.has_kill_before_operation.is_empty()
            && self.has_uninstall_previous.is_empty()
    }
}

/// Semantic version range for matching.
#[derive(Debug, Clone, Default, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "VersionRange")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct VersionRange {
    /// Minimum version (inclusive).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(min = 1, max = 128))]
    pub min_version: Option<String>,

    /// Maximum version (inclusive).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schemars(length(min = 1, max = 128))]
    pub max_version: Option<String>,

    /// Whether to include pre-release versions.
    #[serde(default)]
    pub include_prerelease: bool,
}

/// Constraints applied after a rule matches.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyConstraints")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyConstraints {
    /// Allow interactive mode.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_interactive: bool,

    /// Allow skipping hash verification.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_skip_hash_check: bool,

    /// Allow pre-release versions.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_pre_release: bool,

    /// Allow custom install location.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_custom_install_location: bool,

    /// Glob patterns for allowed install locations.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[schemars(length(max = 64))]
    pub allowed_install_location_patterns: Vec<StringPattern>,

    /// Allow custom parameters.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_custom_parameters: bool,

    /// Exact allowed custom parameters.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[schemars(length(max = 128))]
    pub allowed_custom_parameters: Vec<CustomParameterString>,

    /// Glob patterns for allowed custom parameters.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[schemars(length(max = 128))]
    pub allowed_custom_parameter_patterns: Vec<CustomParameterString>,

    /// Denied custom parameters (deny takes precedence over allow).
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    #[schemars(length(max = 128))]
    pub denied_custom_parameters: Vec<CustomParameterString>,

    /// Allow pre/post operation commands.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_pre_post_commands: bool,

    /// Allow killing processes before operation.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_kill_before_operation: bool,

    /// Allow uninstalling previous version before installing update.
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_uninstall_previous: bool,

    /// Allow skipping upgrade on install operations if an existing version
    /// is detected (for install operations).
    #[serde(default = "default_true", skip_serializing_if = "is_true")]
    pub allow_upgrade: bool,
}

impl Default for PolicyConstraints {
    fn default() -> Self {
        Self {
            allow_interactive: true,
            allow_skip_hash_check: true,
            allow_pre_release: true,
            allow_custom_install_location: true,
            allowed_install_location_patterns: Vec::new(),
            allow_custom_parameters: true,
            allowed_custom_parameters: Vec::new(),
            allowed_custom_parameter_patterns: Vec::new(),
            denied_custom_parameters: Vec::new(),
            allow_pre_post_commands: true,
            allow_kill_before_operation: true,
            allow_uninstall_previous: true,
            allow_upgrade: true,
        }
    }
}

impl PolicyConstraints {
    /// Returns true if all fields are at their defaults (fully permissive).
    pub fn is_default(&self) -> bool {
        *self == Self::default()
    }
}

fn is_true(v: &bool) -> bool {
    *v
}
