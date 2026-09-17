//! Policy model and sample validation tests.

#![allow(clippy::std_instead_of_core, clippy::unwrap_used, unused_crate_dependencies)]

use std::path::PathBuf;

use chrono::{TimeZone, Utc};
use now_policy::{
    CURRENT_POLICY_FORMAT_VERSION, CustomParameterString, PackageIdentifier, PolicyDocument, SourceName, StringPattern,
    VersionString,
};

fn samples_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("assets/samples")
}

#[test]
fn all_sample_policies_deserialize() {
    let dir = samples_dir();

    let policy_files = [
        "boolean-characteristics.policy.json",
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
fn boolean_match_characteristics_accept_omitted_null_false_and_true() {
    let property_names = [
        "Interactive",
        "SkipHashCheck",
        "PreRelease",
        "HasCustomParameters",
        "HasCustomInstallLocation",
        "HasPrePostCommands",
        "HasKillBeforeOperation",
        "HasUninstallPrevious",
    ];

    let omitted: now_policy::PolicyMatch = serde_json::from_str("{}").unwrap();
    let omitted_json = serde_json::to_value(omitted).unwrap();
    for property_name in property_names {
        assert!(omitted_json.get(property_name).is_none());

        let explicit_null: now_policy::PolicyMatch =
            serde_json::from_str(&format!(r#"{{"{property_name}":null}}"#)).unwrap();
        assert!(
            serde_json::to_value(explicit_null)
                .unwrap()
                .get(property_name)
                .is_none()
        );

        for expected in [false, true] {
            let value: now_policy::PolicyMatch =
                serde_json::from_str(&format!(r#"{{"{property_name}":{expected}}}"#)).unwrap();
            assert_eq!(serde_json::to_value(value).unwrap()[property_name], expected);
        }
    }
}

#[test]
fn boolean_match_characteristics_reject_legacy_arrays_and_wrong_types() {
    let property_names = [
        "Interactive",
        "SkipHashCheck",
        "PreRelease",
        "HasCustomParameters",
        "HasCustomInstallLocation",
        "HasPrePostCommands",
        "HasKillBeforeOperation",
        "HasUninstallPrevious",
    ];

    for property_name in property_names {
        for invalid_value in ["[]", "[false]", "[true]", "[false,true]", "\"true\"", "0", "{}"] {
            let json = format!(r#"{{"{property_name}":{invalid_value}}}"#);
            assert!(
                serde_json::from_str::<now_policy::PolicyMatch>(&json).is_err(),
                "{property_name} should reject {invalid_value}"
            );
        }
    }
}

#[test]
fn null_only_boolean_match_is_not_an_effective_rule_criterion() {
    let property_names = [
        "Interactive",
        "SkipHashCheck",
        "PreRelease",
        "HasCustomParameters",
        "HasCustomInstallLocation",
        "HasPrePostCommands",
        "HasKillBeforeOperation",
        "HasUninstallPrevious",
    ];

    for property_name in property_names {
        let null_only =
            format!(r#"{{"Id":"test.rule","Priority":1,"Decision":"Allow","Match":{{"{property_name}":null}}}}"#);
        assert!(serde_json::from_str::<now_policy::PolicyRule>(&null_only).is_err());

        let with_operation = format!(
            r#"{{"Id":"test.rule","Priority":1,"Decision":"Allow","Match":{{"Operations":["Install"],"{property_name}":null}}}}"#
        );
        assert!(serde_json::from_str::<now_policy::PolicyRule>(&with_operation).is_ok());
    }
}

#[test]
fn constraints_are_valid_only_for_allow_rules() {
    let allow_with_constraints = r#"{
        "Id":"allow.rule",
        "Priority":1,
        "Decision":"Allow",
        "Match":{"Operations":["Install"]},
        "Constraints":{"AllowInteractive":false}
    }"#;
    let allow: now_policy::PolicyRule = serde_json::from_str(allow_with_constraints).unwrap();
    assert!(allow.constraints.is_some());
    assert!(serde_json::to_value(&allow).unwrap().get("Constraints").is_some());

    let allow_without_constraints = r#"{
        "Id":"allow.rule",
        "Priority":1,
        "Decision":"Allow",
        "Match":{"Operations":["Install"]}
    }"#;
    assert!(serde_json::from_str::<now_policy::PolicyRule>(allow_without_constraints).is_ok());

    let deny_without_constraints = r#"{
        "Id":"deny.rule",
        "Priority":1,
        "Decision":"Deny",
        "Match":{"Operations":["Install"]}
    }"#;
    assert!(serde_json::from_str::<now_policy::PolicyRule>(deny_without_constraints).is_ok());

    let deny_with_null_constraints = r#"{
        "Id":"deny.rule",
        "Priority":1,
        "Decision":"Deny",
        "Match":{"Operations":["Install"]},
        "Constraints":null
    }"#;
    let deny: now_policy::PolicyRule = serde_json::from_str(deny_with_null_constraints).unwrap();
    assert!(serde_json::to_value(&deny).unwrap().get("Constraints").is_none());

    let deny_with_constraints = r#"{
        "Id":"deny.rule",
        "Enabled":false,
        "Priority":1,
        "Decision":"Deny",
        "Match":{"Operations":["Install"]},
        "Constraints":{"AllowInteractive":false}
    }"#;
    let error = serde_json::from_str::<now_policy::PolicyRule>(deny_with_constraints)
        .unwrap_err()
        .to_string();
    assert!(error.contains("PolicyRule.Constraints"), "unexpected error: {error}");

    let mut invalid = allow;
    invalid.decision = now_policy::Decision::Deny;
    let error = serde_json::to_value(invalid).unwrap_err().to_string();
    assert!(error.contains("PolicyRule.Constraints"), "unexpected error: {error}");
}

#[test]
fn boolean_match_characteristics_round_trip_in_representative_mixed_match() {
    let json = serde_json::json!({
        "Operations": ["Install"],
        "Interactive": false,
        "SkipHashCheck": true,
        "HasCustomParameters": false,
        "HasUninstallPrevious": true
    });
    let value: now_policy::PolicyMatch = serde_json::from_value(json).unwrap();

    assert_eq!(value.interactive, Some(false));
    assert_eq!(value.skip_hash_check, Some(true));
    assert_eq!(value.has_custom_parameters, Some(false));
    assert_eq!(value.has_uninstall_previous, Some(true));
    assert_eq!(value.pre_release, None);
    assert_eq!(value.has_custom_install_location, None);
    assert_eq!(value.has_pre_post_commands, None);
    assert_eq!(value.has_kill_before_operation, None);

    let serialized = serde_json::to_value(value).unwrap();
    assert_eq!(serialized["Interactive"], false);
    assert_eq!(serialized["SkipHashCheck"], true);
    assert!(serialized.get("PreRelease").is_none());
    assert!(serialized.get("HasCustomInstallLocation").is_none());
}

#[test]
fn collection_match_filters_accept_empty_input_and_canonicalize_to_omitted() {
    let properties = [
        ("Operations", "\"Install\""),
        ("Managers", "\"Winget\""),
        ("SourceNames", "\"winget\""),
        ("Scopes", "\"User\""),
        ("Architectures", "\"X64\""),
        ("ExecutionElevation", "\"Standard\""),
    ];

    for (property_name, element_json) in properties {
        let omitted: now_policy::PolicyMatch = serde_json::from_str("{}").unwrap();
        assert!(serde_json::to_value(omitted).unwrap().get(property_name).is_none());

        let empty: now_policy::PolicyMatch = serde_json::from_str(&format!(r#"{{"{property_name}":[]}}"#)).unwrap();
        assert!(serde_json::to_value(empty).unwrap().get(property_name).is_none());

        let populated_json = format!(
            r#"{{"{property_name}":[{element_json}]{} }}"#,
            if property_name == "SourceNames" {
                r#","Managers":["Winget"]"#
            } else {
                ""
            }
        );
        let populated: now_policy::PolicyMatch = serde_json::from_str(&populated_json).unwrap();
        assert_eq!(
            serde_json::to_value(populated).unwrap()[property_name]
                .as_array()
                .map(Vec::len),
            Some(1)
        );
    }
}

#[test]
fn empty_collection_only_match_is_not_an_effective_rule_criterion() {
    let properties = [
        ("Operations", "\"Install\""),
        ("Managers", "\"Winget\""),
        ("SourceNames", "\"winget\""),
        ("Scopes", "\"User\""),
        ("Architectures", "\"X64\""),
        ("ExecutionElevation", "\"Standard\""),
    ];

    for (property_name, element_json) in properties {
        let empty_only =
            format!(r#"{{"Id":"test.rule","Priority":1,"Decision":"Allow","Match":{{"{property_name}":[]}}}}"#);
        assert!(serde_json::from_str::<now_policy::PolicyRule>(&empty_only).is_err());

        let with_boolean = format!(
            r#"{{"Id":"test.rule","Priority":1,"Decision":"Allow","Match":{{"{property_name}":[],"Interactive":false}}}}"#
        );
        let rule: now_policy::PolicyRule = serde_json::from_str(&with_boolean).unwrap();
        let serialized = serde_json::to_value(rule).unwrap();
        assert!(serialized["Match"].get(property_name).is_none());
        assert_eq!(serialized["Match"]["Interactive"], false);

        let populated = format!(
            r#"{{"Id":"test.rule","Priority":1,"Decision":"Allow","Match":{{"{property_name}":[{element_json}]{} }}}}"#,
            if property_name == "SourceNames" {
                r#","Managers":["Winget"]"#
            } else {
                ""
            }
        );
        assert!(serde_json::from_str::<now_policy::PolicyRule>(&populated).is_ok());
    }
}

#[test]
fn collection_match_filters_reject_duplicate_values() {
    let properties = [
        ("Operations", "\"Install\""),
        ("Managers", "\"Winget\""),
        ("SourceNames", "\"winget\""),
        ("Scopes", "\"User\""),
        ("Architectures", "\"X64\""),
        ("ExecutionElevation", "\"Standard\""),
    ];

    for (property_name, element_json) in properties {
        let json = format!(
            r#"{{"{property_name}":[{element_json},{element_json}]{} }}"#,
            if property_name == "SourceNames" {
                r#","Managers":["Winget"]"#
            } else {
                ""
            }
        );
        assert!(
            serde_json::from_str::<now_policy::PolicyMatch>(&json).is_err(),
            "{property_name} should reject duplicate values"
        );
    }
}

#[test]
fn source_names_require_managers_and_preserve_exact_literal_names() {
    let without_manager = r#"{
        "Id":"source.rule",
        "Priority":1,
        "Decision":"Allow",
        "Match":{"SourceNames":["corp*"]}
    }"#;
    let error = serde_json::from_str::<now_policy::PolicyRule>(without_manager)
        .unwrap_err()
        .to_string();
    assert!(error.contains("SourceNames"), "unexpected error: {error}");

    let with_manager = r#"{
        "Id":"source.rule",
        "Priority":1,
        "Decision":"Allow",
        "Match":{
            "Managers":["Winget"],
            "SourceNames":["corp*","PSGallery"]
        }
    }"#;
    let mut rule: now_policy::PolicyRule = serde_json::from_str(with_manager).unwrap();
    assert_eq!(rule.match_criteria.managers.len(), 1);
    assert_eq!(rule.match_criteria.source_names.len(), 2);
    assert!(
        rule.match_criteria
            .source_names
            .iter()
            .any(|name| name.as_ref() == "corp*")
    );

    rule.match_criteria.managers.clear();
    let error = serde_json::to_value(rule).unwrap_err().to_string();
    assert!(error.contains("SourceNames"), "unexpected error: {error}");

    let multiple_managers = r#"{
        "Id":"source.rule",
        "Priority":1,
        "Decision":"Allow",
        "Match":{
            "Managers":["Winget","PowerShell"],
            "SourceNames":["corp"]
        }
    }"#;
    let error = serde_json::from_str::<now_policy::PolicyRule>(multiple_managers)
        .unwrap_err()
        .to_string();
    assert!(error.contains("SourceNames"), "unexpected error: {error}");
}

#[test]
fn package_identifier_condition_requires_exactly_one_nonempty_mode() {
    let exact: now_policy::PackageIdentifierCondition =
        serde_json::from_str(r#"{"Exact":["Microsoft.VisualStudioCode"]}"#).unwrap();
    assert!(matches!(exact, now_policy::PackageIdentifierCondition::Exact(_)));

    let patterns: now_policy::PackageIdentifierCondition =
        serde_json::from_str(r#"{"Patterns":["Microsoft.*"]}"#).unwrap();
    assert!(matches!(patterns, now_policy::PackageIdentifierCondition::Patterns(_)));

    for invalid in [
        "{}",
        r#"{"Exact":[]}"#,
        r#"{"Patterns":[]}"#,
        r#"{"Exact":["Microsoft.VisualStudioCode"],"Patterns":["Microsoft.*"]}"#,
        r#"{"Exact":["Microsoft.VisualStudioCode"],"Patterns":null}"#,
        r#"{"Patterns":null,"Exact":["Microsoft.VisualStudioCode"]}"#,
        r#"{"Patterns":["Microsoft.*"],"Exact":null}"#,
        r#"{"Exact":null,"Patterns":["Microsoft.*"]}"#,
        r#"{"Exact":["Microsoft.*"]}"#,
        r#"{"Exact":["Git.Git","Git.Git"]}"#,
        r#"{"Patterns":["Git.*","Git.*"]}"#,
        r#"{"Exact":["Microsoft.VisualStudioCode"],"\u0045xact":["Git.Git"]}"#,
    ] {
        assert!(
            serde_json::from_str::<now_policy::PackageIdentifierCondition>(invalid).is_err(),
            "should reject {invalid}"
        );
    }

    assert!(
        serde_json::from_str::<now_policy::PolicyMatch>(r#"{"PackageIdentifiers":["Microsoft.VisualStudioCode"]}"#)
            .is_err()
    );
    let absent: now_policy::PolicyMatch = serde_json::from_str(r#"{"PackageIdentifiers":null}"#).unwrap();
    assert!(
        serde_json::to_value(absent)
            .unwrap()
            .get("PackageIdentifiers")
            .is_none()
    );
}

#[test]
fn version_condition_requires_exactly_one_nonempty_mode() {
    let exact: now_policy::VersionCondition =
        serde_json::from_str(r#"{"Exact":["5.6.0.0","2026.09-preview"]}"#).unwrap();
    assert!(matches!(exact, now_policy::VersionCondition::Exact(_)));

    let range: now_policy::VersionCondition =
        serde_json::from_str(r#"{"Range":{"MinVersion":"1.0.0","MaxVersion":"2.0.0"}}"#).unwrap();
    assert!(matches!(range, now_policy::VersionCondition::Range(_)));

    for invalid in [
        "{}",
        r#"{"Exact":[]}"#,
        r#"{"Exact":["1.0.0"],"Range":{"MinVersion":"1.0.0"}}"#,
        r#"{"Exact":["1.0.0"],"Range":null}"#,
        r#"{"Range":null,"Exact":["1.0.0"]}"#,
        r#"{"Range":{"MinVersion":"1.0.0"},"Exact":null}"#,
        r#"{"Exact":null,"Range":{"MinVersion":"1.0.0"}}"#,
        r#"{"Exact":["1.0.0","1.0.0"]}"#,
        r#"{"Exact":["1.0.0"],"\u0045xact":["2.0.0"]}"#,
    ] {
        assert!(
            serde_json::from_str::<now_policy::VersionCondition>(invalid).is_err(),
            "should reject {invalid}"
        );
    }

    for old in [
        r#"{"Versions":["1.0.0"]}"#,
        r#"{"VersionRange":{"MinVersion":"1.0.0"}}"#,
    ] {
        assert!(
            serde_json::from_str::<now_policy::PolicyMatch>(old).is_err(),
            "should reject {old}"
        );
    }
    let absent: now_policy::PolicyMatch = serde_json::from_str(r#"{"Version":null}"#).unwrap();
    assert!(serde_json::to_value(absent).unwrap().get("Version").is_none());
}

#[test]
fn package_identifier_and_version_condition_bounds_apply_on_input_and_output() {
    let identifiers = (0..=1024).map(|index| format!("Package.{index}")).collect::<Vec<_>>();
    let identifier_json = serde_json::json!({ "Exact": identifiers });
    assert!(serde_json::from_value::<now_policy::PackageIdentifierCondition>(identifier_json).is_err());

    let versions = (0..=256).map(|index| format!("1.0.{index}")).collect::<Vec<_>>();
    let version_json = serde_json::json!({ "Exact": versions });
    assert!(serde_json::from_value::<now_policy::VersionCondition>(version_json).is_err());

    let identifiers = (0..=1024)
        .map(|index| PackageIdentifier::parse(&format!("Package.{index}")).unwrap())
        .collect();
    assert!(serde_json::to_value(now_policy::PackageIdentifierCondition::Exact(identifiers)).is_err());

    let versions = (0..=256)
        .map(|index| VersionString::parse(&format!("1.0.{index}")).unwrap())
        .collect();
    assert!(serde_json::to_value(now_policy::VersionCondition::Exact(versions)).is_err());
}

#[test]
fn shared_boolean_characteristics_sample_has_expected_scalar_values() {
    let path = samples_dir().join("boolean-characteristics.policy.json");
    let policy: PolicyDocument = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    let match_criteria = &policy.rules[0].match_criteria;

    assert_eq!(match_criteria.interactive, Some(false));
    assert_eq!(match_criteria.skip_hash_check, Some(true));
    assert_eq!(match_criteria.pre_release, Some(false));
    assert_eq!(match_criteria.has_custom_parameters, Some(true));
    assert_eq!(match_criteria.has_custom_install_location, Some(false));
    assert_eq!(match_criteria.has_pre_post_commands, Some(true));
    assert_eq!(match_criteria.has_kill_before_operation, Some(false));
    assert_eq!(match_criteria.has_uninstall_previous, Some(true));
    assert_eq!(match_criteria.managers.len(), 1);
    assert_eq!(match_criteria.source_names.len(), 1);
    assert_eq!(match_criteria.execution_elevation.len(), 1);
    assert!(matches!(
        match_criteria.package_identifiers.as_ref(),
        Some(now_policy::PackageIdentifierCondition::Exact(_))
    ));
    assert!(matches!(
        match_criteria.version.as_ref(),
        Some(now_policy::VersionCondition::Exact(_))
    ));
}

#[test]
fn policy_text_newtypes_count_unicode_scalars_at_length_boundaries() {
    let multibyte_scalar = "😀";

    assert!(StringPattern::parse(&multibyte_scalar.repeat(256)).is_ok());
    assert!(StringPattern::parse(&multibyte_scalar.repeat(257)).is_err());

    assert!(SourceName::parse(&multibyte_scalar.repeat(128)).is_ok());
    assert!(SourceName::parse(&multibyte_scalar.repeat(129)).is_err());
    assert_eq!(SourceName::parse("corp*").unwrap().as_ref(), "corp*");

    assert!(PackageIdentifier::parse("Microsoft.VisualStudioCode").is_ok());
    assert!(PackageIdentifier::parse("Microsoft.*").is_err());
    assert!(PackageIdentifier::parse("Git.Git\n").is_err());

    assert!(VersionString::parse(&multibyte_scalar.repeat(128)).is_ok());
    assert!(VersionString::parse(&multibyte_scalar.repeat(129)).is_err());

    assert!(CustomParameterString::parse(&multibyte_scalar.repeat(512)).is_ok());
    assert!(CustomParameterString::parse(&multibyte_scalar.repeat(513)).is_err());
}

#[test]
fn invalid_policy_unknown_field_fails_deserialization() {
    let value = serde_json::json!({
        "PolicyFormatVersion": "1.0.0",
        "Metadata": {
            "Id": "test",
            "Publisher": "Test",
            "Revision": 1,
            "PublishedAt": "2026-01-01T00:00:00Z"
        },
        "Enforcement": {
            "DefaultDecision": "Deny",
            "UnknownField": true
        },
        "Rules": []
    });

    let result: Result<PolicyDocument, _> = serde_json::from_value(value);
    assert!(result.is_err(), "policy with unknown field should fail deserialization");
}

#[test]
fn removed_policy_members_fail_deserialization() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let valid: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();

    for removed_member in [
        "PolicyType",
        "RulePrecedence",
        "PackageNames",
        "Elevation",
        "Sources",
        "Versions",
        "VersionRange",
    ] {
        let mut value = valid.clone();
        match removed_member {
            "PolicyType" => value[removed_member] = serde_json::json!("PackageBrokerPolicy"),
            "RulePrecedence" => value["Enforcement"][removed_member] = serde_json::json!("PriorityThenDeny"),
            "PackageNames" => {
                value["Rules"][0]["Match"][removed_member] = serde_json::json!(["Friendly package name"]);
            }
            "Elevation" => value["Rules"][0]["Match"][removed_member] = serde_json::json!(["Elevated"]),
            "VersionRange" => {
                value["Rules"][0]["Match"][removed_member] = serde_json::json!({ "MinVersion": "1.0.0" });
            }
            _ => value["Rules"][0]["Match"][removed_member] = serde_json::json!(["winget"]),
        }

        assert!(
            serde_json::from_value::<PolicyDocument>(value).is_err(),
            "{removed_member} should be rejected as unknown"
        );
    }
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
fn policy_format_version_rejects_components_outside_u64_range() {
    let path = samples_dir().join("corporate-allowlist.policy.json");
    let mut value: serde_json::Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    value["PolicyFormatVersion"] = serde_json::json!("1.18446744073709551616.0");

    assert!(serde_json::from_value::<PolicyDocument>(value).is_err());
}

#[test]
fn invalid_policy_fixture_fails_deserialization() {
    let path = samples_dir().join("invalid/policies/invalid-failure-decision.policy.json");
    let content = std::fs::read_to_string(&path).unwrap();
    let result: Result<PolicyDocument, _> = serde_json::from_str(&content);
    assert!(result.is_err(), "invalid policy fixture should fail deserialization");

    let mut corrected: serde_json::Value = serde_json::from_str(&content).unwrap();
    corrected["Enforcement"]
        .as_object_mut()
        .expect("Enforcement should be an object")
        .remove("failureDecision");
    assert!(
        serde_json::from_value::<PolicyDocument>(corrected).is_ok(),
        "fixture should be valid after removing its intentionally invalid member"
    );
}

#[test]
fn duplicate_property_fixtures_fail_deserialization() {
    let duplicate_dir = samples_dir().join("invalid/duplicates");
    let entries = std::fs::read_dir(&duplicate_dir)
        .unwrap_or_else(|error| panic!("failed to read {}: {error}", duplicate_dir.display()));
    let mut fixture_count = 0;

    for entry in entries {
        let path = entry.unwrap().path();
        if path.extension().is_none_or(|extension| extension != "json") {
            continue;
        }

        fixture_count += 1;
        let content =
            std::fs::read_to_string(&path).unwrap_or_else(|error| panic!("failed to read {}: {error}", path.display()));
        let result: Result<PolicyDocument, _> = serde_json::from_str(&content);
        assert!(
            result.is_err(),
            "duplicate property fixture {} should fail deserialization",
            path.display()
        );
    }

    assert_eq!(fixture_count, 9, "all shared duplicate fixtures must be exercised");
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
fn policy_match_schema_uses_nullable_scalar_booleans() {
    let schema = now_policy::schema::policy_schema_json();
    let property_names = [
        "Interactive",
        "SkipHashCheck",
        "PreRelease",
        "HasCustomParameters",
        "HasCustomInstallLocation",
        "HasPrePostCommands",
        "HasKillBeforeOperation",
        "HasUninstallPrevious",
    ];

    for property_name in property_names {
        let property = [
            format!("/definitions/PolicyRule/properties/Match/properties/{property_name}"),
            format!("/$defs/PolicyRule/properties/Match/properties/{property_name}"),
        ]
        .into_iter()
        .find_map(|path| schema.pointer(&path))
        .unwrap_or_else(|| panic!("missing PolicyMatch.{property_name} schema"));
        let types = property["type"].as_array().expect("optional boolean type array");

        assert!(types.iter().any(|value| value == "boolean"));
        assert!(types.iter().any(|value| value == "null"));
        assert!(property.get("items").is_none());
        assert!(property.get("maxItems").is_none());
        assert!(property.get("uniqueItems").is_none());
    }
}

#[test]
fn standalone_policy_match_schema_requires_exactly_one_manager_for_source_names() {
    let schema = serde_json::to_value(schemars::schema_for!(now_policy::PolicyMatch)).unwrap();
    let root = schema.as_object().expect("PolicyMatch schema should be an object");

    assert!(root.contains_key("if"));
    assert!(root.contains_key("then"));
    assert_eq!(schema["then"]["properties"]["Managers"]["minItems"], 1);
    assert_eq!(schema["then"]["properties"]["Managers"]["maxItems"], 1);
}
