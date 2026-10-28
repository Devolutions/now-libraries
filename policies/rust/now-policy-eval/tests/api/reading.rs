//! Reading requests: unreadable requests, properties, opt-ins, install locations and missing versions.

use now_policy_eval::{
    Architecture, Check, Elevation, InstallerArguments, InstallerArgumentsMode, KillBeforeOperation, PrePostCommands,
    Request, Scope,
};
use rstest::rstest;

use crate::common::{ALLOW_ALL, check_of, evaluate_rules, request, winget_install, with_location, with_parameters};

/// A request through `manager` installing `id` at `version` from `source`.
fn install(manager: &str, source: &str, id: &str, version: &str) -> Request {
    let mut request = request("Install", manager, source, id);
    request.package.version = Some(version.to_owned());
    request
}

fn with(mut request: Request, change: fn(&mut Request)) -> Request {
    change(&mut request);
    request
}

fn elevated(mut request: Request) -> Request {
    request.elevation = Elevation::Elevated;
    request
}

fn installer_arguments(arguments: &str) -> InstallerArguments {
    InstallerArguments {
        arguments: arguments.to_owned(),
        mode: InstallerArgumentsMode::Append,
    }
}

/// Returns the opt-ins a request uses, as the ungranted opt-ins of a catch-all Allow rule without grants.
fn opt_ins(request: &Request) -> Vec<&'static str> {
    evaluate_rules(ALLOW_ALL, request)
        .opt_ins
        .iter()
        .map(|opt_in| opt_in.as_str())
        .collect()
}

/// An Allow rule for every request that grants every opt-in with any value.
const GRANT_ALL: &str = r#"[{"Id":"all","Decision":"Allow","Select":{},"Grants":{
    "Interactive":{},"SkipHashCheck":{},"InstallerArguments":{},"PrePostCommands":{},"KillBeforeOperation":{},"UninstallPrevious":{},"NoUpgrade":{},
    "CustomParameters":{"Patterns":["*"]},"CustomInstallLocation":{"Patterns":["*"]}}}]"#;

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
#[case::custom_parameter_with_line_feed(with_parameters(winget_install(), &["/a\nb"]))]
#[case::custom_parameter_with_carriage_return(with_parameters(winget_install(), &["/a\rb"]))]
#[case::custom_parameter_with_nul(with_parameters(winget_install(), &["/a\0b"]))]
#[case::installer_arguments_with_line_feed(with(winget_install(), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/a\nb"))))]
#[case::installer_arguments_with_carriage_return(with(winget_install(), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/a\rb"))))]
#[case::installer_arguments_with_nul(with(winget_install(), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/a\0b"))))]
#[case::source_name_with_nul(request("Uninstall", "Dotnet", "nuget\0org", "dotnet-ef"))]
#[case::source_name_with_line_feed(request("Uninstall", "Dotnet", "nuget\norg", "dotnet-ef"))]
#[case::source_name_with_carriage_return(request("Uninstall", "Dotnet", "nuget\rorg", "dotnet-ef"))]
// oracle: contract policies/CONTRACT.policy.md §reading
// oracle: contract policies/CONTRACT.policy.md §manager-winget
#[case::winget_id_with_percent(request("Uninstall", "Winget", "winget", "Contoso%Tool"))]
#[case::winget_id_starting_with_dash(request("Uninstall", "Winget", "winget", "-Contoso"))]
#[case::dotnet_id_starting_with_dash(install("Dotnet", "nuget.org", "-dotnet-ef", "8.0.0"))]
#[case::winget_location_with_percent(with_location(winget_install(), "C:\\Apps\\%x"))]
#[case::winget_custom_parameter_with_ampersand(with_parameters(winget_install(), &["/a&b"]))]
#[case::winget_custom_parameter_with_quote(with_parameters(winget_install(), &["/a\"b"]))]
#[case::winget_custom_parameter_with_caret(with_parameters(winget_install(), &["/a^b"]))]
#[case::winget_installer_arguments_with_pipe(with(winget_install(), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/a|b"))))]
#[case::winget_installer_arguments_with_pipe_on_uninstall(with(request("Uninstall", "Winget", "winget", "Contoso.Tool"), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/a|b"))))]
#[case::winget_source_name_with_wide_space(request("Install", "Winget", "my\u{3000}source", "Contoso.Tool"))]
#[case::winget_non_ascii_source_name(request("Uninstall", "Winget", "Dépôt", "Contoso.Tool"))]
#[case::winget_source_name_starting_with_underscore(request("Uninstall", "Winget", "_source", "Contoso.Tool"))]
fn unreadable_requests_by_characters(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::UnreadableRequest);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
// oracle: spec Unicode 16.0.0 PropList.txt White_Space
// oracle: spec Unicode 16.0.0 DerivedCoreProperties.txt Default_Ignorable_Code_Point
#[case::leading_space(" nuget")]
#[case::trailing_tab("nuget\t")]
#[case::trailing_no_break_space("nuget\u{A0}")]
#[case::leading_ideographic_space("\u{3000}nuget")]
#[case::trailing_next_line("nuget\u{85}")]
#[case::zero_width_space("nu\u{200B}get")]
#[case::soft_hyphen("nu\u{AD}get")]
#[case::mongolian_vowel_separator("nu\u{180E}get")]
#[case::variation_selector("nuget\u{FE0F}")]
#[case::tag_character("nu\u{E0041}get")]
fn source_names_that_look_like_another_are_unreadable(#[case] name: &str) {
    // The source of a Dotnet Uninstall is not passed to the manager, so only the generic rule applies to its name.
    assert_eq!(
        check_of(GRANT_ALL, &request("Uninstall", "Dotnet", name, "dotnet-ef")),
        Check::UnreadableRequest
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
#[case::inner_space("my source")]
#[case::inner_ideographic_space("my\u{3000}source")]
#[case::non_ascii("Dépôt")]
#[case::length_128(&"s".repeat(128))]
fn distinct_source_names_are_readable(#[case] name: &str) {
    assert_eq!(
        check_of(GRANT_ALL, &request("Uninstall", "Dotnet", name, "dotnet-ef")),
        Check::AllowRules
    );
}

#[test]
fn package_id_of_256_allowed_characters_is_readable() {
    // oracle: contract policies/CONTRACT.wire.md §package-request
    let characters = "aZ09._+/:[],#$%{}-";
    let id = characters.repeat(256 / characters.len() + 1)[..256].to_owned();
    assert_eq!(
        check_of(GRANT_ALL, &install("Dotnet", "nuget.org", &id, "8.0.0")),
        Check::AllowRules
    );
}

#[test]
fn input_bounds_at_their_maximum_are_readable() {
    // oracle: contract policies/CONTRACT.wire.md §package-request
    let mut request = with_location(winget_install(), &format!("C:\\{}", "a".repeat(2045)));
    request.source.name = "s".repeat(128);
    request.package.version = Some("1".repeat(128));
    request.opt_ins.custom_parameters = with_parameters(winget_install(), &["x"]).opt_ins.custom_parameters;
    if let Some(parameters) = request.opt_ins.custom_parameters.as_mut() {
        parameters.values = vec!["p".repeat(512); 64];
    }
    request.opt_ins.installer_arguments = Some(installer_arguments(&"a".repeat(2048)));
    request.opt_ins.pre_post_commands = Some(PrePostCommands {
        pre: Some("c".repeat(2048)),
        post: Some("c".to_owned()),
    });
    request.opt_ins.kill_before_operation = Some(KillBeforeOperation {
        processes: vec!["k".repeat(128); 64],
    });
    assert_eq!(check_of(GRANT_ALL, &request), Check::AllowRules);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
#[case::apt(install("Apt", "main", "git", "1.0"))]
#[case::dnf(install("Dnf", "fedora", "git", "1.0"))]
#[case::flatpak(install("Flatpak", "flathub", "org.gimp.GIMP", "1.0"))]
#[case::homebrew(install("Homebrew", "homebrew/core", "git", "1.0"))]
#[case::pacman(install("Pacman", "core", "git", "1.0"))]
#[case::snap(install("Snap", "snapcraft", "git", "1.0"))]
#[case::custom_parameters_through_chocolatey(elevated(with_parameters(install("Chocolatey", "chocolatey", "git", "1.0"), &["--force"])))]
#[case::custom_parameters_through_powershell(with_parameters(install("PowerShell", "PSGallery", "Pester", "1.0"), &["-Force"]))]
#[case::custom_parameters_through_npm(with_parameters(install("Npm", "npm", "react", "1.0.0"), &["--force"]))]
#[case::custom_parameters_through_cargo(with_parameters(install("Cargo", "crates.io", "serde", "1.0.0"), &["--force"]))]
#[case::custom_parameters_through_pip(with_parameters(install("Pip", "pip", "requests", "2.31"), &["--force"]))]
#[case::custom_parameters_through_dotnet(with_parameters(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), &["--force"]))]
#[case::custom_parameters_through_vcpkg(with_parameters(request("Install", "Vcpkg", "x64-windows", "zlib"), &["--force"]))]
#[case::installer_arguments_through_scoop(with(request("Install", "Scoop", "main", "git"), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/q"))))]
#[case::installer_arguments_through_chocolatey(elevated(with(install("Chocolatey", "chocolatey", "git", "1.0"), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/q")))))]
#[case::installer_arguments_through_powershell(with(install("PowerShell", "PSGallery", "Pester", "1.0"), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/q"))))]
#[case::version_through_scoop(install("Scoop", "main", "git", "1.0"))]
#[case::version_through_vcpkg(install("Vcpkg", "x64-windows", "zlib", "1.0"))]
#[case::other_npm_source(install("Npm", "registry", "react", "1.0.0"))]
#[case::other_bun_source(install("Bun", "npm", "react", "1.0.0"))]
#[case::other_cargo_source(install("Cargo", "crates", "serde", "1.0.0"))]
#[case::other_pip_source(install("Pip", "pypi", "requests", "1.0"))]
#[case::other_dotnet_source_on_install(install("Dotnet", "nuget", "dotnet-ef", "8.0.0"))]
#[case::npm_source_with_non_ascii_case(install("Npm", "\u{1E3F}pm", "react", "1.0.0"))]
#[case::powershell_wildcard_source(install("PowerShell", "PS*Gallery", "Pester", "1.0"))]
#[case::powershell_source_starting_with_dot(install("PowerShell", ".PSGallery", "Pester", "1.0"))]
#[case::powershell7_source_with_bang(install("PowerShell7", "PSGallery!", "Pester", "1.0"))]
#[case::powershell_non_ascii_source(install("PowerShell", "Dépôt", "Pester", "1.0"))]
#[case::powershell_version_range(install("PowerShell", "PSGallery", "Pester", "[5.0,6.0)"))]
#[case::powershell_id_with_bracket(install("PowerShell", "PSGallery", "Pester[1]", "5.0"))]
#[case::chocolatey_install_with_other_source_characters(elevated(install("Chocolatey", "choco!", "git", "2.4")))]
#[case::bun_version_that_is_not_semantic(install("Bun", "Bun", "react", "18.2"))]
#[case::bun_version_with_leading_zero(install("Bun", "Bun", "react", "18.02.0"))]
#[case::bun_alias_is_not_an_identifier(install("Bun", "Bun", "my-react:react", "18.2.0"))]
#[case::npm_identifier_outside_the_registry(install("Npm", "npm", "github:user/repo", "1.0.0"))]
#[case::npm_version_with_caret(install("Npm", "npm", "react", "^18.2.0"))]
fn unreadable_requests_through_managers(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::UnreadableRequest);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
#[case::npm_source_in_ascii_upper_case(install("Npm", "NPM", "react", "1.0.0"))]
#[case::bun_source_in_lower_case(install("Bun", "bun", "react", "1.0.0"))]
#[case::cargo_source_in_mixed_case(install("Cargo", "Crates.IO", "serde", "1.0.0"))]
#[case::pip_source_in_upper_case(install("Pip", "PIP", "requests", "2.31"))]
#[case::dotnet_source_in_upper_case(install("Dotnet", "NuGet.org", "dotnet-ef", "8.0.0"))]
#[case::npm_channel_like_version(install("Npm", "npm", "react", "1.0.0-next.1"))]
#[case::powershell_source_with_space_dot_dash_underscore(install("PowerShell", "My Repo_1.2-x", "Pester", "5.0"))]
#[case::scoop_bucket_without_version(request("Install", "Scoop", "extras", "git"))]
#[case::scoop_manifest_url(request(
    "Install",
    "Scoop",
    "https://example.test/git.json",
    "https://example.test/git.json"
))]
#[case::vcpkg_without_version(request("Install", "Vcpkg", "x64-windows", "zlib"))]
#[case::scoop_custom_parameters(with_parameters(request("Install", "Scoop", "main", "git"), &["--verbose"]))]
#[case::chocolatey_uninstall_with_any_source(elevated(request("Uninstall", "Chocolatey", "any source!", "git")))]
#[case::bun_semantic_version(install("Bun", "Bun", "react", "18.2.0-rc.1+build.5"))]
fn readable_requests_through_managers(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::AllowRules);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §manager-scoop
#[case::elevated(elevated(request("Install", "Scoop", "main", "git")))]
#[case::interactive(with(request("Install", "Scoop", "main", "git"), |r| r.opt_ins.interactive = true))]
#[case::install_location(with_location(request("Install", "Scoop", "main", "git"), "C:\\Apps"))]
#[case::no_upgrade(with(request("Install", "Scoop", "main", "git"), |r| r.opt_ins.no_upgrade = true))]
#[case::uninstall_previous(with(request("Install", "Scoop", "main", "git"), |r| r.opt_ins.uninstall_previous = true))]
#[case::uninstall_with_skip_hash_check(with(request("Uninstall", "Scoop", "main", "git"), |r| r.opt_ins.skip_hash_check = true))]
#[case::update_with_architecture(with(request("Update", "Scoop", "main", "git"), |r| r.package.architecture = Some(Architecture::X64)))]
#[case::install_with_neutral_architecture(with(request("Install", "Scoop", "main", "git"), |r| r.package.architecture = Some(Architecture::Neutral)))]
#[case::id_with_bracket(request("Install", "Scoop", "main", "git[1]"))]
#[case::id_starting_with_dash(request("Install", "Scoop", "main", "-git"))]
#[case::id_ending_with_dot(request("Install", "Scoop", "main", "git."))]
#[case::bucket_with_json_id(request("Install", "Scoop", "main", "git.json"))]
#[case::manifest_url_with_other_id(request("Install", "Scoop", "https://example.test/git.json", "git"))]
#[case::manifest_url_in_another_case(request(
    "Install",
    "Scoop",
    "https://example.test/git.json",
    "HTTPS://example.test/git.json"
))]
#[case::global_parameter(with_parameters(request("Install", "Scoop", "main", "git"), &["--global"]))]
#[case::arch_parameter_in_upper_case(with_parameters(request("Install", "Scoop", "main", "git"), &["--ARCH=x"]))]
#[case::short_cluster_with_global(with_parameters(request("Install", "Scoop", "main", "git"), &["-G"]))]
#[case::short_cluster_with_architecture(with_parameters(request("Install", "Scoop", "main", "git"), &["-qa"]))]
#[case::skip_hash_check_long(with_parameters(request("Install", "Scoop", "main", "git"), &["--skip-hash-check"]))]
#[case::skip_hash_check_short(with_parameters(request("Install", "Scoop", "main", "git"), &["-s"]))]
#[case::skip_hash_check_in_cluster(with_parameters(request("Install", "Scoop", "main", "git"), &["-ks"]))]
#[case::end_of_options(with_parameters(request("Install", "Scoop", "main", "git"), &["--"]))]
// oracle: contract policies/CONTRACT.policy.md §manager-scoop
#[case::lone_dash(with_parameters(request("Install", "Scoop", "main", "git"), &["-"]))]
#[case::skip_hash_check_after_equals_in_cluster(with_parameters(request("Install", "Scoop", "main", "git"), &["-q=s"]))]
#[case::parameter_that_is_not_an_option(with_parameters(request("Install", "Scoop", "main", "git"), &["value"]))]
#[case::parameter_with_space(with_parameters(request("Install", "Scoop", "main", "git"), &["--x=a b"]))]
fn unreadable_requests_through_scoop(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::UnreadableRequest);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §manager-scoop
#[case::similar_long_option(&["--globalx"])]
#[case::short_option(&["-q"])]
#[case::double_dash_is_not_a_cluster(&["--s"])]
fn readable_requests_through_scoop(#[case] custom_parameters: &[&str]) {
    let subject = with_parameters(request("Install", "Scoop", "main", "git"), custom_parameters);
    assert_eq!(opt_ins(&subject), ["CustomParameters"]);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
#[case::vcpkg_elevated(elevated(request("Install", "Vcpkg", "x64-windows", "zlib")))]
#[case::vcpkg_interactive(with(request("Install", "Vcpkg", "x64-windows", "zlib"), |r| r.opt_ins.interactive = true))]
#[case::vcpkg_architecture(with(request("Install", "Vcpkg", "x64-windows", "zlib"), |r| r.package.architecture = Some(Architecture::X64)))]
#[case::vcpkg_source_that_is_not_a_triplet(request("Install", "Vcpkg", "x64 windows", "zlib"))]
#[case::vcpkg_id_with_other_triplet(request("Install", "Vcpkg", "x64-windows", "zlib:x86-windows"))]
#[case::vcpkg_id_with_triplet_in_other_case(request("Install", "Vcpkg", "x64-windows", "zlib:X64-windows"))]
#[case::vcpkg_id_with_upper_case_port(request("Install", "Vcpkg", "x64-windows", "Zlib"))]
#[case::npm_elevated(elevated(install("Npm", "npm", "react", "1.0.0")))]
#[case::npm_skip_hash_check(with(install("Npm", "npm", "react", "1.0.0"), |r| r.opt_ins.skip_hash_check = true))]
#[case::npm_install_location(with_location(install("Npm", "npm", "react", "1.0.0"), "C:\\Apps"))]
#[case::npm_architecture(with(install("Npm", "npm", "react", "1.0.0"), |r| r.package.architecture = Some(Architecture::X64)))]
#[case::npm_id_with_percent(install("Npm", "npm", "re%act", "1.0.0"))]
#[case::cargo_elevated(elevated(install("Cargo", "crates.io", "serde", "1.0.0")))]
#[case::cargo_pre_post_commands(with(install("Cargo", "crates.io", "serde", "1.0.0"), |r| r.opt_ins.pre_post_commands = Some(PrePostCommands { pre: Some("x".to_owned()), post: None })))]
#[case::cargo_kill_before_operation(with(install("Cargo", "crates.io", "serde", "1.0.0"), |r| r.opt_ins.kill_before_operation = Some(KillBeforeOperation { processes: vec!["a.exe".to_owned()] })))]
#[case::cargo_location_with_batch_character(with_location(
    install("Cargo", "crates.io", "serde", "1.0.0"),
    "C:\\Apps\\%x"
))]
#[case::cargo_uninstall_with_version(with(request("Uninstall", "Cargo", "crates.io", "serde"), |r| r.package.version = Some("1.0.0".to_owned())))]
#[case::cargo_id_with_dot(install("Cargo", "crates.io", "serde.json", "1.0.0"))]
#[case::pip_id_ending_with_dash(install("Pip", "pip", "requests-", "2.31"))]
#[case::pip_uninstall_with_version(with(request("Uninstall", "Pip", "pip", "requests"), |r| r.package.version = Some("2.31".to_owned())))]
#[case::dotnet_machine_scope(elevated(with(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), |r| r.scope = Some(Scope::Machine))))]
#[case::dotnet_uninstall_with_architecture(with(request("Uninstall", "Dotnet", "nuget.org", "dotnet-ef"), |r| r.package.architecture = Some(Architecture::X64)))]
#[case::chocolatey_user_scope(elevated(with(install("Chocolatey", "chocolatey", "git", "2.4"), |r| r.scope = Some(Scope::User))))]
#[case::chocolatey_arm64(elevated(with(install("Chocolatey", "chocolatey", "git", "2.4"), |r| r.package.architecture = Some(Architecture::Arm64))))]
#[case::chocolatey_install_location(elevated(with_location(
    install("Chocolatey", "chocolatey", "git", "2.4"),
    "C:\\Apps"
)))]
#[case::chocolatey_id_ending_with_nupkg(elevated(install("Chocolatey", "chocolatey", "git.NUPKG", "2.4")))]
#[case::chocolatey_id_all(elevated(install("Chocolatey", "chocolatey", "ALL", "2.4")))]
fn unreadable_requests_through_a_manager_that_cannot_carry_them(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::UnreadableRequest);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §opt-ins
#[case::interactive(|r: &mut Request| r.opt_ins.interactive = true, &["Interactive"])]
#[case::skip_hash_check(|r: &mut Request| r.opt_ins.skip_hash_check = true, &["SkipHashCheck"])]
#[case::install_location(|r: &mut Request| r.opt_ins = with_location(request("Install", "Winget", "winget", "x"), "C:\\Apps").opt_ins, &["CustomInstallLocation"])]
#[case::custom_parameter(|r: &mut Request| r.opt_ins = with_parameters(request("Install", "Winget", "winget", "x"), &["/quiet"]).opt_ins, &["CustomParameters"])]
#[case::installer_arguments(|r: &mut Request| r.opt_ins.installer_arguments = Some(installer_arguments("/quiet")), &["InstallerArguments"])]
#[case::pre_operation_command(|r: &mut Request| r.opt_ins.pre_post_commands = Some(PrePostCommands { pre: Some("cmd".to_owned()), post: None }), &["PrePostCommands"])]
#[case::post_operation_command(|r: &mut Request| r.opt_ins.pre_post_commands = Some(PrePostCommands { pre: None, post: Some("cmd".to_owned()) }), &["PrePostCommands"])]
#[case::kill_before_operation(|r: &mut Request| r.opt_ins.kill_before_operation = Some(KillBeforeOperation { processes: vec!["tool.exe".to_owned()] }), &["KillBeforeOperation"])]
#[case::uninstall_previous(|r: &mut Request| r.opt_ins.uninstall_previous = true, &["UninstallPrevious"])]
#[case::no_upgrade(|r: &mut Request| r.opt_ins.no_upgrade = true, &["NoUpgrade"])]
#[case::every_opt_in_in_table_order(
    |r: &mut Request| {
        r.opt_ins = with_parameters(with_location(request("Install", "Winget", "winget", "x"), "C:\\Apps"), &["/quiet"]).opt_ins;
        r.opt_ins.no_upgrade = true;
        r.opt_ins.uninstall_previous = true;
        r.opt_ins.kill_before_operation = Some(KillBeforeOperation { processes: vec!["tool.exe".to_owned()] });
        r.opt_ins.pre_post_commands = Some(PrePostCommands { pre: Some("cmd".to_owned()), post: None });
        r.opt_ins.installer_arguments = Some(installer_arguments("/quiet"));
        r.opt_ins.skip_hash_check = true;
        r.opt_ins.interactive = true;
    },
    &["Interactive", "SkipHashCheck", "CustomInstallLocation", "CustomParameters", "InstallerArguments", "PrePostCommands", "KillBeforeOperation", "UninstallPrevious", "NoUpgrade"]
)]
fn winget_opt_ins_are_used_when_present(#[case] change: fn(&mut Request), #[case] expected: &[&str]) {
    assert_eq!(opt_ins(&with(winget_install(), change)), expected);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §manager-winget
#[case::long_option_with_value(&["--location", "C:\\Apps"])]
#[case::short_option_with_value(&["-l", "C:\\Apps"])]
#[case::long_option_ignoring_ascii_case_with_equals(&["--LOCATION=C:\\Apps"])]
#[case::short_option_sharing_its_element(&["-il", "C:\\Apps"])]
#[case::interactive_long(&["--interactive"])]
#[case::interactive_long_ignoring_ascii_case(&["--INTERACTIVE"])]
#[case::interactive_short(&["-i"])]
#[case::interactive_in_short_cluster(&["-ei"])]
#[case::long_option_name_ends_at_equals(&["--interactive=false"])]
#[case::override_with_value(&["--override", "/quiet"])]
#[case::override_without_value(&["--override"])]
#[case::custom_with_equals(&["--custom=/quiet"])]
#[case::ignore_security_hash(&["--ignore-security-hash"])]
#[case::force(&["/quiet", "--force"])]
#[case::no_upgrade(&["--no-upgrade"])]
#[case::uninstall_previous(&["--uninstall-previous"])]
#[case::source(&["--source", "msstore"])]
#[case::short_source(&["-s", "msstore"])]
#[case::manifest(&["--manifest=app.yaml"])]
#[case::short_manifest(&["-m", "app.yaml"])]
fn winget_custom_parameters_asking_for_another_opt_in_or_source_are_unreadable(#[case] custom_parameters: &[&str]) {
    assert_eq!(
        check_of(GRANT_ALL, &with_parameters(winget_install(), custom_parameters)),
        Check::UnreadableRequest
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §manager-winget
#[case::plain_parameter(&["/quiet"])]
#[case::short_option_in_other_case(&["-I"])]
#[case::short_options_end_at_equals(&["-e=i"])]
#[case::option_after_end_of_options(&["--", "--interactive"])]
#[case::lone_dash_is_not_an_option(&["-", "x"])]
#[case::value_of_an_option_is_not_an_option(&["--architecture", "--interactive"])]
#[case::value_of_an_option_after_equals_is_not_an_option(&["--architecture=--force"])]
fn winget_custom_parameters_that_ask_for_nothing_else_are_custom_parameters_only(#[case] custom_parameters: &[&str]) {
    assert_eq!(
        opt_ins(&with_parameters(winget_install(), custom_parameters)),
        ["CustomParameters"]
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
#[case::interactive_under_powershell(
    with(install("PowerShell", "PSGallery", "Pester", "5.0"), |r| r.opt_ins.interactive = true),
    &["Interactive"]
)]
#[case::install_location_under_powershell(
    with_location(install("PowerShell", "PSGallery", "Pester", "5.0"), "C:\\Apps"),
    &["CustomInstallLocation"]
)]
// oracle: contract policies/CONTRACT.policy.md §manager-winget
#[case::installer_arguments_on_a_winget_uninstall(
    with(request("Uninstall", "Winget", "winget", "Contoso.Tool"), |r| r.opt_ins.installer_arguments = Some(installer_arguments("/quiet"))),
    &["InstallerArguments"]
)]
fn opt_ins_the_command_does_not_carry_are_still_used(#[case] subject: Request, #[case] expected: &[&str]) {
    assert_eq!(opt_ins(&subject), expected);
}

#[test]
fn install_location_the_command_does_not_carry_is_checked() {
    // oracle: contract policies/CONTRACT.policy.md §reading
    // oracle: contract policies/CONTRACT.policy.md §install-location
    let request = with_location(install("PowerShell", "PSGallery", "Pester", "5.0"), "Apps");
    assert_eq!(check_of(GRANT_ALL, &request), Check::InstallLocation);
}

/// An Allow rule granting any install location and custom parameter.
const GRANT_LOCATIONS: &str = r#"[{"Id":"all","Decision":"Allow","Select":{},"Grants":{"CustomInstallLocation":{"Patterns":["*"]},"CustomParameters":{"Patterns":["*"]}}}]"#;

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §install-location
#[case::drive_root("C:\\")]
#[case::drive_root_with_slash("c:/")]
#[case::directory("C:\\Program Files\\Contoso")]
#[case::forward_slashes_and_repeated_separators("d://apps//tool/")]
#[case::inner_dot_and_space("C:\\a.b c\\x")]
fn plain_local_paths_pass(#[case] location: &str) {
    let request = with_location(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), location);
    assert_eq!(check_of(GRANT_LOCATIONS, &request), Check::AllowRules);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §install-location
#[case::drive_without_separator("C:")]
#[case::drive_relative("C:Apps")]
#[case::relative("Apps\\Tool")]
#[case::rooted_without_drive("\\Apps")]
#[case::unc("\\\\server\\share\\Apps")]
#[case::device_path("\\\\?\\C:\\Apps")]
#[case::digit_drive("1:\\Apps")]
#[case::non_ascii_drive("É:\\Apps")]
#[case::dot_segment("C:\\Apps\\.\\Tool")]
#[case::dot_dot_segment("C:\\Apps\\..\\Windows")]
#[case::segment_ending_with_dot("C:\\Apps.")]
#[case::segment_ending_with_space("C:\\Apps \\Tool")]
#[case::segment_starting_with_space("C:\\ Apps")]
#[case::colon_in_segment("C:\\Apps\\a:b")]
#[case::alternate_data_stream("C:\\Apps\\tool.exe:stream")]
#[case::wildcard("C:\\Apps\\*")]
#[case::question_mark("C:\\Apps\\?")]
#[case::quote("C:\\Apps\\\"x\"")]
#[case::angle_bracket("C:\\Apps\\<x")]
#[case::pipe("C:\\Apps\\a|b")]
#[case::c0_control("C:\\Apps\\a\u{1}b")]
#[case::delete_control("C:\\Apps\\a\u{7F}b")]
#[case::c1_control("C:\\Apps\\a\u{9F}b")]
fn other_install_locations_fail(#[case] location: &str) {
    let request = with_location(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), location);
    assert_eq!(check_of(GRANT_LOCATIONS, &request), Check::InstallLocation);
}

#[test]
fn install_location_check_reads_only_the_install_location() {
    // oracle: contract policies/CONTRACT.policy.md §install-location
    let mut request = with_parameters(winget_install(), &["--scope=user"]);
    request.opt_ins.installer_arguments = Some(installer_arguments("Apps"));
    request.opt_ins.custom_install_location = with_location(winget_install(), "C:\\Apps")
        .opt_ins
        .custom_install_location;
    assert_eq!(check_of(GRANT_ALL, &request), Check::AllowRules);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §missing-version
#[case::winget_install_without_version(request("Install", "Winget", "winget", "Contoso.Tool"), Check::MissingVersion)]
#[case::winget_update_without_version(request("Update", "Winget", "winget", "Contoso.Tool"), Check::MissingVersion)]
#[case::winget_uninstall_without_version(request("Uninstall", "Winget", "winget", "Contoso.Tool"), Check::AllowRules)]
#[case::winget_version_that_is_not_a_version(install("Winget", "winget", "Contoso.Tool", "latest"), Check::AllowRules)]
#[case::winget_identifier_selects_no_version(
    install("Winget", "winget", "Contoso.Tool@1.2.3", "1.2.3"),
    Check::AllowRules
)]
#[case::dotnet_identifier_selects_the_version(
    install("Dotnet", "nuget.org", "dotnet-ef@8.0.0", "8.0.0"),
    Check::MissingVersion
)]
#[case::leading_at_selects_no_version(install("Winget", "winget", "@Contoso", "1.2.3"), Check::AllowRules)]
#[case::version_option(with_parameters(winget_install(), &["--version", "1.2.3"]), Check::MissingVersion)]
#[case::short_version_option_without_value(with_parameters(winget_install(), &["-v"]), Check::MissingVersion)]
#[case::version_option_takes_an_option_as_its_value(
    with_parameters(winget_install(), &["--version", "--interactive"]),
    Check::MissingVersion
)]
#[case::npm_two_components(install("Npm", "npm", "react", "18.2"), Check::MissingVersion)]
#[case::npm_tilde_range(install("Npm", "npm", "react", "~18.2.0"), Check::MissingVersion)]
#[case::npm_wildcard_range(install("Npm", "npm", "react", "18.*"), Check::MissingVersion)]
#[case::npm_three_components(install("Npm", "npm", "react", "18.2.0"), Check::AllowRules)]
#[case::npm_prerelease(install("Npm", "npm", "react", "19.0.0-rc.1"), Check::AllowRules)]
#[case::bun_three_components(install("Bun", "Bun", "react", "18.2.0"), Check::AllowRules)]
// oracle: contract policies/CONTRACT.policy.md §missing-version
#[case::powershell7_range(install("PowerShell7", "PSGallery", "Pester", "[5.0,6.0)"), Check::MissingVersion)]
#[case::powershell7_wildcard(install("PowerShell7", "PSGallery", "Pester", "5.*"), Check::MissingVersion)]
#[case::powershell7_open_range(install("PowerShell7", "PSGallery", "Pester", "[5.0,]"), Check::MissingVersion)]
#[case::powershell7_version(install("PowerShell7", "PSGallery", "Pester", "5.3.3"), Check::AllowRules)]
#[case::powershell7_two_components(install("PowerShell7", "PSGallery", "Pester", "5.3"), Check::AllowRules)]
#[case::powershell7_prerelease(install("PowerShell7", "PSGallery", "Pester", "5.4.0-rc1"), Check::AllowRules)]
// oracle: contract policies/CONTRACT.policy.md §manager-npm
#[case::npm_alias_suffix_selects_no_version(
    install("Npm", "npm", "my-react:react@18.2.0", "18.2.0"),
    Check::AllowRules
)]
#[case::npm_scoped_name_selects_no_version(install("Npm", "npm", "@contoso/ui", "1.2.3"), Check::AllowRules)]
#[case::cargo_one_component(install("Cargo", "crates.io", "serde", "1"), Check::MissingVersion)]
#[case::cargo_three_components(install("Cargo", "crates.io", "serde", "1.0.0"), Check::AllowRules)]
#[case::dotnet_two_components(install("Dotnet", "nuget.org", "dotnet-ef", "8.0"), Check::MissingVersion)]
#[case::dotnet_four_components(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0.1"), Check::AllowRules)]
#[case::chocolatey_two_components(elevated(install("Chocolatey", "chocolatey", "git", "2.4")), Check::AllowRules)]
#[case::pip_two_components(install("Pip", "pip", "requests", "2.31"), Check::AllowRules)]
#[case::scoop_install_without_version(request("Install", "Scoop", "main", "git"), Check::AllowRules)]
#[case::vcpkg_update_without_version(request("Update", "Vcpkg", "x64-windows", "zlib"), Check::AllowRules)]
fn missing_versions(#[case] subject: Request, #[case] expected: Check) {
    assert_eq!(check_of(GRANT_ALL, &subject), expected);
}

/// Returns the check of `request` under an Allow rule selecting `condition`, with every opt-in granted.
fn check_selecting(condition: &str, request: &Request) -> Check {
    let rules = GRANT_ALL.replace(r#""Select":{}"#, &format!(r#""Select":{{{condition}}}"#));
    check_of(&rules, request)
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
#[case::winget_field(elevated(with(winget_install(), |r| r.scope = Some(Scope::Machine))), "Machine")]
#[case::winget_option(elevated(with_parameters(winget_install(), &["--scope", "machine"])), "Machine")]
#[case::winget_option_ignoring_ascii_case(with_parameters(winget_install(), &["--scope=USER"]), "User")]
#[case::winget_option_after_end_of_options(with_parameters(with(winget_install(), |r| r.scope = Some(Scope::User)), &["--", "--scope", "machine"]), "User")]
#[case::default_user(request("Uninstall", "Npm", "npm", "react"), "User")]
#[case::chocolatey_default_machine(elevated(request("Uninstall", "Chocolatey", "chocolatey", "git")), "Machine")]
#[case::chocolatey_field(elevated(with(request("Uninstall", "Chocolatey", "chocolatey", "git"), |r| r.scope = Some(Scope::Machine))), "Machine")]
#[case::powershell_install(install("PowerShell", "PSGallery", "Pester", "5.0"), "User")]
#[case::pip_install(install("Pip", "pip", "requests", "2.31"), "User")]
fn known_scopes(#[case] subject: Request, #[case] scope: &str) {
    assert_eq!(
        check_selecting(&format!(r#""Scopes":["{scope}"]"#), &subject),
        Check::AllowRules
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
// oracle: contract policies/CONTRACT.policy.md §applicability
#[case::winget_without_scope(winget_install())]
#[case::winget_field_and_option(elevated(with_parameters(with(winget_install(), |r| r.scope = Some(Scope::Machine)), &["--scope", "machine"])))]
#[case::winget_two_options(elevated(with_parameters(winget_install(), &["--scope", "user", "--scope", "user"])))]
#[case::winget_option_with_other_value(elevated(with_parameters(winget_install(), &["--scope", "global"])))]
#[case::winget_option_without_value(elevated(with_parameters(winget_install(), &["--scope"])))]
#[case::pip_uninstall(with(request("Uninstall", "Pip", "pip", "requests"), |r| r.scope = Some(Scope::User)))]
#[case::powershell_uninstall(request("Uninstall", "PowerShell", "PSGallery", "Pester"))]
#[case::powershell7_uninstall(request("Uninstall", "PowerShell7", "PSGallery", "Pester"))]
fn unknown_scopes(#[case] subject: Request) {
    assert_eq!(
        check_selecting(r#""Scopes":["User","Machine"]"#, &subject),
        Check::DefaultDeny
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
#[case::machine_option_without_elevation(with_parameters(winget_install(), &["--scope", "machine"]))]
#[case::machine_option_with_user_scope(with_parameters(with(winget_install(), |r| r.scope = Some(Scope::User)), &["--scope=machine"]))]
#[case::scope_option_without_value(with_parameters(winget_install(), &["--scope"]))]
#[case::scope_option_with_other_value(with_parameters(winget_install(), &["--scope", "global"]))]
#[case::scope_option_twice(with_parameters(winget_install(), &["--scope", "user", "--scope", "user"]))]
#[case::scope_option_and_field(with_parameters(with(winget_install(), |r| r.scope = Some(Scope::User)), &["--scope", "user"]))]
#[case::chocolatey_default_machine(install("Chocolatey", "chocolatey", "git", "2.4"))]
#[case::chocolatey_uninstall_default_machine(request("Uninstall", "Chocolatey", "chocolatey", "git"))]
fn scope_that_may_be_machine_without_elevation_is_unreadable(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::UnreadableRequest);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
#[case::winget_field(with(winget_install(), |r| r.package.architecture = Some(Architecture::Arm64)), "Arm64")]
#[case::winget_option(with_parameters(winget_install(), &["-a", "X86"]), "X86")]
#[case::scoop_field(with(request("Install", "Scoop", "main", "git"), |r| r.package.architecture = Some(Architecture::X64)), "X64")]
#[case::vcpkg_triplet(request("Install", "Vcpkg", "x64-windows", "zlib"), "X64")]
#[case::vcpkg_triplet_ignoring_ascii_case(request("Install", "Vcpkg", "ARM64-Linux", "zlib"), "Arm64")]
#[case::npm_default(install("Npm", "npm", "react", "18.2.0"), "Neutral")]
#[case::bun_default(install("Bun", "Bun", "react", "18.2.0"), "Neutral")]
#[case::cargo_default(install("Cargo", "crates.io", "serde", "1.0.0"), "Neutral")]
#[case::pip_default(install("Pip", "pip", "requests", "2.31"), "Neutral")]
#[case::dotnet_field(with(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), |r| r.package.architecture = Some(Architecture::X64)), "X64")]
#[case::chocolatey_field(elevated(with(install("Chocolatey", "chocolatey", "git", "2.4"), |r| r.package.architecture = Some(Architecture::X86))), "X86")]
#[case::powershell_whatever_the_field(with(install("PowerShell", "PSGallery", "Pester", "5.0"), |r| r.package.architecture = Some(Architecture::X64)), "Neutral")]
fn known_architectures(#[case] subject: Request, #[case] architecture: &str) {
    assert_eq!(
        check_selecting(&format!(r#""Architectures":["{architecture}"]"#), &subject),
        Check::AllowRules
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
// oracle: contract policies/CONTRACT.policy.md §applicability
#[case::winget_without_architecture(winget_install())]
#[case::winget_field_and_option(with_parameters(with(winget_install(), |r| r.package.architecture = Some(Architecture::X64)), &["--architecture", "x64"]))]
#[case::winget_option_with_other_value(with_parameters(winget_install(), &["--architecture", "amd64"]))]
#[case::winget_option_sharing_its_element(with_parameters(winget_install(), &["-ea", "x64"]))]
#[case::vcpkg_other_triplet(request("Install", "Vcpkg", "wasm32-emscripten", "zlib"))]
#[case::dotnet_neutral(with(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), |r| r.package.architecture = Some(Architecture::Neutral)))]
#[case::chocolatey_x64(elevated(with(install("Chocolatey", "chocolatey", "git", "2.4"), |r| r.package.architecture = Some(Architecture::X64))))]
#[case::chocolatey_without_architecture(elevated(install("Chocolatey", "chocolatey", "git", "2.4")))]
fn unknown_architectures(#[case] subject: Request) {
    assert_eq!(
        check_selecting(r#""Architectures":["X86","X64","Arm64","Neutral"]"#, &subject),
        Check::DefaultDeny
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §request-properties
#[case::elevated_member(|r: &mut Request| r.elevation = Elevation::Elevated, "Elevated")]
#[case::machine_scope_member(|r: &mut Request| { r.scope = Some(Scope::Machine); r.elevation = Elevation::Elevated; }, "Elevated")]
#[case::non_elevated_member(|r: &mut Request| r.elevation = Elevation::NonElevated, "NonElevated")]
#[case::user_scope_member(|r: &mut Request| r.scope = Some(Scope::User), "NonElevated")]
fn elevation_is_read_from_the_member(#[case] change: fn(&mut Request), #[case] elevation: &str) {
    assert_eq!(
        check_selecting(
            &format!(r#""Elevation":"{elevation}""#),
            &with(winget_install(), change)
        ),
        Check::AllowRules
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
// oracle: contract policies/CONTRACT.policy.md §source-selector
#[case::dotnet("Dotnet", request("Uninstall", "Dotnet", "nuget.org", "dotnet-ef"))]
#[case::chocolatey("Chocolatey", elevated(request("Uninstall", "Chocolatey", "chocolatey", "git")))]
#[case::winget("Winget", request("Uninstall", "Winget", "winget", "Contoso.Tool"))]
#[case::scoop("Scoop", request("Uninstall", "Scoop", "main", "git"))]
#[case::vcpkg("Vcpkg", request("Uninstall", "Vcpkg", "x64-windows", "zlib"))]
#[case::npm("Npm", request("Uninstall", "Npm", "npm", "react"))]
fn any_source_selector_matches_the_unknown_source_of_an_uninstall(#[case] manager: &str, #[case] subject: Request) {
    assert_eq!(
        check_selecting(
            &format!(r#""Sources":[{{"Manager":"{manager}","AnySource":true}}]"#),
            &subject
        ),
        Check::AllowRules
    );
}

#[test]
fn chocolatey_default_machine_scope_needs_elevation_and_is_machine() {
    // oracle: contract policies/CONTRACT.policy.md §manager-chocolatey
    // oracle: contract policies/CONTRACT.policy.md §reading
    let subject = install("Chocolatey", "chocolatey", "git", "2.4");
    assert_eq!(
        (
            check_selecting(r#""Scopes":["Machine"]"#, &subject),
            check_selecting(r#""Scopes":["Machine"],"Elevation":"Elevated""#, &elevated(subject)),
        ),
        (Check::UnreadableRequest, Check::AllowRules)
    );
}
