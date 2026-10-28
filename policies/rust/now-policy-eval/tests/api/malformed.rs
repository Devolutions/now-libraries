//! Requests the types allow but the wire contract rejects as `BadRequest` are denied as unreadable.

use now_policy_eval::{
    Check, CustomParameters, Elevation, InstallerArguments, InstallerArgumentsMode, KillBeforeOperation,
    PrePostCommands, Request, Scope,
};
use rstest::rstest;

use crate::common::{METADATA, TRUST_ALL, check_of, evaluate, request, summary, winget_install, with_location};

/// An Allow rule for every request that grants every opt-in with any value.
const GRANT_ALL: &str = r#"[{"Id":"all","Decision":"Allow","Select":{},"Grants":{
    "Interactive":{},"SkipHashCheck":{},"InstallerArguments":{},"PrePostCommands":{},"KillBeforeOperation":{},"UninstallPrevious":{},"NoUpgrade":{},
    "CustomParameters":{"Patterns":["*"]},"CustomInstallLocation":{"Patterns":["*"]}}}]"#;

/// Returns a WinGet install that `set` changes to a value of `length`.
fn with_length(set: fn(&mut Request, usize), length: usize) -> Request {
    let mut request = winget_install();
    set(&mut request, length);
    request
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §package-request
#[case::source_name(|r: &mut Request, n| r.source.name = "s".repeat(n), 1, 128)]
#[case::package_id(|r: &mut Request, n| r.package.id = "p".repeat(n), 1, 256)]
#[case::version(|r: &mut Request, n| r.package.version = Some("1".repeat(n)), 1, 128)]
fn package_request_strings_have_their_lengths(
    #[case] set: fn(&mut Request, usize),
    #[case] min: usize,
    #[case] max: usize,
) {
    let check = |length| check_of(GRANT_ALL, &with_length(set, length));
    assert_eq!([check(min), check(max)], [Check::AllowRules; 2]);
    assert_eq!([check(min - 1), check(max + 1)], [Check::UnreadableRequest; 2]);
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §opt-ins
#[case::installer_arguments(|r: &mut Request, n| r.opt_ins.installer_arguments = Some(InstallerArguments { arguments: "a".repeat(n), mode: InstallerArgumentsMode::Append }), 1, 2048)]
#[case::pre_command(|r: &mut Request, n| r.opt_ins.pre_post_commands = Some(PrePostCommands { pre: Some("c".repeat(n)), post: None }), 1, 2048)]
#[case::post_command(|r: &mut Request, n| r.opt_ins.pre_post_commands = Some(PrePostCommands { pre: None, post: Some("c".repeat(n)) }), 1, 2048)]
#[case::custom_parameter_count(|r: &mut Request, n| r.opt_ins.custom_parameters = Some(CustomParameters { values: vec!["p".to_owned(); n] }), 1, 64)]
#[case::custom_parameter_length(|r: &mut Request, n| r.opt_ins.custom_parameters = Some(CustomParameters { values: vec!["p".repeat(n)] }), 1, 512)]
#[case::process_count(|r: &mut Request, n| r.opt_ins.kill_before_operation = Some(KillBeforeOperation { processes: vec!["a.exe".to_owned(); n] }), 1, 64)]
#[case::process_length(|r: &mut Request, n| r.opt_ins.kill_before_operation = Some(KillBeforeOperation { processes: vec!["p".repeat(n)] }), 1, 128)]
fn opt_in_values_have_their_lengths(#[case] set: fn(&mut Request, usize), #[case] min: usize, #[case] max: usize) {
    let check = |length| check_of(GRANT_ALL, &with_length(set, length));
    assert_eq!([check(min), check(max)], [Check::AllowRules; 2]);
    assert_eq!([check(min - 1), check(max + 1)], [Check::UnreadableRequest; 2]);
}

#[test]
fn install_location_has_its_length() {
    // oracle: contract policies/CONTRACT.wire.md §opt-ins
    let check = |path: &str| check_of(GRANT_ALL, &with_location(winget_install(), path));
    assert_eq!(check(&format!("C:\\{}", "a".repeat(2045))), Check::AllowRules);
    assert_eq!(
        [check(""), check(&format!("C:\\{}", "a".repeat(2046)))],
        [Check::UnreadableRequest; 2]
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §package-request
#[case::space("Contoso Tool")]
#[case::tilde("Contoso~Tool")]
#[case::backslash("Contoso\\Tool")]
#[case::star("Contoso*")]
#[case::double_quote("Contoso\"Tool")]
#[case::ampersand("Contoso&Tool")]
#[case::equals("Contoso=Tool")]
#[case::parenthesis("Contoso(Tool)")]
#[case::non_ascii("Contosö")]
#[case::line_feed("Contoso\nTool")]
fn package_id_characters_are_limited(#[case] id: &str) {
    // A Dotnet install adds no id rule beyond a leading `-`, so the characters alone decide.
    let dotnet = |id: &str| {
        let mut request = request("Install", "Dotnet", "nuget.org", id);
        request.package.version = Some("8.0.0".to_owned());
        request
    };
    assert_eq!(
        [
            check_of(GRANT_ALL, &dotnet("Contoso.Tool")),
            check_of(GRANT_ALL, &dotnet(id))
        ],
        [Check::AllowRules, Check::UnreadableRequest]
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §opt-ins
#[case::neither(None, None, Check::UnreadableRequest)]
#[case::pre_only(Some("cmd"), None, Check::AllowRules)]
#[case::post_only(None, Some("cmd"), Check::AllowRules)]
#[case::both(Some("cmd"), Some("cmd"), Check::AllowRules)]
fn pre_post_commands_need_a_command(#[case] pre: Option<&str>, #[case] post: Option<&str>, #[case] expected: Check) {
    let mut request = winget_install();
    request.opt_ins.pre_post_commands = Some(PrePostCommands {
        pre: pre.map(str::to_owned),
        post: post.map(str::to_owned),
    });
    assert_eq!(check_of(GRANT_ALL, &request), expected);
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §opt-ins
#[case::name("tool.exe", Check::AllowRules)]
#[case::star("tool*.exe", Check::UnreadableRequest)]
#[case::question_mark("tool?.exe", Check::UnreadableRequest)]
fn processes_to_kill_have_no_wildcards(#[case] process: &str, #[case] expected: Check) {
    let mut request = winget_install();
    request.opt_ins.kill_before_operation = Some(KillBeforeOperation {
        processes: vec!["other.exe".to_owned(), process.to_owned()],
    });
    assert_eq!(check_of(GRANT_ALL, &request), expected);
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §package-request
#[case::machine_elevated(Some(Scope::Machine), Elevation::Elevated, Check::AllowRules)]
#[case::machine_non_elevated(Some(Scope::Machine), Elevation::NonElevated, Check::UnreadableRequest)]
#[case::user_non_elevated(Some(Scope::User), Elevation::NonElevated, Check::AllowRules)]
#[case::user_elevated(Some(Scope::User), Elevation::Elevated, Check::AllowRules)]
fn machine_scope_needs_elevation(#[case] scope: Option<Scope>, #[case] elevation: Elevation, #[case] expected: Check) {
    let mut request = winget_install();
    request.scope = scope;
    request.elevation = elevation;
    assert_eq!(check_of(GRANT_ALL, &request), expected);
}

#[test]
fn malformed_request_is_denied_whatever_the_policy_and_audit_mode_say() {
    // oracle: contract policies/CONTRACT.policy.md §precedence
    let policy = format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"Enforcement":{{"AuditMode":true}},"TrustedSources":{TRUST_ALL},"Rules":[{{"Id":"all","Decision":"Allow","Select":{{}}}}]}}"#
    );
    let mut request = winget_install();
    request.source.name = String::new();
    assert_eq!(
        summary(&evaluate(&policy, &request)),
        ("Deny", "UnreadableRequest", Vec::new(), Vec::new(), false)
    );
}
