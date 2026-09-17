//! Policy document models.

use std::collections::BTreeSet;

use chrono::{DateTime, Utc};
use schemars::{JsonSchema, Schema, SchemaGenerator, json_schema};
use serde::{Deserialize, Serialize};

use crate::{
    Architecture, CustomParameterString, Decision, Elevation, HttpUrl, ManagerName, ModelValidationError, Operation,
    PackageIdentifier, PolicyFormatVersion, ResourceId, Scope, SemanticVersion, SourceName, StringPattern,
    VersionString,
};

const MAX_POLICY_REVISION: u32 = 2_147_483_647;

/// A policy document governing which package operations are allowed or denied.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyDocument")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyDocument {
    /// Software-managed policy document format version.
    ///
    /// Applications must not expose this field as publisher-authored editable metadata.
    pub policy_format_version: PolicyFormatVersion,

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
            policy_format_version: self.policy_format_version.clone(),
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
    /// Software-managed policy document format version.
    ///
    /// Applications must stamp the current value and must not expose this field
    /// as publisher-authored editable metadata.
    pub policy_format_version: PolicyFormatVersion,

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
            policy_format_version: self.policy_format_version,
            metadata: self.metadata.into_policy_metadata(revision, published_at)?,
            enforcement: self.enforcement,
            rules: self.rules,
        })
    }
}

/// Policy metadata. When both validity bounds are present, `ValidFrom` must be strictly earlier
/// than `ValidUntil`.
#[derive(Debug, Clone, JsonSchema)]
#[schemars(rename = "PolicyMetadata")]
#[schemars(rename_all = "PascalCase")]
#[schemars(deny_unknown_fields)]
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

    /// Earliest instant when the policy is active. When both bounds are present, this must be
    /// strictly earlier than `ValidUntil`.
    pub valid_from: Option<DateTime<Utc>>,

    /// Instant after which the policy is inactive. When both bounds are present, this must be
    /// strictly later than `ValidFrom`.
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

fn deserialize_policy_revision<'de, D: serde::Deserializer<'de>>(deserializer: D) -> Result<u32, D::Error> {
    let revision = u32::deserialize(deserializer)?;
    validate_policy_revision(revision).map_err(serde::de::Error::custom)?;
    Ok(revision)
}

/// Editable policy metadata without server-managed revision and publication time. When both
/// validity bounds are present, `ValidFrom` must be strictly earlier than `ValidUntil`.
#[derive(Debug, Clone, JsonSchema)]
#[schemars(rename = "PolicyDraftMetadata")]
#[schemars(rename_all = "PascalCase")]
#[schemars(deny_unknown_fields)]
pub struct PolicyDraftMetadata {
    /// Unique policy identifier.
    pub id: ResourceId,

    /// Organization that publishes the policy.
    #[schemars(length(min = 1, max = 128))]
    pub publisher: String,

    /// Earliest instant when the policy is active. When both bounds are present, this must be
    /// strictly earlier than `ValidUntil`.
    pub valid_from: Option<DateTime<Utc>>,

    /// Instant after which the policy is inactive. When both bounds are present, this must be
    /// strictly later than `ValidFrom`.
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
    fn into_policy_metadata(
        self,
        revision: u32,
        published_at: DateTime<Utc>,
    ) -> Result<PolicyMetadata, ModelValidationError> {
        validate_validity_window(
            "PolicyDraftMetadata",
            self.valid_from.as_ref(),
            self.valid_until.as_ref(),
        )?;
        Ok(PolicyMetadata {
            id: self.id,
            publisher: self.publisher,
            revision,
            published_at,
            valid_from: self.valid_from,
            valid_until: self.valid_until,
            description: self.description,
            support_url: self.support_url,
        })
    }
}

fn validate_validity_window(
    type_name: &'static str,
    valid_from: Option<&DateTime<Utc>>,
    valid_until: Option<&DateTime<Utc>>,
) -> Result<(), ModelValidationError> {
    if let (Some(valid_from), Some(valid_until)) = (valid_from, valid_until)
        && valid_from >= valid_until
    {
        return Err(ModelValidationError::Invalid {
            type_name,
            reason: "ValidUntil must be strictly later than ValidFrom".to_owned(),
        });
    }
    Ok(())
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
struct PolicyMetadataWire {
    id: ResourceId,
    publisher: String,
    #[serde(deserialize_with = "deserialize_policy_revision")]
    revision: u32,
    published_at: DateTime<Utc>,
    #[serde(default)]
    valid_from: Option<DateTime<Utc>>,
    #[serde(default)]
    valid_until: Option<DateTime<Utc>>,
    #[serde(default)]
    description: Option<String>,
    #[serde(default)]
    support_url: Option<HttpUrl>,
}

impl TryFrom<PolicyMetadataWire> for PolicyMetadata {
    type Error = ModelValidationError;

    fn try_from(value: PolicyMetadataWire) -> Result<Self, Self::Error> {
        validate_validity_window("PolicyMetadata", value.valid_from.as_ref(), value.valid_until.as_ref())?;
        Ok(Self {
            id: value.id,
            publisher: value.publisher,
            revision: value.revision,
            published_at: value.published_at,
            valid_from: value.valid_from,
            valid_until: value.valid_until,
            description: value.description,
            support_url: value.support_url,
        })
    }
}

impl<'de> Deserialize<'de> for PolicyMetadata {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        Self::try_from(PolicyMetadataWire::deserialize(deserializer)?).map_err(serde::de::Error::custom)
    }
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct PolicyMetadataRef<'a> {
    id: &'a ResourceId,
    publisher: &'a str,
    revision: &'a u32,
    published_at: &'a DateTime<Utc>,
    #[serde(skip_serializing_if = "Option::is_none")]
    valid_from: Option<&'a DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    valid_until: Option<&'a DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    description: Option<&'a str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    support_url: Option<&'a HttpUrl>,
}

impl Serialize for PolicyMetadata {
    fn serialize<S: serde::Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        validate_policy_revision(self.revision).map_err(serde::ser::Error::custom)?;
        validate_validity_window("PolicyMetadata", self.valid_from.as_ref(), self.valid_until.as_ref())
            .map_err(serde::ser::Error::custom)?;
        PolicyMetadataRef {
            id: &self.id,
            publisher: &self.publisher,
            revision: &self.revision,
            published_at: &self.published_at,
            valid_from: self.valid_from.as_ref(),
            valid_until: self.valid_until.as_ref(),
            description: self.description.as_deref(),
            support_url: self.support_url.as_ref(),
        }
        .serialize(serializer)
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
struct PolicyDraftMetadataWire {
    id: ResourceId,
    publisher: String,
    #[serde(default)]
    valid_from: Option<DateTime<Utc>>,
    #[serde(default)]
    valid_until: Option<DateTime<Utc>>,
    #[serde(default)]
    description: Option<String>,
    #[serde(default)]
    support_url: Option<HttpUrl>,
}

impl TryFrom<PolicyDraftMetadataWire> for PolicyDraftMetadata {
    type Error = ModelValidationError;

    fn try_from(value: PolicyDraftMetadataWire) -> Result<Self, Self::Error> {
        validate_validity_window(
            "PolicyDraftMetadata",
            value.valid_from.as_ref(),
            value.valid_until.as_ref(),
        )?;
        Ok(Self {
            id: value.id,
            publisher: value.publisher,
            valid_from: value.valid_from,
            valid_until: value.valid_until,
            description: value.description,
            support_url: value.support_url,
        })
    }
}

impl<'de> Deserialize<'de> for PolicyDraftMetadata {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        Self::try_from(PolicyDraftMetadataWire::deserialize(deserializer)?).map_err(serde::de::Error::custom)
    }
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct PolicyDraftMetadataRef<'a> {
    id: &'a ResourceId,
    publisher: &'a str,
    #[serde(skip_serializing_if = "Option::is_none")]
    valid_from: Option<&'a DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    valid_until: Option<&'a DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    description: Option<&'a str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    support_url: Option<&'a HttpUrl>,
}

impl Serialize for PolicyDraftMetadata {
    fn serialize<S: serde::Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        validate_validity_window(
            "PolicyDraftMetadata",
            self.valid_from.as_ref(),
            self.valid_until.as_ref(),
        )
        .map_err(serde::ser::Error::custom)?;
        PolicyDraftMetadataRef {
            id: &self.id,
            publisher: &self.publisher,
            valid_from: self.valid_from.as_ref(),
            valid_until: self.valid_until.as_ref(),
            description: self.description.as_deref(),
            support_url: self.support_url.as_ref(),
        }
        .serialize(serializer)
    }
}

/// Enforcement configuration.
///
/// Matching rules are evaluated by ascending priority. Deny wins equal-priority
/// Allow/Deny ties; remaining equal-priority ties retain document order.
#[derive(Debug, Clone, Serialize, Deserialize, JsonSchema)]
#[schemars(rename = "PolicyEnforcement")]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
pub struct PolicyEnforcement {
    /// Decision when no rule matches.
    pub default_decision: Decision,

    /// When true, broker logs decisions but does not enforce.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub audit_mode: Option<bool>,
}

/// A single policy rule.
#[derive(Debug, Clone, JsonSchema)]
#[schemars(rename = "PolicyRule")]
#[schemars(transform = enforce_allow_constraints_schema)]
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
    /// At least one effective non-null, nonempty criterion must be present.
    #[serde(rename = "Match", deserialize_with = "deserialize_non_empty_match")]
    #[schemars(with = "NonEmptyPolicyMatchSchema")]
    pub match_criteria: PolicyMatch,

    /// Additional safety limits applied after an Allow rule matches.
    /// Constraints are invalid on Deny rules.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub constraints: Option<PolicyConstraints>,
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
struct PolicyRuleWire {
    id: ResourceId,
    #[serde(default = "default_true")]
    enabled: bool,
    priority: u32,
    decision: Decision,
    #[serde(default)]
    reason: Option<String>,
    #[serde(rename = "Match", deserialize_with = "deserialize_non_empty_match")]
    match_criteria: PolicyMatch,
    #[serde(default)]
    constraints: Option<PolicyConstraints>,
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct PolicyRuleRef<'a> {
    id: &'a ResourceId,
    enabled: bool,
    priority: u32,
    decision: Decision,
    #[serde(skip_serializing_if = "Option::is_none")]
    reason: Option<&'a String>,
    #[serde(rename = "Match")]
    match_criteria: &'a PolicyMatch,
    #[serde(skip_serializing_if = "Option::is_none")]
    constraints: Option<&'a PolicyConstraints>,
}

fn default_true() -> bool {
    true
}

fn validate_policy_rule(
    decision: Decision,
    match_criteria: &PolicyMatch,
    constraints: Option<&PolicyConstraints>,
) -> Result<(), &'static str> {
    if match_criteria.is_empty() {
        return Err("PolicyRule.Match must contain at least one effective criterion");
    }
    if decision == Decision::Deny && constraints.is_some() {
        return Err("PolicyRule.Constraints are valid only when Decision is Allow");
    }
    if !match_criteria.source_names.is_empty() && match_criteria.managers.len() != 1 {
        return Err("PolicyRule.Match.SourceNames requires exactly one PolicyRule.Match.Managers value");
    }

    Ok(())
}

impl TryFrom<PolicyRuleWire> for PolicyRule {
    type Error = &'static str;

    fn try_from(value: PolicyRuleWire) -> Result<Self, Self::Error> {
        validate_policy_rule(value.decision, &value.match_criteria, value.constraints.as_ref())?;
        Ok(Self {
            id: value.id,
            enabled: value.enabled,
            priority: value.priority,
            decision: value.decision,
            reason: value.reason,
            match_criteria: value.match_criteria,
            constraints: value.constraints,
        })
    }
}

impl<'de> Deserialize<'de> for PolicyRule {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        Self::try_from(PolicyRuleWire::deserialize(deserializer)?).map_err(serde::de::Error::custom)
    }
}

impl Serialize for PolicyRule {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        validate_policy_rule(self.decision, &self.match_criteria, self.constraints.as_ref())
            .map_err(serde::ser::Error::custom)?;
        PolicyRuleRef {
            id: &self.id,
            enabled: self.enabled,
            priority: self.priority,
            decision: self.decision,
            reason: self.reason.as_ref(),
            match_criteria: &self.match_criteria,
            constraints: self.constraints.as_ref(),
        }
        .serialize(serializer)
    }
}

fn enforce_allow_constraints_schema(schema: &mut Schema) {
    schema
        .as_object_mut()
        .expect("PolicyRule schema should be an object")
        .extend(
            json_schema!({
                "not": {
                    "required": ["Decision", "Constraints"],
                    "properties": {
                        "Decision": { "enum": ["Deny"] },
                        "Constraints": { "type": "object" }
                    }
                }
            })
            .as_object()
            .expect("PolicyRule conditional schema should be an object")
            .clone(),
        );
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
        let mut schema = PolicyMatch::json_schema(generator);
        let schema_object = schema.as_object_mut().expect("PolicyMatch schema should be an object");
        schema_object.insert("minProperties".to_owned(), serde_json::json!(1));
        schema_object.insert(
            "anyOf".to_owned(),
            serde_json::json!([
                { "required": ["Operations"], "properties": { "Operations": { "minItems": 1 } } },
                { "required": ["Managers"], "properties": { "Managers": { "minItems": 1 } } },
                { "required": ["SourceNames"], "properties": { "SourceNames": { "minItems": 1 } } },
                { "required": ["PackageIdentifiers"], "properties": { "PackageIdentifiers": { "type": "object" } } },
                { "required": ["Version"], "properties": { "Version": { "type": "object" } } },
                { "required": ["Scopes"], "properties": { "Scopes": { "minItems": 1 } } },
                { "required": ["Architectures"], "properties": { "Architectures": { "minItems": 1 } } },
                { "required": ["ExecutionElevation"], "properties": { "ExecutionElevation": { "minItems": 1 } } },
                { "required": ["Interactive"], "properties": { "Interactive": { "type": "boolean" } } },
                { "required": ["SkipHashCheck"], "properties": { "SkipHashCheck": { "type": "boolean" } } },
                { "required": ["PreRelease"], "properties": { "PreRelease": { "type": "boolean" } } },
                { "required": ["HasCustomParameters"], "properties": { "HasCustomParameters": { "type": "boolean" } } },
                { "required": ["HasCustomInstallLocation"], "properties": { "HasCustomInstallLocation": { "type": "boolean" } } },
                { "required": ["HasPrePostCommands"], "properties": { "HasPrePostCommands": { "type": "boolean" } } },
                { "required": ["HasKillBeforeOperation"], "properties": { "HasKillBeforeOperation": { "type": "boolean" } } },
                { "required": ["HasUninstallPrevious"], "properties": { "HasUninstallPrevious": { "type": "boolean" } } }
            ]),
        );
        schema
    }
}

/// Match criteria for a policy rule. All specified fields must match.
/// At least one effective non-null, nonempty criterion must be present.
#[derive(Debug, Clone, Default, JsonSchema)]
#[schemars(rename = "PolicyMatch")]
#[schemars(rename_all = "PascalCase")]
#[schemars(deny_unknown_fields)]
#[schemars(transform = enforce_source_names_schema)]
pub struct PolicyMatch {
    /// Optional operation filter. Omitted or empty does not narrow matching;
    /// canonical serialization omits an empty collection.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 3))]
    pub operations: BTreeSet<Operation>,

    /// Optional manager filter. Omitted or empty does not narrow matching;
    /// canonical serialization omits an empty collection.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 16))]
    pub managers: BTreeSet<ManagerName>,

    /// Optional exact configured-source-name filter. Matching uses the selected package manager's
    /// source-name comparison semantics; wildcard characters are literal. Nonempty source names
    /// require exactly one manager. Omitted or empty does not narrow matching; canonical
    /// serialization omits an empty collection.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 128))]
    pub source_names: BTreeSet<SourceName>,

    /// Optional package-identifier condition. Exact uses validated stable identifiers; Patterns
    /// uses explicit wildcard patterns that may authorize multiple packages. Absent does not
    /// narrow matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub package_identifiers: Option<PackageIdentifierCondition>,

    /// Optional package-version condition. Exact supports arbitrary package version strings;
    /// Range applies only to semantic versions. Absent does not narrow matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub version: Option<VersionCondition>,

    /// Optional scope filter. Omitted or empty does not narrow matching;
    /// canonical serialization omits an empty collection.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 2))]
    pub scopes: BTreeSet<Scope>,

    /// Optional architecture filter. Omitted or empty does not narrow matching;
    /// canonical serialization omits an empty collection.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 5))]
    pub architectures: BTreeSet<Architecture>,

    /// Optional effective execution-elevation filter. Elevated means the package operation
    /// will run with administrator privileges; Standard means it will not. Omitted or empty
    /// does not narrow matching; canonical serialization omits an empty collection.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    #[schemars(length(max = 2))]
    pub execution_elevation: BTreeSet<Elevation>,

    /// Optional condition on the request's interactive characteristic.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub interactive: Option<bool>,

    /// Optional condition on the request's skipHashCheck characteristic.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub skip_hash_check: Option<bool>,

    /// Optional condition on the request's preRelease characteristic.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub pre_release: Option<bool>,

    /// Optional condition on whether the request has custom parameters.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub has_custom_parameters: Option<bool>,

    /// Optional condition on whether the request has a custom install location.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub has_custom_install_location: Option<bool>,

    /// Optional condition on whether the request has pre/post commands.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub has_pre_post_commands: Option<bool>,

    /// Optional condition on whether the request has kill-before-operation entries.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub has_kill_before_operation: Option<bool>,

    /// Optional condition on whether the request enables uninstall-previous.
    /// Absent means this characteristic does not affect matching.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub has_uninstall_previous: Option<bool>,
}

fn enforce_source_names_schema(schema: &mut Schema) {
    let schema = schema.as_object_mut().expect("PolicyMatch schema should be an object");
    schema.insert(
        "if".to_owned(),
        serde_json::json!({
            "required": ["SourceNames"],
            "properties": {
                "SourceNames": { "minItems": 1 }
            }
        }),
    );
    schema.insert(
        "then".to_owned(),
        serde_json::json!({
            "required": ["Managers"],
            "properties": {
                "Managers": { "minItems": 1, "maxItems": 1 }
            }
        }),
    );
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
struct PolicyMatchWire {
    #[serde(default)]
    operations: Vec<Operation>,
    #[serde(default)]
    managers: Vec<ManagerName>,
    #[serde(default)]
    source_names: Vec<SourceName>,
    #[serde(default)]
    package_identifiers: Option<PackageIdentifierCondition>,
    #[serde(default)]
    version: Option<VersionCondition>,
    #[serde(default)]
    scopes: Vec<Scope>,
    #[serde(default)]
    architectures: Vec<Architecture>,
    #[serde(default)]
    execution_elevation: Vec<Elevation>,
    #[serde(default)]
    interactive: Option<bool>,
    #[serde(default)]
    skip_hash_check: Option<bool>,
    #[serde(default)]
    pre_release: Option<bool>,
    #[serde(default)]
    has_custom_parameters: Option<bool>,
    #[serde(default)]
    has_custom_install_location: Option<bool>,
    #[serde(default)]
    has_pre_post_commands: Option<bool>,
    #[serde(default)]
    has_kill_before_operation: Option<bool>,
    #[serde(default)]
    has_uninstall_previous: Option<bool>,
}

#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
struct PolicyMatchRef<'a> {
    #[serde(skip_serializing_if = "BTreeSet::is_empty")]
    operations: &'a BTreeSet<Operation>,
    #[serde(skip_serializing_if = "BTreeSet::is_empty")]
    managers: &'a BTreeSet<ManagerName>,
    #[serde(skip_serializing_if = "BTreeSet::is_empty")]
    source_names: &'a BTreeSet<SourceName>,
    #[serde(skip_serializing_if = "Option::is_none")]
    package_identifiers: Option<&'a PackageIdentifierCondition>,
    #[serde(skip_serializing_if = "Option::is_none")]
    version: Option<&'a VersionCondition>,
    #[serde(skip_serializing_if = "BTreeSet::is_empty")]
    scopes: &'a BTreeSet<Scope>,
    #[serde(skip_serializing_if = "BTreeSet::is_empty")]
    architectures: &'a BTreeSet<Architecture>,
    #[serde(skip_serializing_if = "BTreeSet::is_empty")]
    execution_elevation: &'a BTreeSet<Elevation>,
    #[serde(skip_serializing_if = "Option::is_none")]
    interactive: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    skip_hash_check: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pre_release: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    has_custom_parameters: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    has_custom_install_location: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    has_pre_post_commands: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    has_kill_before_operation: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    has_uninstall_previous: Option<bool>,
}

const MAX_MANAGERS: usize = 16;
const MAX_SOURCE_NAMES: usize = 128;

fn validate_policy_match(value: &PolicyMatch) -> Result<(), &'static str> {
    if value.managers.len() > MAX_MANAGERS {
        return Err("PolicyMatch.Managers must contain at most 16 values");
    }
    if value.source_names.len() > MAX_SOURCE_NAMES {
        return Err("PolicyMatch.SourceNames must contain at most 128 values");
    }
    if !value.source_names.is_empty() && value.managers.len() != 1 {
        return Err("PolicyMatch.SourceNames requires exactly one PolicyMatch.Managers value");
    }
    Ok(())
}

fn reject_duplicate_values<T: Ord>(values: &[T], path: &'static str) -> Result<(), &'static str> {
    let unique = values.iter().collect::<BTreeSet<_>>();
    if unique.len() != values.len() {
        return Err(path);
    }
    Ok(())
}

impl TryFrom<PolicyMatchWire> for PolicyMatch {
    type Error = &'static str;

    fn try_from(value: PolicyMatchWire) -> Result<Self, Self::Error> {
        reject_duplicate_values(
            &value.operations,
            "PolicyMatch.Operations must not contain duplicate values",
        )?;
        reject_duplicate_values(
            &value.managers,
            "PolicyMatch.Managers must not contain duplicate values",
        )?;
        if value.managers.len() > MAX_MANAGERS {
            return Err("PolicyMatch.Managers must contain at most 16 values");
        }
        reject_duplicate_values(
            &value.source_names,
            "PolicyMatch.SourceNames must not contain duplicate values",
        )?;
        if value.source_names.len() > MAX_SOURCE_NAMES {
            return Err("PolicyMatch.SourceNames must contain at most 128 values");
        }
        reject_duplicate_values(&value.scopes, "PolicyMatch.Scopes must not contain duplicate values")?;
        reject_duplicate_values(
            &value.architectures,
            "PolicyMatch.Architectures must not contain duplicate values",
        )?;
        reject_duplicate_values(
            &value.execution_elevation,
            "PolicyMatch.ExecutionElevation must not contain duplicate values",
        )?;

        let result = Self {
            operations: value.operations.into_iter().collect(),
            managers: value.managers.into_iter().collect(),
            source_names: value.source_names.into_iter().collect(),
            package_identifiers: value.package_identifiers,
            version: value.version,
            scopes: value.scopes.into_iter().collect(),
            architectures: value.architectures.into_iter().collect(),
            execution_elevation: value.execution_elevation.into_iter().collect(),
            interactive: value.interactive,
            skip_hash_check: value.skip_hash_check,
            pre_release: value.pre_release,
            has_custom_parameters: value.has_custom_parameters,
            has_custom_install_location: value.has_custom_install_location,
            has_pre_post_commands: value.has_pre_post_commands,
            has_kill_before_operation: value.has_kill_before_operation,
            has_uninstall_previous: value.has_uninstall_previous,
        };
        validate_policy_match(&result)?;
        Ok(result)
    }
}

impl<'de> Deserialize<'de> for PolicyMatch {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        Self::try_from(PolicyMatchWire::deserialize(deserializer)?).map_err(serde::de::Error::custom)
    }
}

impl Serialize for PolicyMatch {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        validate_policy_match(self).map_err(serde::ser::Error::custom)?;
        PolicyMatchRef {
            operations: &self.operations,
            managers: &self.managers,
            source_names: &self.source_names,
            package_identifiers: self.package_identifiers.as_ref(),
            version: self.version.as_ref(),
            scopes: &self.scopes,
            architectures: &self.architectures,
            execution_elevation: &self.execution_elevation,
            interactive: self.interactive,
            skip_hash_check: self.skip_hash_check,
            pre_release: self.pre_release,
            has_custom_parameters: self.has_custom_parameters,
            has_custom_install_location: self.has_custom_install_location,
            has_pre_post_commands: self.has_pre_post_commands,
            has_kill_before_operation: self.has_kill_before_operation,
            has_uninstall_previous: self.has_uninstall_previous,
        }
        .serialize(serializer)
    }
}

impl PolicyMatch {
    /// Returns true if no criteria are specified.
    pub fn is_empty(&self) -> bool {
        self.operations.is_empty()
            && self.managers.is_empty()
            && self.source_names.is_empty()
            && self.package_identifiers.is_none()
            && self.version.is_none()
            && self.scopes.is_empty()
            && self.architectures.is_empty()
            && self.execution_elevation.is_empty()
            && self.interactive.is_none()
            && self.skip_hash_check.is_none()
            && self.pre_release.is_none()
            && self.has_custom_parameters.is_none()
            && self.has_custom_install_location.is_none()
            && self.has_pre_post_commands.is_none()
            && self.has_kill_before_operation.is_none()
            && self.has_uninstall_previous.is_none()
    }
}

/// Mutually exclusive package-identifier matching mode.
#[derive(Debug, Clone)]
pub enum PackageIdentifierCondition {
    Exact(BTreeSet<PackageIdentifier>),
    Patterns(BTreeSet<StringPattern>),
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
enum PackageIdentifierConditionWire {
    Exact(Vec<PackageIdentifier>),
    Patterns(Vec<StringPattern>),
}

impl<'de> Deserialize<'de> for PackageIdentifierCondition {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        match PackageIdentifierConditionWire::deserialize(deserializer)? {
            PackageIdentifierConditionWire::Exact(values) if values.is_empty() || values.len() > 1024 => Err(
                serde::de::Error::custom("PackageIdentifiers.Exact must contain between 1 and 1024 values"),
            ),
            PackageIdentifierConditionWire::Exact(values) => {
                reject_duplicate_values(&values, "PackageIdentifiers.Exact must not contain duplicate values")
                    .map_err(serde::de::Error::custom)?;
                Ok(Self::Exact(values.into_iter().collect()))
            }
            PackageIdentifierConditionWire::Patterns(values) if values.is_empty() || values.len() > 1024 => Err(
                serde::de::Error::custom("PackageIdentifiers.Patterns must contain between 1 and 1024 values"),
            ),
            PackageIdentifierConditionWire::Patterns(values) => {
                reject_duplicate_values(&values, "PackageIdentifiers.Patterns must not contain duplicate values")
                    .map_err(serde::de::Error::custom)?;
                Ok(Self::Patterns(values.into_iter().collect()))
            }
        }
    }
}

impl Serialize for PackageIdentifierCondition {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        match self {
            Self::Exact(values) if values.is_empty() || values.len() > 1024 => Err(serde::ser::Error::custom(
                "PackageIdentifiers.Exact must contain between 1 and 1024 values",
            )),
            Self::Exact(values) => {
                #[derive(Serialize)]
                #[serde(rename_all = "PascalCase")]
                enum ExactRef<'a> {
                    Exact(&'a BTreeSet<PackageIdentifier>),
                }
                ExactRef::Exact(values).serialize(serializer)
            }
            Self::Patterns(values) if values.is_empty() || values.len() > 1024 => Err(serde::ser::Error::custom(
                "PackageIdentifiers.Patterns must contain between 1 and 1024 values",
            )),
            Self::Patterns(values) => {
                #[derive(Serialize)]
                #[serde(rename_all = "PascalCase")]
                enum PatternsRef<'a> {
                    Patterns(&'a BTreeSet<StringPattern>),
                }
                PatternsRef::Patterns(values).serialize(serializer)
            }
        }
    }
}

impl JsonSchema for PackageIdentifierCondition {
    fn schema_name() -> std::borrow::Cow<'static, str> {
        "PackageIdentifierCondition".into()
    }

    fn json_schema(generator: &mut SchemaGenerator) -> Schema {
        json_schema!({
            "description": "Exactly one package-identifier mode. Exact authorizes stable identifiers; Patterns explicitly authorizes every identifier matched by a wildcard pattern.",
            "oneOf": [
                {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["Exact"],
                    "properties": {
                        "Exact": {
                            "type": "array",
                            "minItems": 1,
                            "maxItems": 1024,
                            "uniqueItems": true,
                            "items": generator.subschema_for::<PackageIdentifier>()
                        }
                    }
                },
                {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["Patterns"],
                    "properties": {
                        "Patterns": {
                            "type": "array",
                            "minItems": 1,
                            "maxItems": 1024,
                            "uniqueItems": true,
                            "items": generator.subschema_for::<StringPattern>()
                        }
                    }
                }
            ]
        })
    }
}

/// Mutually exclusive package-version condition.
#[derive(Debug, Clone)]
pub enum VersionCondition {
    /// One or more exact package version strings. Values need not be semantic versions.
    Exact(BTreeSet<VersionString>),
    /// Semantic-version range.
    Range(VersionRange),
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
enum VersionConditionWire {
    Exact(Vec<VersionString>),
    Range(VersionRange),
}

impl<'de> Deserialize<'de> for VersionCondition {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        match VersionConditionWire::deserialize(deserializer)? {
            VersionConditionWire::Exact(values) if values.is_empty() || values.len() > 256 => Err(
                serde::de::Error::custom("Version.Exact must contain between 1 and 256 values"),
            ),
            VersionConditionWire::Exact(values) => {
                reject_duplicate_values(&values, "Version.Exact must not contain duplicate values")
                    .map_err(serde::de::Error::custom)?;
                Ok(Self::Exact(values.into_iter().collect()))
            }
            VersionConditionWire::Range(range) => Ok(Self::Range(range)),
        }
    }
}

impl Serialize for VersionCondition {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        match self {
            Self::Exact(values) if values.is_empty() || values.len() > 256 => Err(serde::ser::Error::custom(
                "Version.Exact must contain between 1 and 256 values",
            )),
            Self::Exact(values) => {
                #[derive(Serialize)]
                #[serde(rename_all = "PascalCase")]
                enum ExactRef<'a> {
                    Exact(&'a BTreeSet<VersionString>),
                }
                ExactRef::Exact(values).serialize(serializer)
            }
            Self::Range(range) => {
                #[derive(Serialize)]
                #[serde(rename_all = "PascalCase")]
                enum RangeRef<'a> {
                    Range(&'a VersionRange),
                }
                RangeRef::Range(range).serialize(serializer)
            }
        }
    }
}

impl JsonSchema for VersionCondition {
    fn schema_name() -> std::borrow::Cow<'static, str> {
        "VersionCondition".into()
    }

    fn json_schema(generator: &mut SchemaGenerator) -> Schema {
        json_schema!({
            "description": "Exactly one package-version mode. Exact accepts arbitrary package version strings; Range applies only to semantic versions.",
            "oneOf": [
                {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["Exact"],
                    "properties": {
                        "Exact": {
                            "type": "array",
                            "minItems": 1,
                            "maxItems": 256,
                            "uniqueItems": true,
                            "items": generator.subschema_for::<VersionString>()
                        }
                    }
                },
                {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["Range"],
                    "properties": {
                        "Range": generator.subschema_for::<VersionRange>()
                    }
                }
            ]
        })
    }
}

/// Nonempty semantic-version range for matching.
#[derive(Debug, Clone, Default, JsonSchema)]
#[schemars(rename = "VersionRange")]
#[schemars(rename_all = "PascalCase")]
#[schemars(deny_unknown_fields)]
#[schemars(transform = require_version_range_boundary)]
pub struct VersionRange {
    /// Minimum version (inclusive).
    pub min_version: Option<SemanticVersion>,

    /// Maximum version (inclusive).
    pub max_version: Option<SemanticVersion>,

    /// Whether to include pre-release versions.
    #[serde(default)]
    pub include_prerelease: bool,
}

#[derive(Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(deny_unknown_fields)]
struct VersionRangeWire {
    #[serde(default, skip_serializing_if = "Option::is_none")]
    min_version: Option<SemanticVersion>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    max_version: Option<SemanticVersion>,
    #[serde(default)]
    include_prerelease: bool,
}

fn validate_version_range(range: &VersionRange) -> Result<(), &'static str> {
    if range.min_version.is_none() && range.max_version.is_none() {
        return Err("Version.Range must specify MinVersion or MaxVersion");
    }
    for version in [range.min_version.as_ref(), range.max_version.as_ref()]
        .into_iter()
        .flatten()
    {
        if SemanticVersion::parse(version).is_err() {
            return Err("Version.Range boundaries must be canonical semantic versions");
        }
    }
    Ok(())
}

impl<'de> Deserialize<'de> for VersionRange {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        let wire = VersionRangeWire::deserialize(deserializer)?;
        let range = Self {
            min_version: wire.min_version,
            max_version: wire.max_version,
            include_prerelease: wire.include_prerelease,
        };
        validate_version_range(&range).map_err(serde::de::Error::custom)?;
        Ok(range)
    }
}

impl Serialize for VersionRange {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        validate_version_range(self).map_err(serde::ser::Error::custom)?;
        VersionRangeWire {
            min_version: self.min_version.clone(),
            max_version: self.max_version.clone(),
            include_prerelease: self.include_prerelease,
        }
        .serialize(serializer)
    }
}

fn require_version_range_boundary(schema: &mut Schema) {
    schema
        .as_object_mut()
        .expect("VersionRange schema should be an object")
        .insert(
            "anyOf".to_owned(),
            serde_json::json!([
                { "required": ["MinVersion"], "properties": { "MinVersion": { "type": "string" } } },
                { "required": ["MaxVersion"], "properties": { "MaxVersion": { "type": "string" } } }
            ]),
        );
}

/// Additional safety limits applied after an Allow rule matches.
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
