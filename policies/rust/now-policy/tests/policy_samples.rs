//! Policy model and sample validation tests.

#![allow(clippy::std_instead_of_core, clippy::unwrap_used, unused_crate_dependencies)]

use std::path::PathBuf;

use chrono::{TimeZone, Utc};
use now_policy::{PolicyDocument, PolicyDraftDocument};

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

    let draft = PolicyDraftDocument::from(&committed);
    let draft_json = serde_json::to_value(&draft).unwrap();
    assert!(draft_json["Metadata"].get("Revision").is_none());
    assert!(draft_json["Metadata"].get("PublishedAt").is_none());

    let published_at = Utc.with_ymd_and_hms(2026, 8, 29, 0, 0, 0).unwrap();
    let recommitted = draft.into_policy_document(7, published_at).unwrap();
    assert_eq!(recommitted.metadata.id.to_string(), committed.metadata.id.to_string());
    assert_eq!(recommitted.metadata.revision, 7);
    assert_eq!(recommitted.metadata.published_at, published_at);
}

#[test]
fn draft_conversion_rejects_zero_revision() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let committed: PolicyDocument = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    let draft = PolicyDraftDocument::from(&committed);
    let published_at = Utc.with_ymd_and_hms(2026, 8, 29, 0, 0, 0).unwrap();

    assert!(draft.into_policy_document(0, published_at).is_err());
}

#[test]
fn mixed_boolean_match_values_are_rejected() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    value["Rules"][0]["Match"]["Interactive"] = serde_json::json!([false, true]);

    let result: Result<PolicyDocument, _> = serde_json::from_value(value);
    assert!(result.is_err());
}

#[test]
fn invalid_policy_unknown_field_fails_deserialization() {
    let value = serde_json::json!({
        "$schema": "https://devolutions.net/schemas/now-policy.schema.1.0.json",
        "PolicyVersion": "1.0.0",
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
