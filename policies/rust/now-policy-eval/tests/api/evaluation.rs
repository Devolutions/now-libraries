//! Evaluating requests: applicability, grants, precedence, audit mode and decisions.

use now_policy_eval::{Check, Request};
use rstest::rstest;

use crate::common::{
    ALLOW_ALL, METADATA, TRUST_ALL, check_of, document, evaluate, evaluate_at, evaluate_rules, request, summary,
    winget_install,
};

type Summary = (&'static str, &'static str, Vec<String>, Vec<&'static str>, bool);

fn expected(
    outcome: &'static str,
    check: &'static str,
    rules: &[&str],
    opt_ins: &[&'static str],
    audited: bool,
) -> Summary {
    (
        outcome,
        check,
        rules.iter().map(|rule| (*rule).to_owned()).collect(),
        opt_ins.to_vec(),
        audited,
    )
}

fn install(manager: &str, source: &str, id: &str, version: &str) -> Request {
    let mut request = request("Install", manager, source, id);
    request.package.version = Some(version.to_owned());
    request
}

fn with_parameters(parameters: &[&str]) -> Request {
    let mut request = winget_install();
    request.options.custom_parameters = parameters.iter().map(|parameter| (*parameter).to_owned()).collect();
    request
}

/// Builds a policy document with metadata extended by `metadata_extra` and an optional enforcement object.
fn document_with(metadata_extra: &str, enforcement: Option<&str>, trusted_sources: &str, rules: &str) -> String {
    let metadata = METADATA.replace(r#""Revision":1"#, &format!(r#""Revision":1{metadata_extra}"#));
    let enforcement = enforcement.map_or_else(String::new, |enforcement| format!(r#","Enforcement":{enforcement}"#));
    format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{metadata}{enforcement},"TrustedSources":{trusted_sources},"Rules":{rules}}}"#
    )
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §validity-window
#[case::before_valid_from("2026-01-31T23:59:59.999999999Z", "Deny", "ValidityWindow")]
#[case::at_valid_from("2026-02-01T00:00:00Z", "Allow", "AllowRules")]
#[case::at_valid_until_in_another_offset("2026-02-28T13:00:00+01:00", "Allow", "AllowRules")]
#[case::after_valid_until("2026-02-28T12:00:00.000000001Z", "Deny", "ValidityWindow")]
fn validity_window(#[case] at: &str, #[case] outcome: &str, #[case] check: &str) {
    let text = document_with(
        r#","ValidFrom":"2026-02-01T00:00:00Z","ValidUntil":"2026-02-28T12:00:00Z""#,
        None,
        TRUST_ALL,
        ALLOW_ALL,
    );
    let decision = evaluate_at(&text, &winget_install(), at);
    assert_eq!((decision.outcome.as_str(), decision.check.as_str()), (outcome, check));
}

#[test]
fn open_validity_window_is_always_in_force() {
    // oracle: contract policies/CONTRACT.policy.md §validity-window
    let text = document(TRUST_ALL, ALLOW_ALL);
    assert_eq!(
        evaluate_at(&text, &winget_install(), "0001-01-01T00:00:00Z").check,
        Check::AllowRules
    );
    assert_eq!(
        evaluate_at(&text, &winget_install(), "9999-12-31T23:59:59Z").check,
        Check::AllowRules
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §trusted-sources
#[case::named_source(r#"[{"Manager":"Winget","Source":"winget"}]"#, "AllowRules")]
#[case::named_source_in_other_ascii_case(r#"[{"Manager":"Winget","Source":"WinGet"}]"#, "AllowRules")]
#[case::any_source(r#"[{"Manager":"Winget","AnySource":true}]"#, "AllowRules")]
#[case::other_source(r#"[{"Manager":"Winget","Source":"msstore"}]"#, "UntrustedSource")]
#[case::other_manager(r#"[{"Manager":"Chocolatey","AnySource":true}]"#, "UntrustedSource")]
#[case::empty_list(r#"[]"#, "UntrustedSource")]
fn trusted_sources_govern_installs(#[case] trusted_sources: &str, #[case] check: &str) {
    let decision = evaluate(&document(trusted_sources, ALLOW_ALL), &winget_install());
    assert_eq!(decision.check.as_str(), check);
}

#[test]
fn untrusted_source_is_denied_whatever_the_rules_say() {
    // oracle: contract policies/CONTRACT.policy.md §trusted-sources
    let rules = r#"[{"Id":"winget","Decision":"Allow","Select":{"Sources":[{"Manager":"Winget","Source":"winget"}]}}]"#;
    let mut request = winget_install();
    request.operation = "Update".to_owned();
    assert_eq!(
        summary(&evaluate(&document("[]", rules), &request)),
        expected("Deny", "UntrustedSource", &[], &[], false)
    );
}

#[test]
fn trusted_sources_do_not_govern_uninstalls() {
    // oracle: contract policies/CONTRACT.policy.md §trusted-sources
    let rules = r#"[{"Id":"msstore","Decision":"Allow","Select":{"Operations":["Uninstall"],"Sources":[{"Manager":"Winget","Source":"msstore"}]}}]"#;
    let request = request("Uninstall", "Winget", "msstore", "9NBLGGH4NNS1");
    assert_eq!(
        summary(&evaluate(&document("[]", rules), &request)),
        expected("Allow", "AllowRules", &["msstore"], &[], false)
    );
}

#[test]
fn source_compares_ignoring_case_with_unicode_simple_uppercase() {
    // oracle: contract policies/CONTRACT.policy.md §text-comparison
    // oracle: spec Unicode 16.0.0 UnicodeData.txt
    // U+00E9 maps to U+00C9 and U+03C3 maps to U+03A3 in the simple uppercase mapping.
    let mut request = winget_install();
    request.source.name = "dépôt-σ".to_owned();
    let trusted = r#"[{"Manager":"Winget","Source":"DÉPÔT-Σ"}]"#;
    assert_eq!(
        evaluate(&document(trusted, ALLOW_ALL), &request).check,
        Check::AllowRules
    );
}

#[test]
fn unknown_source_is_not_trusted_by_a_named_source() {
    // oracle: contract policies/CONTRACT.policy.md §source-selector
    // oracle: contract policies/CONTRACT.policy.md §manager-npm
    // An alias whose target is not a registry name has an unknown source under Npm.
    let request = install("Npm", "npm", "github:user/repo", "1.0.0");
    let named = r#"[{"Manager":"Npm","Source":"npm"}]"#;
    assert_eq!(
        evaluate(&document(named, ALLOW_ALL), &request).check,
        Check::UntrustedSource
    );
}

#[test]
fn unknown_source_is_trusted_by_any_source() {
    // oracle: contract policies/CONTRACT.policy.md §source-selector
    // oracle: contract policies/CONTRACT.policy.md §manager-npm
    let request = install("Npm", "npm", "github:user/repo", "1.0.0");
    let any = r#"[{"Manager":"Npm","AnySource":true}]"#;
    assert_eq!(evaluate(&document(any, ALLOW_ALL), &request).check, Check::AllowRules);
}

#[test]
fn disabled_rule_is_never_applicable() {
    // oracle: contract policies/CONTRACT.policy.md §rules
    let rules = r#"[
        {"Id":"allow","Decision":"Allow","Select":{}},
        {"Id":"deny","Enabled":false,"Decision":"Deny","Select":{}}
    ]"#;
    assert_eq!(check_of(rules, &winget_install()), Check::AllowRules);
}

#[test]
fn no_rules_deny_by_default() {
    // oracle: contract policies/CONTRACT.policy.md §precedence
    let mut request = winget_install();
    request.options.interactive = true;
    assert_eq!(
        summary(&evaluate_rules("[]", &request)),
        expected("Deny", "DefaultDeny", &[], &["Interactive"], false)
    );
}

#[test]
fn allow_rule_without_grants_permits_only_plain_requests() {
    // oracle: contract policies/CONTRACT.policy.md §rules
    let mut request = winget_install();
    request.options.no_upgrade = true;
    assert_eq!(
        summary(&evaluate_rules(ALLOW_ALL, &request)),
        expected("Deny", "UngrantedOptIn", &["all"], &["NoUpgrade"], false)
    );
}

#[test]
fn deny_rule_without_forbids_denies_every_request_it_applies_to() {
    // oracle: contract policies/CONTRACT.policy.md §rules
    let rules = r#"[
        {"Id":"all","Decision":"Allow","Select":{}},
        {"Id":"ban","Decision":"Deny","Reason":"Banned","Select":{"Packages":["Contoso.*"]}}
    ]"#;
    assert_eq!(
        summary(&evaluate_rules(rules, &winget_install())),
        expected("Deny", "DenyRule", &["ban: Banned"], &[], false)
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §opt-in-evaluation
#[case::opt_in_not_used(false, "Allow", "AllowRules", &["all"], &[])]
#[case::opt_in_used(true, "Deny", "DenyRule", &["no-interactive"], &["Interactive"])]
fn deny_rule_with_forbids_denies_only_requests_using_them(
    #[case] interactive: bool,
    #[case] outcome: &'static str,
    #[case] check: &'static str,
    #[case] rules: &[&str],
    #[case] opt_ins: &[&'static str],
) {
    let policy_rules = r#"[
        {"Id":"all","Decision":"Allow","Select":{},"Grants":{"Interactive":{}}},
        {"Id":"no-interactive","Decision":"Deny","Select":{},"Forbids":{"Interactive":{}}}
    ]"#;
    let mut request = winget_install();
    request.options.interactive = interactive;
    assert_eq!(
        summary(&evaluate_rules(policy_rules, &request)),
        expected(outcome, check, rules, opt_ins, false)
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §selection
#[case::operation_in_list(r#"{"Operations":["Update","Install"]}"#, "AllowRules")]
#[case::operation_not_in_list(r#"{"Operations":["Uninstall"]}"#, "DefaultDeny")]
#[case::source_in_list(
    r#"{"Sources":[{"Manager":"Chocolatey","AnySource":true},{"Manager":"Winget","Source":"winget"}]}"#,
    "AllowRules"
)]
#[case::source_of_other_manager(r#"{"Sources":[{"Manager":"Chocolatey","Source":"winget"}]}"#, "DefaultDeny")]
#[case::package_pattern(r#"{"Packages":["Other","Contoso.*"]}"#, "AllowRules")]
#[case::package_pattern_not_matching(r#"{"Packages":["Contoso"]}"#, "DefaultDeny")]
#[case::package_pattern_with_inner_star(r#"{"Packages":["C*o*.T*l"]}"#, "AllowRules")]
#[case::elevation_matching(r#"{"Elevation":"NonElevated"}"#, "AllowRules")]
#[case::elevation_not_matching(r#"{"Elevation":"Elevated"}"#, "DefaultDeny")]
#[case::every_condition_must_hold(r#"{"Operations":["Install"],"Elevation":"Elevated"}"#, "DefaultDeny")]
fn selection_conditions(#[case] selection: &str, #[case] check: &str) {
    let rules = format!(r#"[{{"Id":"a","Decision":"Allow","Select":{selection}}}]"#);
    assert_eq!(check_of(&rules, &winget_install()).as_str(), check);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §version-condition
// oracle: contract policies/CONTRACT.policy.md §version-scheme
#[case::within_bounds(r#""Minimum":"1.0","Maximum":"2.0""#, "1.2.3", "AllowRules")]
#[case::missing_components_count_as_zero(r#""Minimum":"1.2.3.0","Maximum":"1.2.3""#, "1.2.3", "AllowRules")]
#[case::components_compare_numerically(r#""Minimum":"1.9""#, "1.10.0", "AllowRules")]
#[case::leading_zeros_ignored(r#""Maximum":"1.2""#, "01.002", "AllowRules")]
#[case::long_components(r#""Minimum":"1.99999999999999999999""#, "1.100000000000000000000", "AllowRules")]
#[case::v_prefix(r#""Minimum":"V1.2.3","Maximum":"v1.2.3""#, "1.2.3", "AllowRules")]
#[case::below_minimum(r#""Minimum":"1.2.4""#, "1.2.3", "DefaultDeny")]
#[case::above_maximum(r#""Maximum":"1.2.2""#, "1.2.3", "DefaultDeny")]
#[case::pre_release_below_its_release(r#""Minimum":"1.2.3""#, "1.2.3-rc.1", "DefaultDeny")]
#[case::pre_release_within_bounds(r#""Maximum":"1.2.3""#, "1.2.3-rc.1", "AllowRules")]
#[case::numeric_pre_release_identifiers_compare_numerically(r#""Minimum":"1.0.0-rc.9""#, "1.0.0-rc.10", "AllowRules")]
#[case::numeric_identifier_below_alphanumeric(r#""Minimum":"1.0.0-alpha""#, "1.0.0-1", "DefaultDeny")]
#[case::shorter_pre_release_is_lower(r#""Minimum":"1.0.0-alpha.1""#, "1.0.0-alpha", "DefaultDeny")]
#[case::pre_release_identifiers_compare_in_ascii_order(r#""Minimum":"1.0.0-beta""#, "1.0.0-Beta", "DefaultDeny")]
#[case::build_metadata_ignored(r#""Minimum":"1.2.3+zzz","Maximum":"1.2.3+aaa""#, "1.2.3+mmm", "AllowRules")]
#[case::not_a_version_is_unknown(r#""Minimum":"0.0.1""#, "1.2.3.beta", "DefaultDeny")]
fn version_conditions(#[case] condition: &str, #[case] version: &str, #[case] check: &str) {
    let rules =
        format!(r#"[{{"Id":"a","Decision":"Allow","Select":{{"Operations":["Install"],"Version":{{{condition}}}}}}}]"#);
    let request = install("Chocolatey", "chocolatey", "git", version);
    assert_eq!(check_of(&rules, &request).as_str(), check);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §version-scheme
#[case::chocolatey_pre_release_excluded("Chocolatey", "chocolatey", "2.0.0-beta", "DefaultDeny")]
#[case::chocolatey_release_kept("Chocolatey", "chocolatey", "2.0.0+beta", "AllowRules")]
#[case::winget_has_no_pre_release("Winget", "winget", "2.0.0-beta", "AllowRules")]
fn prerelease_exclude(#[case] manager: &str, #[case] source: &str, #[case] version: &str, #[case] check: &str) {
    let rules =
        r#"[{"Id":"a","Decision":"Allow","Select":{"Operations":["Install"],"Version":{"Prerelease":"Exclude"}}}]"#;
    assert_eq!(
        check_of(rules, &install(manager, source, "git", version)).as_str(),
        check
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §applicability
#[case::allow_rule_does_not_apply(r#"[{"Id":"a","Decision":"Allow","Select":{"Architectures":["X64"]}}]"#, "DefaultDeny", &[])]
#[case::deny_rule_applies(r#"[{"Id":"all","Decision":"Allow","Select":{}},{"Id":"d","Decision":"Deny","Select":{"Architectures":["Arm64"]}}]"#, "DenyRule", &["d"])]
#[case::allow_rule_counts_as_excepted(r#"[{"Id":"a","Decision":"Allow","Select":{},"Except":[{"Architectures":["Arm64"]}]}]"#, "DefaultDeny", &[])]
#[case::deny_rule_counts_as_not_excepted(r#"[{"Id":"all","Decision":"Allow","Select":{}},{"Id":"d","Decision":"Deny","Select":{},"Except":[{"Architectures":["X64"]}]}]"#, "DenyRule", &["d"])]
#[case::known_condition_decides_selection(r#"[{"Id":"a","Decision":"Allow","Select":{}},{"Id":"d","Decision":"Deny","Select":{"Architectures":["Arm64"],"Operations":["Uninstall"]}}]"#, "AllowRules", &["a"])]
#[case::known_except_entry_decides_list(r#"[{"Id":"all","Decision":"Allow","Select":{}},{"Id":"d","Decision":"Deny","Select":{},"Except":[{"Architectures":["X64"]},{"Packages":["Contoso.*"]}]}]"#, "AllowRules", &["all"])]
fn unknown_properties_resolve_toward_denial(#[case] rules: &str, #[case] check: &str, #[case] rule_ids: &[&str]) {
    // A WinGet request without an architecture has an unknown architecture.
    let decision = evaluate_rules(rules, &winget_install());
    let ids: Vec<&str> = decision.rules.iter().map(|rule| rule.id.as_str()).collect();
    assert_eq!((decision.check.as_str(), ids.as_slice()), (check, rule_ids));
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §package-identity
#[case::winget_lowercases_ascii(
    install("Winget", "winget", "Contoso.Tool", "1.0"),
    "CONTOSO.*",
    "AllowRules",
    "DenyRule"
)]
#[case::npm_keeps_case(install("Npm", "npm", "React", "18.2.0"), "React", "AllowRules", "DenyRule")]
#[case::npm_case_difference_is_unknown(install("Npm", "npm", "react", "18.2.0"), "React", "DefaultDeny", "DenyRule")]
#[case::npm_alias_partly_covered(
    install("Npm", "npm", "my-react:react", "18.2.0"),
    "react",
    "DefaultDeny",
    "DenyRule"
)]
#[case::npm_alias_not_covered(
    install("Npm", "npm", "my-react:react", "18.2.0"),
    "vue",
    "DefaultDeny",
    "AllowRules"
)]
#[case::npm_scoped_alias_target(install("Npm", "npm", "ui:@contoso/ui", "1.0.0"), "*ui", "AllowRules", "DenyRule")]
#[case::npm_unknown_identity(install("Npm", "npm", "github:user/repo", "1.0.0"), "*", "DefaultDeny", "DenyRule")]
#[case::bun_alias_is_unknown(install("Bun", "Bun", "my-react:react", "18.2.0"), "*", "DefaultDeny", "DenyRule")]
#[case::pip_runs_of_separators(
    install("Pip", "pip", "Foo__Bar.baz", "1.0"),
    "foo-bar_BAZ",
    "AllowRules",
    "DenyRule"
)]
#[case::pip_pattern_segments_canonical(
    install("Pip", "pip", "zope.interface", "1.0"),
    "Zope_*",
    "AllowRules",
    "DenyRule"
)]
#[case::cargo_underscore_is_dash(
    install("Cargo", "crates.io", "Serde_JSON", "1.0.0"),
    "serde-json",
    "AllowRules",
    "DenyRule"
)]
#[case::vcpkg_port(
    request("Install", "Vcpkg", "x64-windows", "zlib[core,bzip2]:x64-windows"),
    "zlib",
    "AllowRules",
    "DenyRule"
)]
#[case::non_ascii_case_difference_is_unknown(
    install("Winget", "winget", "Contoso.Tool", "1.0"),
    "contoſo.*",
    "DefaultDeny",
    "DenyRule"
)]
fn package_patterns(
    #[case] subject: Request,
    #[case] pattern: &str,
    #[case] allow_check: &str,
    #[case] deny_check: &str,
) {
    let allow = format!(r#"[{{"Id":"a","Decision":"Allow","Select":{{"Packages":["{pattern}"]}}}}]"#);
    let deny = format!(
        r#"[{{"Id":"all","Decision":"Allow","Select":{{}}}},{{"Id":"d","Decision":"Deny","Select":{{"Packages":["{pattern}"]}}}}]"#
    );
    assert_eq!(
        (check_of(&allow, &subject).as_str(), check_of(&deny, &subject).as_str()),
        (allow_check, deny_check)
    );
}

#[test]
fn npm_alias_covered_by_two_patterns_is_allowed() {
    // oracle: contract policies/CONTRACT.policy.md §package-identity
    let rules = r#"[{"Id":"a","Decision":"Allow","Select":{"Packages":["my-react","react"]}}]"#;
    assert_eq!(
        check_of(rules, &install("Npm", "npm", "my-react:react", "18.2.0")),
        Check::AllowRules
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §grants
// oracle: contract policies/CONTRACT.policy.md §text-comparison
#[case::exact(&["/quiet"], r#"{"Patterns":["/quiet"]}"#, "AllowRules")]
#[case::ignoring_case(&["/QUIET"], r#"{"Patterns":["/quiet"]}"#, "AllowRules")]
#[case::ignoring_case_with_simple_uppercase(&["/ſ"], r#"{"Patterns":["/s"]}"#, "AllowRules")]
#[case::star_matches_the_rest(&["/log=C:\\x.log"], r#"{"Patterns":["/log=*"]}"#, "AllowRules")]
#[case::whole_value(&["/quiet2"], r#"{"Patterns":["/quiet"]}"#, "UngrantedOptIn")]
#[case::each_parameter_covered(&["/quiet", "/norestart"], r#"{"Patterns":["/quiet","/norestart"]}"#, "AllowRules")]
#[case::one_parameter_uncovered(&["/quiet", "/norestart"], r#"{"Patterns":["/quiet"]}"#, "UngrantedOptIn")]
#[case::except_narrows(&["/log=C:\\x.log"], r#"{"Patterns":["/log=*"],"Except":["/log=C:\\*"]}"#, "UngrantedOptIn")]
#[case::no_escape_character(&["/a\\*"], r#"{"Patterns":["/a\\*"]}"#, "AllowRules")]
fn custom_parameter_grants(#[case] parameters: &[&str], #[case] grant: &str, #[case] check: &str) {
    let rules = format!(r#"[{{"Id":"a","Decision":"Allow","Select":{{}},"Grants":{{"CustomParameters":{grant}}}}}]"#);
    assert_eq!(check_of(&rules, &with_parameters(parameters)).as_str(), check);
}

#[test]
fn custom_parameter_grants_add_up_across_rules() {
    // oracle: contract policies/CONTRACT.policy.md §opt-in-evaluation
    let rules = r#"[
        {"Id":"quiet","Decision":"Allow","Select":{},"Grants":{"CustomParameters":{"Patterns":["/quiet"]}}},
        {"Id":"norestart","Decision":"Allow","Select":{"Packages":["Contoso.*"]},"Grants":{"CustomParameters":{"Patterns":["/norestart"]}}}
    ]"#;
    assert_eq!(
        summary(&evaluate_rules(rules, &with_parameters(&["/quiet", "/norestart"]))),
        expected("Allow", "AllowRules", &["quiet", "norestart"], &[], false)
    );
}

#[test]
fn grant_except_narrows_only_its_own_grant() {
    // oracle: contract policies/CONTRACT.policy.md §grants
    let rules = r#"[
        {"Id":"narrow","Decision":"Allow","Select":{},"Grants":{"CustomParameters":{"Patterns":["/log=*"],"Except":["/log=C:\\*"]}}},
        {"Id":"broad","Decision":"Allow","Select":{},"Grants":{"CustomParameters":{"Patterns":["/log=*"]}}}
    ]"#;
    assert_eq!(
        check_of(rules, &with_parameters(&["/log=C:\\x.log"])),
        Check::AllowRules
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
// oracle: contract policies/CONTRACT.policy.md §manager-winget
#[case::custom_parameters_only(r#""CustomParameters":{"Patterns":["*"]}"#, "UngrantedOptIn", &["InstallerArguments"])]
#[case::installer_arguments_only(r#""InstallerArguments":{}"#, "UngrantedOptIn", &["CustomParameters"])]
#[case::both(r#""InstallerArguments":{},"CustomParameters":{"Patterns":["--override","/quiet"]}"#, "AllowRules", &[])]
fn custom_parameter_with_another_opt_in_needs_both_grants(
    #[case] grants: &str,
    #[case] check: &str,
    #[case] opt_ins: &[&'static str],
) {
    let rules = format!(r#"[{{"Id":"a","Decision":"Allow","Select":{{}},"Grants":{{{grants}}}}}]"#);
    let decision = evaluate_rules(&rules, &with_parameters(&["--override", "/quiet"]));
    let names: Vec<&str> = decision.opt_ins.iter().map(|opt_in| opt_in.as_str()).collect();
    assert_eq!((decision.check.as_str(), names.as_slice()), (check, opt_ins));
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §grants
#[case::normalized_pattern("C:/Apps/*", "c:\\apps//Tool\\", "AllowRules")]
#[case::pattern_with_trailing_separator("C:\\Apps\\", "C:/Apps", "AllowRules")]
#[case::drive_root_pattern("c:/", "C:\\", "AllowRules")]
#[case::other_drive("C:\\Apps\\*", "D:\\Apps\\Tool", "UngrantedOptIn")]
#[case::prefix_without_star("C:\\Apps", "C:\\Apps\\Tool", "UngrantedOptIn")]
fn install_location_grants(#[case] pattern: &str, #[case] location: &str, #[case] check: &str) {
    let pattern = pattern.replace('\\', "\\\\");
    let rules = format!(
        r#"[{{"Id":"a","Decision":"Allow","Select":{{}},"Grants":{{"CustomInstallLocation":{{"Patterns":["{pattern}"]}}}}}}]"#
    );
    let mut request = winget_install();
    request.options.custom_install_location = Some(location.to_owned());
    assert_eq!(check_of(&rules, &request).as_str(), check);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §opt-in-evaluation
#[case::covered_parameter(&["/quiet", "/log=x"], "Deny", "DenyRule", &["d"], &["CustomParameters"])]
#[case::uncovered_parameters(&["/quiet"], "Allow", "AllowRules", &["a"], &[])]
fn custom_parameter_forbids(
    #[case] parameters: &[&str],
    #[case] outcome: &'static str,
    #[case] check: &'static str,
    #[case] rules: &[&str],
    #[case] opt_ins: &[&'static str],
) {
    let policy_rules = r#"[
        {"Id":"a","Decision":"Allow","Select":{},"Grants":{"CustomParameters":{"Patterns":["*"]}}},
        {"Id":"d","Decision":"Deny","Select":{},"Forbids":{"CustomParameters":{"Patterns":["/LOG*"]}}}
    ]"#;
    assert_eq!(
        summary(&evaluate_rules(policy_rules, &with_parameters(parameters))),
        expected(outcome, check, rules, opt_ins, false)
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §opt-in-evaluation
#[case::forbidden_location("C:\\Windows\\System32", "DenyRule")]
#[case::excepted_location("C:\\Windows\\Temp\\x", "AllowRules")]
#[case::other_location("C:\\Apps", "AllowRules")]
fn install_location_forbids(#[case] location: &str, #[case] check: &str) {
    let rules = r#"[
        {"Id":"a","Decision":"Allow","Select":{},"Grants":{"CustomInstallLocation":{"Patterns":["*"]}}},
        {"Id":"d","Decision":"Deny","Select":{},"Forbids":{"CustomInstallLocation":{"Patterns":["c:/windows/*"],"Except":["C:/Windows/Temp/*"]}}}
    ]"#;
    let mut request = winget_install();
    request.options.custom_install_location = Some(location.to_owned());
    assert_eq!(check_of(rules, &request).as_str(), check);
}

#[test]
fn allow_rule_cannot_require_an_opt_in() {
    // oracle: contract policies/CONTRACT.policy.md §applicability
    // A plain request is allowed by a rule that grants an opt-in it does not use.
    let rules = r#"[{"Id":"a","Decision":"Allow","Select":{},"Grants":{"Interactive":{}}}]"#;
    assert_eq!(check_of(rules, &winget_install()), Check::AllowRules);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §precedence
#[case::unreadable_over_validity_window(
    |r: &mut Request| { r.operation = "Repair".to_owned(); },
    "UnreadableRequest"
)]
#[case::validity_window_over_install_location(
    |r: &mut Request| { r.options.custom_install_location = Some("Apps".to_owned()); },
    "ValidityWindow"
)]
fn precedence_outside_the_validity_window(#[case] change: fn(&mut Request), #[case] check: &str) {
    let text = document_with(r#","ValidUntil":"2026-01-01T00:00:00Z""#, None, "[]", "[]");
    let mut request = winget_install();
    change(&mut request);
    assert_eq!(evaluate(&text, &request).check.as_str(), check);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §precedence
#[case::install_location_over_missing_version(
    |r: &mut Request| { r.package.version = None; r.options.custom_install_location = Some("Apps".to_owned()); },
    "InstallLocation"
)]
#[case::missing_version_over_untrusted_source(
    |r: &mut Request| { r.package.version = None; r.source.name = "msstore".to_owned(); },
    "MissingVersion"
)]
#[case::untrusted_source_over_deny_rule(
    |r: &mut Request| { r.source.name = "msstore".to_owned(); },
    "UntrustedSource"
)]
#[case::deny_rule_over_ungranted_opt_in(
    |r: &mut Request| { r.options.interactive = true; },
    "DenyRule"
)]
fn precedence_of_checks(#[case] change: fn(&mut Request), #[case] check: &str) {
    let rules = r#"[
        {"Id":"a","Decision":"Allow","Select":{}},
        {"Id":"d","Decision":"Deny","Select":{"Packages":["Contoso.Tool"]}}
    ]"#;
    let text = document(r#"[{"Manager":"Winget","Source":"winget"}]"#, rules);
    let mut request = winget_install();
    change(&mut request);
    assert_eq!(evaluate(&text, &request).check.as_str(), check);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §audit-mode
#[case::untrusted_source(
    |r: &mut Request| { r.source.name = "msstore".to_owned(); },
    expected("Allow", "UntrustedSource", &[], &[], true)
)]
#[case::deny_rule(
    |r: &mut Request| { r.package.id = "Contoso.Banned".to_owned(); },
    expected("Allow", "DenyRule", &["ban: Banned"], &[], true)
)]
#[case::ungranted_opt_in(
    |r: &mut Request| { r.options.skip_hash_check = true; },
    expected("Allow", "UngrantedOptIn", &["tools"], &["SkipHashCheck"], true)
)]
#[case::default_deny(
    |r: &mut Request| { r.package.id = "Fabrikam.App".to_owned(); r.options.interactive = true; },
    expected("Allow", "DefaultDeny", &[], &["Interactive"], true)
)]
#[case::allowed_request_is_not_audited(
    |_: &mut Request| {},
    expected("Allow", "AllowRules", &["tools"], &[], false)
)]
#[case::unreadable_request(
    |r: &mut Request| { r.package.id = String::new(); },
    expected("Deny", "UnreadableRequest", &[], &[], false)
)]
#[case::install_location(
    |r: &mut Request| { r.options.custom_install_location = Some("\\\\server\\share".to_owned()); },
    expected("Deny", "InstallLocation", &[], &[], false)
)]
#[case::missing_version(
    |r: &mut Request| { r.package.version = None; },
    expected("Deny", "MissingVersion", &[], &[], false)
)]
fn audit_mode(#[case] change: fn(&mut Request), #[case] decision: Summary) {
    let rules = r#"[
        {"Id":"tools","Decision":"Allow","Select":{"Packages":["Contoso.*"]}},
        {"Id":"ban","Decision":"Deny","Reason":"Banned","Select":{"Packages":["Contoso.Banned"]}}
    ]"#;
    let text = document_with(
        "",
        Some(r#"{"AuditMode":true}"#),
        r#"[{"Manager":"Winget","Source":"winget"}]"#,
        rules,
    );
    let mut request = winget_install();
    change(&mut request);
    assert_eq!(summary(&evaluate(&text, &request)), decision);
}

#[test]
fn audit_mode_does_not_apply_outside_the_validity_window() {
    // oracle: contract policies/CONTRACT.policy.md §audit-mode
    let text = document_with(
        r#","ValidFrom":"2027-01-01T00:00:00Z""#,
        Some(r#"{"AuditMode":true}"#),
        TRUST_ALL,
        "[]",
    );
    assert_eq!(
        summary(&evaluate(&text, &winget_install())),
        expected("Deny", "ValidityWindow", &[], &[], false)
    );
}

#[test]
fn audit_mode_false_denies() {
    // oracle: contract policies/CONTRACT.policy.md §audit-mode
    let text = document_with("", Some(r#"{"AuditMode":false}"#), TRUST_ALL, "[]");
    assert_eq!(
        summary(&evaluate(&text, &winget_install())),
        expected("Deny", "DefaultDeny", &[], &[], false)
    );
}

#[test]
fn deny_rule_decision_names_every_denying_rule_in_policy_order() {
    // oracle: contract policies/CONTRACT.policy.md §decision
    let rules = r#"[
        {"Id":"z-location","Decision":"Deny","Reason":"No locations","Select":{},"Forbids":{"CustomInstallLocation":{"Patterns":["*"]}}},
        {"Id":"allow","Decision":"Allow","Select":{},"Grants":{"Interactive":{},"NoUpgrade":{},"CustomInstallLocation":{"Patterns":["*"]}}},
        {"Id":"a-interactive","Decision":"Deny","Select":{},"Forbids":{"NoUpgrade":{},"Interactive":{}}},
        {"Id":"not-applicable","Decision":"Deny","Select":{"Operations":["Uninstall"]}},
        {"Id":"not-denying","Decision":"Deny","Select":{},"Forbids":{"KillBeforeOperation":{}}}
    ]"#;
    let mut request = winget_install();
    request.options.no_upgrade = true;
    request.options.interactive = true;
    request.options.custom_install_location = Some("C:\\Apps".to_owned());
    assert_eq!(
        summary(&evaluate_rules(rules, &request)),
        expected(
            "Deny",
            "DenyRule",
            &["z-location: No locations", "a-interactive"],
            &["Interactive", "CustomInstallLocation", "NoUpgrade"],
            false
        )
    );
}

#[test]
fn ungranted_opt_in_decision_names_every_applicable_allow_rule() {
    // oracle: contract policies/CONTRACT.policy.md §decision
    let rules = r#"[
        {"Id":"b","Decision":"Allow","Reason":"Kill","Select":{},"Grants":{"KillBeforeOperation":{}}},
        {"Id":"other","Decision":"Allow","Select":{"Operations":["Uninstall"]},"Grants":{"NoUpgrade":{}}},
        {"Id":"a","Decision":"Allow","Select":{"Packages":["Contoso.*"]}}
    ]"#;
    let mut request = winget_install();
    request.options.kill_before_operation = vec!["tool.exe".to_owned()];
    request.options.no_upgrade = true;
    request.options.skip_hash_check = true;
    assert_eq!(
        summary(&evaluate_rules(rules, &request)),
        expected(
            "Deny",
            "UngrantedOptIn",
            &["b: Kill", "a"],
            &["SkipHashCheck", "NoUpgrade"],
            false
        )
    );
}

#[test]
fn allow_decision_names_every_applicable_allow_rule() {
    // oracle: contract policies/CONTRACT.policy.md §decision
    let rules = r#"[
        {"Id":"broad","Decision":"Allow","Select":{}},
        {"Id":"narrow","Decision":"Allow","Reason":"Contoso","Select":{"Packages":["Contoso.*"]},"Grants":{"Interactive":{}}},
        {"Id":"other","Decision":"Allow","Select":{"Packages":["Fabrikam.*"]}}
    ]"#;
    let mut request = winget_install();
    request.options.interactive = true;
    assert_eq!(
        summary(&evaluate_rules(rules, &request)),
        expected("Allow", "AllowRules", &["broad", "narrow: Contoso"], &[], false)
    );
}
