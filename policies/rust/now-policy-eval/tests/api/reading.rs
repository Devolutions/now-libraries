//! Reading requests: unreadable requests, properties, opt-ins, install locations and missing versions.

use now_policy_eval::{Check, Request};
use rstest::rstest;

use crate::common::{ALLOW_ALL, check_of, evaluate_rules, request, winget_install};

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

fn parameters(mut request: Request, parameters: &[&str]) -> Request {
    request.options.custom_parameters = parameters.iter().map(|parameter| (*parameter).to_owned()).collect();
    request
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
#[case::operation_out_of_set(|r: &mut Request| r.operation = "Repair".to_owned())]
#[case::operation_in_other_case(|r: &mut Request| r.operation = "install".to_owned())]
#[case::manager_out_of_set(|r: &mut Request| r.manager = "WinGet".to_owned())]
#[case::requested_elevation_out_of_set(|r: &mut Request| r.client.requested_elevation = "Admin".to_owned())]
#[case::requested_elevation_absent(|r: &mut Request| r.client.requested_elevation = String::new())]
#[case::architecture_out_of_set(|r: &mut Request| r.package.architecture = Some("x64".to_owned()))]
#[case::scope_out_of_set(|r: &mut Request| r.options.scope = Some("Global".to_owned()))]
#[case::empty_source_name(|r: &mut Request| r.source.name = String::new())]
#[case::source_name_of_129_bytes(|r: &mut Request| r.source.name = "s".repeat(129))]
#[case::source_url_of_2049_bytes(|r: &mut Request| r.source.url = Some("u".repeat(2049)))]
#[case::empty_package_id(|r: &mut Request| r.package.id = String::new())]
#[case::package_id_of_257_characters(|r: &mut Request| r.package.id = "p".repeat(257))]
#[case::package_id_with_space(|r: &mut Request| r.package.id = "Contoso Tool".to_owned())]
#[case::package_id_with_tilde(|r: &mut Request| r.package.id = "Contoso~Tool".to_owned())]
#[case::package_id_non_ascii(|r: &mut Request| r.package.id = "Contosö".to_owned())]
#[case::empty_version(|r: &mut Request| r.package.version = Some(String::new()))]
#[case::version_of_129_bytes(|r: &mut Request| r.package.version = Some("1".repeat(129)))]
#[case::empty_channel(|r: &mut Request| r.package.channel = Some(String::new()))]
#[case::channel_of_17_bytes(|r: &mut Request| r.package.channel = Some("c".repeat(17)))]
#[case::install_location_of_2049_bytes(|r: &mut Request| r.options.custom_install_location = Some(format!("C:\\{}", "a".repeat(2046))))]
#[case::pre_operation_command_of_2049_bytes(|r: &mut Request| r.options.pre_operation_command = Some("c".repeat(2049)))]
#[case::post_operation_command_of_2049_bytes(|r: &mut Request| r.options.post_operation_command = Some("c".repeat(2049)))]
#[case::sixty_five_custom_parameters(|r: &mut Request| r.options.custom_parameters = vec!["/q".to_owned(); 65])]
#[case::custom_parameter_of_513_bytes(|r: &mut Request| r.options.custom_parameters = vec!["p".repeat(513)])]
#[case::empty_custom_parameter(|r: &mut Request| r.options.custom_parameters = vec![String::new()])]
#[case::sixty_five_processes_to_kill(|r: &mut Request| r.options.kill_before_operation = vec!["a.exe".to_owned(); 65])]
#[case::empty_process_to_kill(|r: &mut Request| r.options.kill_before_operation = vec![String::new()])]
#[case::process_to_kill_of_129_bytes(|r: &mut Request| r.options.kill_before_operation = vec!["p".repeat(129)])]
#[case::custom_parameter_with_line_feed(|r: &mut Request| r.options.custom_parameters = vec!["/a\nb".to_owned()])]
#[case::custom_parameter_with_carriage_return(|r: &mut Request| r.options.custom_parameters = vec!["/a\rb".to_owned()])]
#[case::custom_parameter_with_nul(|r: &mut Request| r.options.custom_parameters = vec!["/a\0b".to_owned()])]
#[case::source_name_with_nul(|r: &mut Request| r.source.name = "win\0get".to_owned())]
#[case::source_name_with_line_feed(|r: &mut Request| r.source.name = "win\nget".to_owned())]
#[case::source_url_through_winget(|r: &mut Request| r.source.url = Some("https://cdn.winget.microsoft.com/cache".to_owned()))]
#[case::empty_source_url_through_winget(|r: &mut Request| r.source.url = Some(String::new()))]
fn unreadable_winget_requests(#[case] change: fn(&mut Request)) {
    assert_eq!(
        check_of(GRANT_ALL, &with(winget_install(), change)),
        Check::UnreadableRequest
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
// oracle: spec Unicode 16.0.0 PropList.txt White_Space
// oracle: spec Unicode 16.0.0 DerivedCoreProperties.txt Default_Ignorable_Code_Point
#[case::leading_space(" winget")]
#[case::trailing_tab("winget\t")]
#[case::trailing_no_break_space("winget\u{A0}")]
#[case::leading_ideographic_space("\u{3000}winget")]
#[case::trailing_next_line("winget\u{85}")]
#[case::zero_width_space("win\u{200B}get")]
#[case::soft_hyphen("win\u{AD}get")]
#[case::mongolian_vowel_separator("win\u{180E}get")]
#[case::variation_selector("winget\u{FE0F}")]
#[case::tag_character("win\u{E0041}get")]
fn source_names_that_look_like_another_are_unreadable(#[case] name: &str) {
    let mut request = winget_install();
    request.source.name = name.to_owned();
    assert_eq!(check_of(GRANT_ALL, &request), Check::UnreadableRequest);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §reading
#[case::inner_space("my source")]
#[case::inner_ideographic_space("my\u{3000}source")]
#[case::non_ascii("Dépôt")]
#[case::length_128(&"s".repeat(128))]
fn distinct_source_names_are_readable(#[case] name: &str) {
    let mut request = winget_install();
    request.source.name = name.to_owned();
    assert_eq!(check_of(GRANT_ALL, &request), Check::AllowRules);
}

#[test]
fn package_id_of_256_allowed_characters_is_readable() {
    // oracle: contract policies/CONTRACT.policy.md §request-input
    let mut request = winget_install();
    let characters = "aZ09._+/:[],#$%{}-";
    request.package.id = characters.repeat(256 / characters.len() + 1)[..256].to_owned();
    assert_eq!(check_of(GRANT_ALL, &request), Check::AllowRules);
}

#[test]
fn input_bounds_at_their_maximum_are_readable() {
    // oracle: contract policies/CONTRACT.policy.md §request-input
    let mut request = winget_install();
    request.package.version = Some("1".repeat(128));
    request.package.channel = Some("c".repeat(16));
    request.options.custom_parameters = vec!["p".repeat(512); 64];
    request.options.pre_operation_command = Some("c".repeat(2048));
    request.options.post_operation_command = Some(String::new());
    request.options.kill_before_operation = vec!["k".repeat(128); 64];
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
#[case::custom_parameters_through_chocolatey(parameters(install("Chocolatey", "chocolatey", "git", "1.0"), &["--force"]))]
#[case::custom_parameters_through_powershell(parameters(install("PowerShell", "PSGallery", "Pester", "1.0"), &["-Force"]))]
#[case::custom_parameters_through_npm(parameters(install("Npm", "npm", "react", "1.0.0"), &["--force"]))]
#[case::version_through_scoop(install("Scoop", "main", "git", "1.0"))]
#[case::version_through_vcpkg(install("Vcpkg", "x64-windows", "zlib", "1.0"))]
#[case::channel_through_scoop(with(request("Install", "Scoop", "main", "git"), |r| r.package.channel = Some("beta".to_owned())))]
#[case::channel_through_bun(with(install("Bun", "Bun", "react", "1.0.0"), |r| r.package.channel = Some("beta".to_owned())))]
#[case::channel_through_cargo(with(install("Cargo", "crates.io", "serde", "1.0.0"), |r| r.package.channel = Some("beta".to_owned())))]
#[case::other_npm_source(install("Npm", "registry", "react", "1.0.0"))]
#[case::other_bun_source(install("Bun", "npm", "react", "1.0.0"))]
#[case::other_cargo_source(install("Cargo", "crates", "serde", "1.0.0"))]
#[case::other_pip_source(install("Pip", "pypi", "requests", "1.0"))]
#[case::npm_source_with_non_ascii_case(install("Npm", "\u{1E3F}pm", "react", "1.0.0"))]
#[case::source_url_through_npm(with(install("Npm", "npm", "react", "1.0.0"), |r| r.source.url = Some("https://www.npmjs.com".to_owned())))]
#[case::other_source_url_through_bun(with(install("Bun", "Bun", "react", "1.0.0"), |r| r.source.url = Some("https://registry.npmjs.org".to_owned())))]
#[case::bun_source_url_with_path(with(install("Bun", "Bun", "react", "1.0.0"), |r| r.source.url = Some("https://www.npmjs.com/x".to_owned())))]
#[case::powershell_wildcard_source(install("PowerShell", "PS*Gallery", "Pester", "1.0"))]
#[case::powershell_source_starting_with_dot(install("PowerShell", ".PSGallery", "Pester", "1.0"))]
#[case::powershell7_source_with_bang(install("PowerShell7", "PSGallery!", "Pester", "1.0"))]
#[case::powershell_non_ascii_source(install("PowerShell", "Dépôt", "Pester", "1.0"))]
fn unreadable_requests_through_managers(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::UnreadableRequest);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
#[case::npm_source_in_ascii_upper_case(install("Npm", "NPM", "react", "1.0.0"))]
#[case::bun_source_in_lower_case(install("Bun", "bun", "react", "1.0.0"))]
#[case::cargo_source_in_mixed_case(install("Cargo", "Crates.IO", "serde", "1.0.0"))]
#[case::pip_source_in_upper_case(install("Pip", "PIP", "requests", "2.31"))]
#[case::bun_npmjs_url_with_trailing_slashes(with(install("Bun", "Bun", "react", "1.0.0"), |r| r.source.url = Some("HTTPS://WWW.NPMJS.COM//".to_owned())))]
#[case::npm_channel(with(install("Npm", "npm", "react", "1.0.0"), |r| r.package.channel = Some("next".to_owned())))]
#[case::chocolatey_channel(with(install("Chocolatey", "chocolatey", "git", "2.0"), |r| r.package.channel = Some("beta".to_owned())))]
#[case::powershell_source_with_space_dot_dash_underscore(install("PowerShell", "My Repo_1.2-x", "Pester", "5.0"))]
#[case::scoop_bucket_without_version(request("Install", "Scoop", "extras", "git"))]
#[case::scoop_manifest_url(request("Install", "Scoop", "https://example.test/git.json", "git"))]
#[case::vcpkg_without_version(request("Install", "Vcpkg", "x64-windows", "zlib"))]
#[case::scoop_custom_parameters(parameters(request("Install", "Scoop", "main", "git"), &["--global"]))]
fn readable_requests_through_managers(#[case] subject: Request) {
    assert_eq!(check_of(GRANT_ALL, &subject), Check::AllowRules);
}

#[test]
fn pre_release_option_has_no_effect() {
    // oracle: contract policies/CONTRACT.policy.md §reading
    let rules = r#"[
        {"Id":"stable","Decision":"Allow","Select":{"Operations":["Install"],"Version":{"Prerelease":"Exclude"}}},
        {"Id":"deny-pre","Decision":"Deny","Select":{"Operations":["Install"],"Version":{"Minimum":"0.0.0-0","Maximum":"1.2.3-zzz"}}}
    ]"#;
    let mut request = install("Chocolatey", "chocolatey", "git", "1.2.3");
    let without = evaluate_rules(rules, &request);
    request.options.pre_release = true;
    assert_eq!(evaluate_rules(rules, &request), without);
    assert_eq!(without.check, Check::AllowRules);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §opt-ins
#[case::interactive(|r: &mut Request| r.options.interactive = true, &["Interactive"])]
#[case::skip_hash_check(|r: &mut Request| r.options.skip_hash_check = true, &["SkipHashCheck"])]
#[case::install_location(|r: &mut Request| r.options.custom_install_location = Some("C:\\Apps".to_owned()), &["CustomInstallLocation"])]
#[case::custom_parameter(|r: &mut Request| r.options.custom_parameters = vec!["/quiet".to_owned()], &["CustomParameters"])]
#[case::empty_pre_operation_command(|r: &mut Request| r.options.pre_operation_command = Some(String::new()), &["PrePostCommands"])]
#[case::post_operation_command(|r: &mut Request| r.options.post_operation_command = Some("cmd /c echo".to_owned()), &["PrePostCommands"])]
#[case::kill_before_operation(|r: &mut Request| r.options.kill_before_operation = vec!["tool.exe".to_owned()], &["KillBeforeOperation"])]
#[case::uninstall_previous(|r: &mut Request| r.options.uninstall_previous = true, &["UninstallPrevious"])]
#[case::no_upgrade(|r: &mut Request| r.options.no_upgrade = true, &["NoUpgrade"])]
#[case::empty_kill_list(|r: &mut Request| r.options.kill_before_operation = Vec::new(), &[])]
#[case::every_option_in_table_order(
    |r: &mut Request| {
        r.options.no_upgrade = true;
        r.options.uninstall_previous = true;
        r.options.kill_before_operation = vec!["tool.exe".to_owned()];
        r.options.pre_operation_command = Some("cmd".to_owned());
        r.options.custom_parameters = vec!["/quiet".to_owned()];
        r.options.custom_install_location = Some("C:\\Apps".to_owned());
        r.options.skip_hash_check = true;
        r.options.interactive = true;
    },
    &["Interactive", "SkipHashCheck", "CustomInstallLocation", "CustomParameters", "PrePostCommands", "KillBeforeOperation", "UninstallPrevious", "NoUpgrade"]
)]
fn winget_options_carry_opt_ins(#[case] change: fn(&mut Request), #[case] expected: &[&str]) {
    assert_eq!(opt_ins(&with(winget_install(), change)), expected);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §manager-winget
#[case::plain_parameter(&["/quiet"], &["CustomParameters"])]
#[case::override_with_value(&["--override", "/quiet"], &["CustomParameters", "InstallerArguments"])]
#[case::custom_with_equals(&["--custom=/quiet"], &["CustomParameters", "InstallerArguments"])]
#[case::override_without_value(&["--override"], &["CustomParameters", "InstallerArguments"])]
#[case::interactive_long(&["--interactive"], &["Interactive", "CustomParameters"])]
#[case::interactive_long_ignoring_ascii_case(&["--INTERACTIVE"], &["Interactive", "CustomParameters"])]
#[case::interactive_short(&["-i"], &["Interactive", "CustomParameters"])]
#[case::interactive_in_short_cluster(&["-ei"], &["Interactive", "CustomParameters"])]
#[case::short_option_in_other_case(&["-I"], &["CustomParameters"])]
#[case::short_options_end_at_equals(&["-e=i"], &["CustomParameters"])]
#[case::ignore_security_hash(&["--ignore-security-hash"], &["SkipHashCheck", "CustomParameters"])]
#[case::force(&["--force"], &["SkipHashCheck", "CustomParameters"])]
#[case::no_upgrade(&["--no-upgrade"], &["CustomParameters", "NoUpgrade"])]
#[case::uninstall_previous(&["--uninstall-previous"], &["CustomParameters", "UninstallPrevious"])]
#[case::long_option_name_ends_at_equals(&["--interactive=false"], &["Interactive", "CustomParameters"])]
#[case::option_after_end_of_options(&["--", "--interactive"], &["CustomParameters"])]
#[case::value_is_not_read_as_an_option(&["--override", "--interactive"], &["CustomParameters", "InstallerArguments"])]
#[case::lone_dash_is_not_an_option(&["-", "x"], &["CustomParameters"])]
#[case::every_option_in_table_order(&["--no-upgrade", "--uninstall-previous", "--override", "/q", "--force", "-i"], &["Interactive", "SkipHashCheck", "CustomParameters", "InstallerArguments", "UninstallPrevious", "NoUpgrade"])]
fn winget_custom_parameters_carry_opt_ins(#[case] custom_parameters: &[&str], #[case] expected: &[&str]) {
    assert_eq!(opt_ins(&parameters(winget_install(), custom_parameters)), expected);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §manager-scoop
#[case::skip_hash_check_long(&["--skip-hash-check"], &["SkipHashCheck", "CustomParameters"])]
#[case::skip_hash_check_short(&["-s"], &["SkipHashCheck", "CustomParameters"])]
#[case::skip_hash_check_in_cluster(&["-ks"], &["SkipHashCheck", "CustomParameters"])]
#[case::double_dash_is_not_a_cluster(&["--s"], &["CustomParameters"])]
#[case::other_option(&["--global"], &["CustomParameters"])]
fn scoop_custom_parameters_carry_opt_ins(#[case] custom_parameters: &[&str], #[case] expected: &[&str]) {
    let request = parameters(request("Install", "Scoop", "main", "git"), custom_parameters);
    assert_eq!(opt_ins(&request), expected);
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
    let mut request = winget_install();
    request.options.custom_install_location = Some(location.to_owned());
    assert_eq!(check_of(GRANT_LOCATIONS, &request), Check::AllowRules);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §install-location
#[case::empty("")]
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
    let mut request = winget_install();
    request.options.custom_install_location = Some(location.to_owned());
    assert_eq!(check_of(GRANT_LOCATIONS, &request), Check::InstallLocation);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §manager-winget
#[case::long_option_with_value(&["--location", "C:\\Apps"], Check::AllowRules)]
#[case::short_option_with_value(&["-l", "C:\\Apps"], Check::AllowRules)]
#[case::long_option_ignoring_ascii_case_with_equals(&["--LOCATION=C:\\Apps"], Check::AllowRules)]
#[case::quoted_value_after_equals(&["--location=\"C:\\Program Files\\Contoso\""], Check::AllowRules)]
#[case::short_option_with_equals(&["-l=C:\\Apps"], Check::AllowRules)]
#[case::option_without_value(&["--location"], Check::InstallLocation)]
#[case::short_option_sharing_its_element(&["-il", "C:\\Apps"], Check::InstallLocation)]
#[case::option_twice_with_one_location(&["--location", "C:\\Apps", "-l", "C:\\Apps"], Check::InstallLocation)]
#[case::relative_value(&["--location", "Apps"], Check::InstallLocation)]
fn winget_location_options(#[case] custom_parameters: &[&str], #[case] expected: Check) {
    assert_eq!(
        check_of(GRANT_ALL, &parameters(winget_install(), custom_parameters)),
        expected
    );
}

#[test]
fn location_set_by_the_field_and_an_option_fails_even_when_equal() {
    // oracle: contract policies/CONTRACT.policy.md §install-location
    let mut request = parameters(winget_install(), &["--location", "C:\\Apps"]);
    request.options.custom_install_location = Some("C:\\Apps".to_owned());
    assert_eq!(check_of(GRANT_LOCATIONS, &request), Check::InstallLocation);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §missing-version
#[case::winget_install_without_version(request("Install", "Winget", "winget", "Contoso.Tool"), Check::MissingVersion)]
#[case::winget_update_without_version(request("Update", "Winget", "winget", "Contoso.Tool"), Check::MissingVersion)]
#[case::winget_uninstall_without_version(request("Uninstall", "Winget", "winget", "Contoso.Tool"), Check::AllowRules)]
#[case::winget_version_that_is_not_a_version(install("Winget", "winget", "Contoso.Tool", "latest"), Check::AllowRules)]
#[case::identifier_selects_the_version(
    install("Winget", "winget", "Contoso.Tool@1.2.3", "1.2.3"),
    Check::MissingVersion
)]
#[case::leading_at_selects_no_version(install("Winget", "winget", "@Contoso", "1.2.3"), Check::AllowRules)]
#[case::version_option(parameters(winget_install(), &["--version", "1.2.3"]), Check::MissingVersion)]
#[case::short_version_option_without_value(parameters(winget_install(), &["-v"]), Check::MissingVersion)]
#[case::npm_two_components(install("Npm", "npm", "react", "18.2"), Check::MissingVersion)]
#[case::npm_range(install("Npm", "npm", "react", "^18.2.0"), Check::MissingVersion)]
#[case::npm_three_components(install("Npm", "npm", "react", "18.2.0"), Check::AllowRules)]
#[case::npm_pre_release(install("Npm", "npm", "react", "19.0.0-rc.1"), Check::AllowRules)]
#[case::npm_alias_selects_the_version(install("Npm", "npm", "my-react:react@18.2.0", "18.2.0"), Check::MissingVersion)]
#[case::bun_two_components(install("Bun", "Bun", "react", "18.2"), Check::MissingVersion)]
#[case::cargo_one_component(install("Cargo", "crates.io", "serde", "1"), Check::MissingVersion)]
#[case::cargo_three_components(install("Cargo", "crates.io", "serde", "1.0.0"), Check::AllowRules)]
#[case::dotnet_two_components(install("Dotnet", "nuget.org", "dotnet-ef", "8.0"), Check::MissingVersion)]
#[case::dotnet_four_components(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0.1"), Check::AllowRules)]
#[case::chocolatey_two_components(install("Chocolatey", "chocolatey", "git", "2.4"), Check::AllowRules)]
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
#[case::winget_field(with(winget_install(), |r| r.options.scope = Some("Machine".to_owned())), "Machine")]
#[case::winget_option(parameters(winget_install(), &["--scope", "machine"]), "Machine")]
#[case::winget_option_ignoring_ascii_case(parameters(winget_install(), &["--scope=USER"]), "User")]
#[case::winget_option_after_end_of_options(parameters(with(winget_install(), |r| r.options.scope = Some("User".to_owned())), &["--", "--scope", "machine"]), "User")]
#[case::default_user(request("Uninstall", "Npm", "npm", "react"), "User")]
#[case::chocolatey_default_machine(request("Uninstall", "Chocolatey", "chocolatey", "git"), "Machine")]
#[case::chocolatey_field(with(request("Uninstall", "Chocolatey", "chocolatey", "git"), |r| r.options.scope = Some("User".to_owned())), "User")]
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
#[case::winget_field_and_option(parameters(with(winget_install(), |r| r.options.scope = Some("Machine".to_owned())), &["--scope", "machine"]))]
#[case::winget_two_options(parameters(winget_install(), &["--scope", "user", "--scope", "user"]))]
#[case::winget_option_with_other_value(parameters(winget_install(), &["--scope", "global"]))]
#[case::winget_option_without_value(parameters(winget_install(), &["--scope"]))]
#[case::pip_uninstall(with(request("Uninstall", "Pip", "pip", "requests"), |r| r.options.scope = Some("User".to_owned())))]
#[case::powershell_uninstall(request("Uninstall", "PowerShell", "PSGallery", "Pester"))]
#[case::powershell7_uninstall(request("Uninstall", "PowerShell7", "PSGallery", "Pester"))]
fn unknown_scopes(#[case] subject: Request) {
    assert_eq!(
        check_selecting(r#""Scopes":["User","Machine"]"#, &subject),
        Check::DefaultDeny
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §managers
#[case::winget_field(with(winget_install(), |r| r.package.architecture = Some("Arm64".to_owned())), "Arm64")]
#[case::winget_option(parameters(winget_install(), &["-a", "X86"]), "X86")]
#[case::scoop_field(with(request("Install", "Scoop", "main", "git"), |r| r.package.architecture = Some("X64".to_owned())), "X64")]
#[case::vcpkg_triplet(request("Install", "Vcpkg", "x64-windows", "zlib"), "X64")]
#[case::vcpkg_triplet_ignoring_ascii_case(request("Install", "Vcpkg", "ARM64-Linux", "zlib"), "Arm64")]
#[case::vcpkg_field_over_triplet(with(request("Install", "Vcpkg", "x64-windows", "zlib"), |r| r.package.architecture = Some("X86".to_owned())), "X86")]
#[case::npm_default(install("Npm", "npm", "react", "18.2.0"), "Neutral")]
#[case::bun_default(install("Bun", "Bun", "react", "18.2.0"), "Neutral")]
#[case::cargo_default(install("Cargo", "crates.io", "serde", "1.0.0"), "Neutral")]
#[case::pip_default(install("Pip", "pip", "requests", "2.31"), "Neutral")]
#[case::dotnet_field(with(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), |r| r.package.architecture = Some("X64".to_owned())), "X64")]
#[case::chocolatey_field(with(install("Chocolatey", "chocolatey", "git", "2.4"), |r| r.package.architecture = Some("X86".to_owned())), "X86")]
#[case::powershell_whatever_the_field(with(install("PowerShell", "PSGallery", "Pester", "5.0"), |r| r.package.architecture = Some("X64".to_owned())), "Neutral")]
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
#[case::winget_field_and_option(parameters(with(winget_install(), |r| r.package.architecture = Some("X64".to_owned())), &["--architecture", "x64"]))]
#[case::winget_option_with_other_value(parameters(winget_install(), &["--architecture", "amd64"]))]
#[case::winget_option_sharing_its_element(parameters(winget_install(), &["-ia", "x64"]))]
#[case::scoop_arch_option(parameters(with(request("Install", "Scoop", "main", "git"), |r| r.package.architecture = Some("X64".to_owned())), &["--arch", "64bit"]))]
#[case::scoop_short_cluster(parameters(with(request("Install", "Scoop", "main", "git"), |r| r.package.architecture = Some("X64".to_owned())), &["-ga"]))]
#[case::vcpkg_other_triplet(request("Install", "Vcpkg", "wasm32-emscripten", "zlib"))]
#[case::dotnet_neutral(with(install("Dotnet", "nuget.org", "dotnet-ef", "8.0.0"), |r| r.package.architecture = Some("Neutral".to_owned())))]
#[case::chocolatey_x64(with(install("Chocolatey", "chocolatey", "git", "2.4"), |r| r.package.architecture = Some("X64".to_owned())))]
#[case::chocolatey_without_architecture(install("Chocolatey", "chocolatey", "git", "2.4"))]
fn unknown_architectures(#[case] subject: Request) {
    assert_eq!(
        check_selecting(r#""Architectures":["X86","X64","Arm64","Neutral"]"#, &subject),
        Check::DefaultDeny
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §request-properties
#[case::machine_scope_field(|r: &mut Request| r.options.scope = Some("Machine".to_owned()), "Elevated")]
#[case::elevated_client(|r: &mut Request| r.client.requested_elevation = "Elevated".to_owned(), "Elevated")]
#[case::user_scope_standard_client(|r: &mut Request| r.options.scope = Some("User".to_owned()), "NonElevated")]
#[case::machine_scope_option_only(|r: &mut Request| r.options.custom_parameters = vec!["--scope".to_owned(), "machine".to_owned()], "NonElevated")]
fn elevation_is_read(#[case] change: fn(&mut Request), #[case] elevation: &str) {
    assert_eq!(
        check_selecting(
            &format!(r#""Elevation":"{elevation}""#),
            &with(winget_install(), change)
        ),
        Check::AllowRules
    );
}

#[test]
fn chocolatey_default_machine_scope_does_not_elevate() {
    // oracle: contract policies/CONTRACT.policy.md §request-properties
    let request = install("Chocolatey", "chocolatey", "git", "2.4");
    assert_eq!(
        check_selecting(r#""Scopes":["Machine"],"Elevation":"NonElevated""#, &request),
        Check::AllowRules
    );
}
