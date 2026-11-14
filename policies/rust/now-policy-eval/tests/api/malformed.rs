//! Requests the types allow but the wire contract rejects as `BadRequest` get an error and no decision.

use now_policy_eval::{
    BadRequest, Check, CustomParameters, Elevation, InstallerArguments, InstallerArgumentsMode, KillBeforeOperation,
    PrePostCommands, Request, Scope,
};
use rstest::rstest;

use crate::common::{METADATA, TRUST_ALL, document, request, try_evaluate, winget_install, with_location};

/// An Allow rule for every request that grants every opt-in with any value.
const GRANT_ALL: &str = r#"[{"Id":"all","Decision":"Allow","Select":{},"Grants":{
    "Interactive":{},"SkipHashCheck":{},"InstallerArguments":{},"PrePostCommands":{},"KillBeforeOperation":{},"UninstallPrevious":{},"NoUpgrade":{},
    "CustomParameters":{"Patterns":["*"]},"CustomInstallLocation":{"Patterns":["*"]}}}]"#;

/// Returns the check of the decision for `request` under a policy that grants everything, or the error.
fn check_of(request: &Request) -> Result<Check, BadRequest> {
    try_evaluate(&document(TRUST_ALL, GRANT_ALL), request).map(|decision| decision.check)
}

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
    let check = |length| check_of(&with_length(set, length));
    assert_eq!([check(min), check(max)], [Ok(Check::AllowRules); 2]);
    assert_eq!([check(min - 1), check(max + 1)], [Err(BadRequest); 2]);
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
    let check = |length| check_of(&with_length(set, length));
    assert_eq!([check(min), check(max)], [Ok(Check::AllowRules); 2]);
    assert_eq!([check(min - 1), check(max + 1)], [Err(BadRequest); 2]);
}

#[test]
fn install_location_has_its_length() {
    // oracle: contract policies/CONTRACT.wire.md §opt-ins
    let check = |path: &str| check_of(&with_location(winget_install(), path));
    assert_eq!(check(&format!("C:\\{}", "a".repeat(2045))), Ok(Check::AllowRules));
    assert_eq!(
        [check(""), check(&format!("C:\\{}", "a".repeat(2046)))],
        [Err(BadRequest); 2]
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
        [check_of(&dotnet("Contoso.Tool")), check_of(&dotnet(id))],
        [Ok(Check::AllowRules), Err(BadRequest)]
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §opt-ins
#[case::neither(None, None, Err(BadRequest))]
#[case::pre_only(Some("cmd"), None, Ok(Check::AllowRules))]
#[case::post_only(None, Some("cmd"), Ok(Check::AllowRules))]
#[case::both(Some("cmd"), Some("cmd"), Ok(Check::AllowRules))]
fn pre_post_commands_need_a_command(
    #[case] pre: Option<&str>,
    #[case] post: Option<&str>,
    #[case] expected: Result<Check, BadRequest>,
) {
    let mut request = winget_install();
    request.opt_ins.pre_post_commands = Some(PrePostCommands {
        pre: pre.map(str::to_owned),
        post: post.map(str::to_owned),
    });
    assert_eq!(check_of(&request), expected);
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §opt-ins
#[case::name("tool.exe", Ok(Check::AllowRules))]
#[case::star("tool*.exe", Err(BadRequest))]
#[case::question_mark("tool?.exe", Err(BadRequest))]
fn processes_to_kill_have_no_wildcards(#[case] process: &str, #[case] expected: Result<Check, BadRequest>) {
    let mut request = winget_install();
    request.opt_ins.kill_before_operation = Some(KillBeforeOperation {
        processes: vec!["other.exe".to_owned(), process.to_owned()],
    });
    assert_eq!(check_of(&request), expected);
}

#[rstest]
// oracle: contract policies/CONTRACT.wire.md §package-request
#[case::machine_elevated(Some(Scope::Machine), Elevation::Elevated, Ok(Check::AllowRules))]
#[case::machine_non_elevated(Some(Scope::Machine), Elevation::NonElevated, Err(BadRequest))]
#[case::user_non_elevated(Some(Scope::User), Elevation::NonElevated, Ok(Check::AllowRules))]
#[case::user_elevated(Some(Scope::User), Elevation::Elevated, Ok(Check::AllowRules))]
fn machine_scope_needs_elevation(
    #[case] scope: Option<Scope>,
    #[case] elevation: Elevation,
    #[case] expected: Result<Check, BadRequest>,
) {
    let mut request = winget_install();
    request.scope = scope;
    request.elevation = elevation;
    assert_eq!(check_of(&request), expected);
}

#[test]
fn malformed_request_gets_an_error_whatever_the_policy_and_audit_mode_say() {
    // oracle: contract policies/CONTRACT.policy.md §request-input
    let policy = format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"Enforcement":{{"AuditMode":true}},"TrustedSources":{TRUST_ALL},"Rules":[{{"Id":"all","Decision":"Allow","Select":{{}}}}]}}"#
    );
    let mut request = winget_install();
    request.source.name = String::new();
    assert_eq!(try_evaluate(&policy, &request), Err(BadRequest));
}

#[test]
fn unreadable_request_that_is_well_formed_gets_a_denial() {
    // oracle: contract policies/CONTRACT.policy.md §unreadable
    let mut request = winget_install();
    request.package.id = "-Contoso.Tool".to_owned();
    assert_eq!(check_of(&request), Ok(Check::UnreadableRequest));
}
