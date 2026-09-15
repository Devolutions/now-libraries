//! Policy model and sample validation tests.

#![allow(clippy::std_instead_of_core, clippy::unwrap_used, unused_crate_dependencies)]

use std::path::PathBuf;

use chrono::{TimeZone, Utc};
use now_policy::{CURRENT_POLICY_FORMAT_VERSION, CustomParameterString, PolicyDocument, StringPattern, VersionString};

fn samples_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("assets/samples")
}

#[test]
fn all_sample_policies_deserialize() {
    let dir = samples_dir();

    let policy_files = [
        "corporate-allowlist.policy.json",
        "deny-risky-options.policy.json",
        "powershell-advanced.policy.json",
        "powershell-current-user.policy.json",
        "scenario-coverage.policy.json",
    ];

    for file in &policy_files {
        let path = dir.join(file);
        let content =
            std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("failed to read {}: {e}", path.display()));
        let _policy: PolicyDocument = serde_json::from_str(&content)
            .unwrap_or_else(|e| panic!("failed to deserialize policy {}: {e}", path.display()));
    }
}

#[test]
fn draft_conversion_omits_and_restores_server_metadata() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let content = std::fs::read_to_string(path).unwrap();
    let committed: PolicyDocument = serde_json::from_str(&content).unwrap();

    let draft = committed.to_draft();
    let draft_json = serde_json::to_value(&draft).unwrap();
    assert!(draft_json.get("$schema").is_none());
    assert_eq!(draft_json["PolicyFormatVersion"], CURRENT_POLICY_FORMAT_VERSION);
    assert!(draft_json["Metadata"].get("Revision").is_none());
    assert!(draft_json["Metadata"].get("PublishedAt").is_none());

    let published_at = Utc.with_ymd_and_hms(2026, 8, 29, 0, 0, 0).unwrap();
    let recommitted = draft.into_policy_document(7, published_at).unwrap();
    let recommitted_json = serde_json::to_value(&recommitted).unwrap();
    assert!(recommitted_json.get("$schema").is_none());
    assert_eq!(recommitted_json["PolicyFormatVersion"], CURRENT_POLICY_FORMAT_VERSION);
    assert_eq!(recommitted.metadata.id.to_string(), committed.metadata.id.to_string());
    assert_eq!(recommitted.metadata.publisher, committed.metadata.publisher);
    assert_eq!(recommitted.metadata.description, committed.metadata.description);
    assert_eq!(recommitted.metadata.support_url, committed.metadata.support_url);
    assert_eq!(recommitted.metadata.valid_from, committed.metadata.valid_from);
    assert_eq!(recommitted.metadata.valid_until, committed.metadata.valid_until);
    assert_eq!(recommitted.metadata.revision, 7);
    assert_eq!(recommitted.metadata.published_at, published_at);
}

#[test]
fn draft_conversion_enforces_revision_bounds() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let committed: PolicyDocument = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    let draft = committed.to_draft();
    let published_at = Utc.with_ymd_and_hms(2026, 8, 29, 0, 0, 0).unwrap();

    assert!(draft.into_policy_document(0, published_at).is_err());

    let draft = committed.to_draft();
    assert!(draft.into_policy_document(2_147_483_647, published_at).is_ok());

    let draft = committed.to_draft();
    assert!(draft.into_policy_document(2_147_483_648, published_at).is_err());
}

#[test]
fn mixed_boolean_match_values_are_rejected() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    value["Rules"][0]["Match"]["Interactive"] = serde_json::json!([false, true]);

    let result: Result<PolicyDocument, _> = serde_json::from_value(value);
    assert!(result.is_err());

    let empty: now_policy::PolicyMatch = serde_json::from_value(serde_json::json!({ "Interactive": [] })).unwrap();
    assert!(empty.interactive.is_empty());

    let mut invalid = now_policy::PolicyMatch::default();
    invalid.interactive.extend([false, true]);
    assert!(serde_json::to_value(invalid).is_err());
}

#[test]
fn policy_text_newtypes_count_unicode_scalars_at_length_boundaries() {
    let multibyte_scalar = "😀";

    assert!(StringPattern::parse(&multibyte_scalar.repeat(256)).is_ok());
    assert!(StringPattern::parse(&multibyte_scalar.repeat(257)).is_err());

    assert!(VersionString::parse(&multibyte_scalar.repeat(128)).is_ok());
    assert!(VersionString::parse(&multibyte_scalar.repeat(129)).is_err());

    assert!(CustomParameterString::parse(&multibyte_scalar.repeat(512)).is_ok());
    assert!(CustomParameterString::parse(&multibyte_scalar.repeat(513)).is_err());
}

#[test]
fn invalid_policy_unknown_field_fails_deserialization() {
    let value = serde_json::json!({
        "PolicyFormatVersion": "1.0.0",
        "PolicyType": "PackageBrokerPolicy",
        "Metadata": {
            "Id": "test",
            "Publisher": "Test",
            "Revision": 1,
            "PublishedAt": "2026-01-01T00:00:00Z"
        },
        "Enforcement": {
            "DefaultDecision": "Deny",
            "RulePrecedence": "PriorityThenDeny",
            "UnknownField": true
        },
        "Rules": []
    });

    let result: Result<PolicyDocument, _> = serde_json::from_value(value);
    assert!(result.is_err(), "policy with unknown field should fail deserialization");
}

#[test]
fn schema_field_is_rejected() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    value["$schema"] = serde_json::json!("https://example.invalid/policy.schema.json");

    let error = serde_json::from_value::<PolicyDocument>(value).unwrap_err().to_string();
    assert!(error.contains("unknown field `$schema`"), "unexpected error: {error}");
}

#[test]
fn unsupported_policy_format_version_is_rejected() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    value["PolicyFormatVersion"] = serde_json::json!("2.0.0");

    let error = serde_json::from_value::<PolicyDocument>(value).unwrap_err().to_string();
    assert!(
        error.contains("unsupported major version 2"),
        "unexpected error: {error}"
    );
}

#[test]
fn malformed_policy_format_versions_are_rejected() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let valid: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();

    for version in ["not-semver", "1.02.3", "1.2.3-beta", "1.0.0\n"] {
        let mut value = valid.clone();
        value["PolicyFormatVersion"] = serde_json::json!(version);
        assert!(
            serde_json::from_value::<PolicyDocument>(value).is_err(),
            "{version:?} should be rejected"
        );
    }
}

#[test]
fn compatible_policy_format_version_is_preserved_by_conversions() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    value["PolicyFormatVersion"] = serde_json::json!("1.2.3");

    let committed = serde_json::from_value::<PolicyDocument>(value).unwrap();
    let draft = committed.to_draft();
    assert_eq!(draft.policy_format_version.to_string(), "1.2.3");
    let recommitted = draft
        .into_policy_document(5, Utc.with_ymd_and_hms(2026, 8, 29, 0, 0, 0).unwrap())
        .unwrap();
    assert_eq!(recommitted.policy_format_version.to_string(), "1.2.3");
}

#[test]
fn policy_format_version_rejects_components_above_100() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    value["PolicyFormatVersion"] = serde_json::json!("1.101.0");

    assert!(serde_json::from_value::<PolicyDocument>(value).is_err());
}

#[test]
fn invalid_policy_fixture_fails_deserialization() {
    let path = samples_dir().join("invalid/policies/invalid-failure-decision.policy.json");
    let content = std::fs::read_to_string(&path).unwrap();
    let result: Result<PolicyDocument, _> = serde_json::from_str(&content);
    assert!(result.is_err(), "invalid policy fixture should fail deserialization");
}

#[test]
fn policy_schema_generates_valid_json() {
    let schema = now_policy::schema::policy_schema_json();
    assert!(schema.is_object());
    let obj = schema.as_object().unwrap();
    assert!(
        obj.contains_key("definitions") || obj.contains_key("$defs"),
        "schema should have type definitions"
    );
}

#[test]
fn policy_schemas_are_repository_local_and_omit_document_schema() {
    for schema in [
        now_policy::schema::policy_schema_json(),
        now_policy::schema::policy_draft_schema_json(),
    ] {
        let properties = schema["properties"].as_object().unwrap();
        let required = schema["required"].as_array().unwrap();
        assert!(schema.get("$id").is_none());
        assert!(!properties.contains_key("$schema"));
        assert!(!required.iter().any(|value| value == "$schema"));

        let version_schema = &schema["definitions"]["PolicyFormatVersion"];
        assert_eq!(version_schema["type"], "string");
        assert!(version_schema["pattern"].as_str().unwrap().starts_with("^1\\."));
    }
}

#[test]
fn committed_policy_enforces_revision_bounds_during_serialization_and_deserialization() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();

    for revision in [serde_json::json!(0), serde_json::json!(2_147_483_648u64)] {
        value["Metadata"]["Revision"] = revision;
        assert!(serde_json::from_value::<PolicyDocument>(value.clone()).is_err());
    }

    value["Metadata"]["Revision"] = serde_json::json!(1);
    let mut policy: PolicyDocument = serde_json::from_value(value).unwrap();
    policy.metadata.revision = 0;
    assert!(serde_json::to_value(policy).is_err());
}

#[test]
fn policy_match_schema_requires_at_least_one_property() {
    let schema = now_policy::schema::policy_schema_json();
    let min_properties = [
        "/definitions/PolicyRule/properties/Match/minProperties",
        "/$defs/PolicyRule/properties/Match/minProperties",
    ]
    .into_iter()
    .find_map(|path| schema.pointer(path).and_then(serde_json::Value::as_u64));

    assert_eq!(min_properties, Some(1));
}

#[test]
fn policy_match_schema_limits_boolean_arrays_to_one_item() {
    let schema = now_policy::schema::policy_schema_json();
    let max_items = [
        "/definitions/PolicyMatch/properties/Interactive/maxItems",
        "/$defs/PolicyMatch/properties/Interactive/maxItems",
    ]
    .into_iter()
    .find_map(|path| schema.pointer(path).and_then(serde_json::Value::as_u64));

    assert_eq!(max_items, Some(1));
}
