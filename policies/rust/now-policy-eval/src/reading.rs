//! Reading a request the way the broker executes it ([§reading], [§managers]).
//!
//! This module is the only place that knows how the broker builds commands, so a change to a command changes only this module.
//!
//! [§reading]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#reading
//! [§managers]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#managers

use crate::request::Request;
use crate::text::{is_default_ignorable, is_white_space};
use crate::values::{Architecture, Elevation, Manager, Operation, OptIn, OptInSet, RequestedElevation, Scope};
use crate::version::Version;

/// The properties and opt-ins of a readable request ([§request-properties]).
///
/// `None` stands for an unknown property.
///
/// [§request-properties]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#request-properties
#[derive(Debug, Clone)]
pub(crate) struct ReadRequest {
    pub(crate) operation: Operation,
    pub(crate) manager: Manager,
    pub(crate) source: Option<String>,
    /// The package names in their manager's canonical form.
    pub(crate) package_names: Option<Vec<String>>,
    /// The concrete version.
    pub(crate) version: Option<String>,
    pub(crate) missing_version: bool,
    pub(crate) scope: Option<Scope>,
    pub(crate) architecture: Option<Architecture>,
    pub(crate) elevation: Elevation,
    pub(crate) opt_ins: OptInSet,
    pub(crate) custom_parameters: Vec<String>,
    pub(crate) install_location: InstallLocation,
}

/// The outcome of the install location check ([§install-location]).
///
/// [§install-location]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#install-location
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum InstallLocation {
    /// The request does not use `CustomInstallLocation`.
    NotUsed,
    /// The request carries exactly one location, a plain local path, normalized.
    Passes(String),
    /// The request fails the install location check.
    Fails,
}

/// The request is unreadable ([§reading]).
///
/// [§reading]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#reading
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct Unreadable;

/// Reads `request` the way the broker executes it.
pub(crate) fn read(request: &Request) -> Result<ReadRequest, Unreadable> {
    let operation = Operation::from_name(&request.operation).ok_or(Unreadable)?;
    let manager = Manager::from_name(&request.manager).ok_or(Unreadable)?;
    let requested_elevation = RequestedElevation::from_name(&request.client.requested_elevation).ok_or(Unreadable)?;
    let requested_architecture = optional(request.package.architecture.as_deref(), Architecture::from_name)?;
    let requested_scope = optional(request.options.scope.as_deref(), Scope::from_name)?;

    check_input_bounds(request)?;
    check_manager_rules(request, operation, manager)?;

    let options = &request.options;
    let parameters = match manager {
        Manager::Winget => read_winget_parameters(&options.custom_parameters),
        Manager::Scoop => read_scoop_parameters(&options.custom_parameters),
        _ => ParameterEffects::default(),
    };

    let identity = read_identity(manager, &request.package.id);

    let source = if identity.unknown_source {
        None
    } else {
        Some(request.source.name.clone())
    };

    let version = concrete_version(manager, request.package.version.as_deref(), &identity, &parameters);
    let missing_version = matches!(operation, Operation::Install | Operation::Update)
        && !matches!(manager, Manager::Scoop | Manager::Vcpkg)
        && version.is_none();

    let scope = read_scope(manager, operation, requested_scope, &parameters);
    let architecture = read_architecture(manager, requested_architecture, &request.source.name, &parameters);

    let elevation = if requested_scope == Some(Scope::Machine) || requested_elevation == RequestedElevation::Elevated {
        Elevation::Elevated
    } else {
        Elevation::NonElevated
    };

    // An `Options` field carries its opt-in under every manager, even one whose command does not carry the field.
    let mut opt_ins = parameters.opt_ins;
    for (opt_in, field_carries) in [
        (OptIn::Interactive, options.interactive),
        (OptIn::SkipHashCheck, options.skip_hash_check),
        (OptIn::CustomInstallLocation, options.custom_install_location.is_some()),
        (
            OptIn::PrePostCommands,
            options.pre_operation_command.is_some() || options.post_operation_command.is_some(),
        ),
        (OptIn::KillBeforeOperation, !options.kill_before_operation.is_empty()),
        (OptIn::UninstallPrevious, options.uninstall_previous),
        (OptIn::NoUpgrade, options.no_upgrade),
    ] {
        if field_carries {
            opt_ins.insert(opt_in);
        }
    }
    if !options.custom_parameters.is_empty() {
        opt_ins.insert(OptIn::CustomParameters);
    }

    let mut locations: Vec<Option<&str>> = Vec::new();
    if let Some(location) = &options.custom_install_location {
        locations.push(Some(location));
    }
    locations.extend(parameters.locations.iter().map(Option::as_deref));
    let install_location = match locations.as_slice() {
        [] => InstallLocation::NotUsed,
        [Some(location)] => {
            normalize_plain_local_path(location).map_or(InstallLocation::Fails, InstallLocation::Passes)
        }
        _ => InstallLocation::Fails,
    };

    Ok(ReadRequest {
        operation,
        manager,
        source,
        package_names: identity.names,
        version,
        missing_version,
        scope,
        architecture,
        elevation,
        opt_ins,
        custom_parameters: options.custom_parameters.clone(),
        install_location,
    })
}

fn optional<T>(value: Option<&str>, from_name: fn(&str) -> Option<T>) -> Result<Option<T>, Unreadable> {
    value.map(|name| from_name(name).ok_or(Unreadable)).transpose()
}

/// Checks the lengths and character sets of [§request-input] and the characters the broker cannot pass.
///
/// [§request-input]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#request-input
fn check_input_bounds(request: &Request) -> Result<(), Unreadable> {
    let options = &request.options;

    let within = |text: &str, min: usize, max: usize| min <= text.len() && text.len() <= max;
    let optional_within =
        |text: &Option<String>, min: usize, max: usize| text.as_deref().is_none_or(|text| within(text, min, max));

    let package_id_valid = within(&request.package.id, 1, 256)
        && request.package.id.bytes().all(|b| {
            b.is_ascii_alphanumeric()
                || matches!(
                    b,
                    b'.' | b'_'
                        | b'+'
                        | b'@'
                        | b'/'
                        | b':'
                        | b'['
                        | b']'
                        | b','
                        | b'#'
                        | b'$'
                        | b'%'
                        | b'{'
                        | b'}'
                        | b'-'
                )
        });

    let valid = within(&request.source.name, 1, 128)
        && optional_within(&request.source.url, 0, 2048)
        && package_id_valid
        && optional_within(&request.package.version, 1, 128)
        && optional_within(&request.package.channel, 1, 16)
        && optional_within(&options.custom_install_location, 0, 2048)
        && options.custom_parameters.len() <= 64
        && options
            .custom_parameters
            .iter()
            .all(|parameter| within(parameter, 1, 512))
        && optional_within(&options.pre_operation_command, 0, 2048)
        && optional_within(&options.post_operation_command, 0, 2048)
        && options.kill_before_operation.len() <= 64
        && options
            .kill_before_operation
            .iter()
            .all(|process| within(process, 1, 128));

    let passable = |text: &str| !text.contains(['\0', '\n', '\r']);
    let source_name = &request.source.name;
    let source_name_distinct = !source_name.starts_with(is_white_space)
        && !source_name.ends_with(is_white_space)
        && !source_name.chars().any(is_default_ignorable);

    if valid
        && passable(source_name)
        && passable(&request.package.id)
        && options.custom_parameters.iter().all(|parameter| passable(parameter))
        && source_name_distinct
    {
        Ok(())
    } else {
        Err(Unreadable)
    }
}

/// Checks the rules of [§managers] and each manager's reading that make a request unreadable.
///
/// [§managers]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#managers
fn check_manager_rules(request: &Request, operation: Operation, manager: Manager) -> Result<(), Unreadable> {
    let source_name = request.source.name.as_str();
    let has_channel = request.package.channel.is_some();

    let builds_command = matches!(
        manager,
        Manager::Winget
            | Manager::PowerShell
            | Manager::PowerShell7
            | Manager::Bun
            | Manager::Cargo
            | Manager::Chocolatey
            | Manager::Dotnet
            | Manager::Npm
            | Manager::Pip
            | Manager::Scoop
            | Manager::Vcpkg
    );

    let accepts_custom_parameters = matches!(manager, Manager::Winget | Manager::Scoop);

    let source_url_allowed = match (&request.source.url, manager) {
        (None, _) => true,
        (Some(_), Manager::Winget | Manager::Scoop) => true,
        (Some(url), Manager::Bun) => url.trim_end_matches('/').eq_ignore_ascii_case("https://www.npmjs.com"),
        (Some(url), Manager::Cargo) => url.trim_end_matches('/').eq_ignore_ascii_case("https://index.crates.io"),
        (Some(_), Manager::Dotnet) if operation == Operation::Uninstall => true,
        (Some(url), Manager::Dotnet) => {
            url.eq_ignore_ascii_case("https://api.nuget.org/v3/index.json") || url.chars().all(is_white_space)
        }
        (Some(_), _) => false,
    };

    let pins_version = !matches!(manager, Manager::Scoop | Manager::Vcpkg);

    let manager_reading_allows = match manager {
        Manager::Npm => source_name.eq_ignore_ascii_case("npm"),
        Manager::Bun => source_name.eq_ignore_ascii_case("Bun") && !has_channel,
        Manager::Cargo => source_name.eq_ignore_ascii_case("crates.io") && !has_channel,
        Manager::Pip => source_name.eq_ignore_ascii_case("pip"),
        Manager::Scoop => !has_channel,
        Manager::PowerShell | Manager::PowerShell7 => is_powershell_source_name(source_name),
        _ => true,
    };

    if builds_command
        && (accepts_custom_parameters || request.options.custom_parameters.is_empty())
        && source_url_allowed
        && (pins_version || request.package.version.is_none())
        && manager_reading_allows
    {
        Ok(())
    } else {
        Err(Unreadable)
    }
}

/// Returns whether PowerShell reads `name` as a plain source name ([§manager-powershell]).
///
/// [§manager-powershell]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-powershell
fn is_powershell_source_name(name: &str) -> bool {
    name.bytes().next().is_some_and(|first| first.is_ascii_alphanumeric())
        && name
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'-' | b'_' | b' '))
}

/// What a request's custom parameters do, according to its manager's reading.
#[derive(Debug, Default)]
struct ParameterEffects {
    opt_ins: OptInSet,
    /// Each install location an option sets, `None` when the option takes no value.
    locations: Vec<Option<String>>,
    /// Each scope an option sets, `None` when it sets no valid scope.
    scopes: Vec<Option<Scope>>,
    /// Each architecture an option sets, `None` when it sets no valid architecture.
    architectures: Vec<Option<Architecture>>,
    architecture_unknown: bool,
    selects_version: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum WingetOption {
    Location,
    Scope,
    Architecture,
    Interactive,
    InstallerArguments,
    SkipHashCheck,
    NoUpgrade,
    UninstallPrevious,
    Version,
}

impl WingetOption {
    fn from_long(name: &str) -> Option<Self> {
        const LONG: &[(&str, WingetOption)] = &[
            ("location", WingetOption::Location),
            ("scope", WingetOption::Scope),
            ("architecture", WingetOption::Architecture),
            ("interactive", WingetOption::Interactive),
            ("override", WingetOption::InstallerArguments),
            ("custom", WingetOption::InstallerArguments),
            ("ignore-security-hash", WingetOption::SkipHashCheck),
            ("force", WingetOption::SkipHashCheck),
            ("no-upgrade", WingetOption::NoUpgrade),
            ("uninstall-previous", WingetOption::UninstallPrevious),
            ("version", WingetOption::Version),
        ];

        LONG.iter()
            .find(|(long, _)| long.eq_ignore_ascii_case(name))
            .map(|(_, option)| *option)
    }

    fn from_short(name: char) -> Option<Self> {
        match name {
            'l' => Some(Self::Location),
            'a' => Some(Self::Architecture),
            'i' => Some(Self::Interactive),
            'v' => Some(Self::Version),
            _ => None,
        }
    }

    fn takes_value(self) -> bool {
        matches!(
            self,
            Self::Location | Self::Scope | Self::Architecture | Self::InstallerArguments | Self::Version
        )
    }
}

/// Reads WinGet custom parameters ([§manager-winget]).
///
/// [§manager-winget]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-winget
fn read_winget_parameters(parameters: &[String]) -> ParameterEffects {
    let mut effects = ParameterEffects::default();
    let mut idx = 0;

    while idx < parameters.len() {
        let element = parameters[idx].as_str();
        idx += 1;

        if element == "--" {
            break;
        }

        let names = element.split('=').next().unwrap_or_default();
        let (options, takes_value): (Vec<WingetOption>, bool) = if let Some(long) = names.strip_prefix("--") {
            (WingetOption::from_long(long).into_iter().collect(), true)
        } else if let Some(shorts) = names.strip_prefix('-').filter(|_| 1 < element.len()) {
            (
                shorts.chars().filter_map(WingetOption::from_short).collect(),
                shorts.chars().count() == 1,
            )
        } else {
            continue;
        };

        for option in options {
            let value = if option.takes_value() && takes_value {
                match element.split_once('=') {
                    Some((_, value)) => Some(strip_quotes(value).to_owned()),
                    None => {
                        let next = parameters.get(idx).cloned();
                        if next.is_some() {
                            idx += 1;
                        }
                        next
                    }
                }
            } else {
                None
            };

            match option {
                WingetOption::Location => effects.locations.push(value),
                WingetOption::Scope => effects.scopes.push(value.and_then(|value| {
                    Scope::ALL
                        .iter()
                        .copied()
                        .find(|scope| scope.as_str().eq_ignore_ascii_case(&value))
                })),
                WingetOption::Architecture => effects.architectures.push(value.and_then(|value| {
                    Architecture::ALL
                        .iter()
                        .copied()
                        .find(|architecture| architecture.as_str().eq_ignore_ascii_case(&value))
                })),
                WingetOption::Interactive => effects.opt_ins.insert(OptIn::Interactive),
                WingetOption::InstallerArguments => effects.opt_ins.insert(OptIn::InstallerArguments),
                WingetOption::SkipHashCheck => effects.opt_ins.insert(OptIn::SkipHashCheck),
                WingetOption::NoUpgrade => effects.opt_ins.insert(OptIn::NoUpgrade),
                WingetOption::UninstallPrevious => effects.opt_ins.insert(OptIn::UninstallPrevious),
                WingetOption::Version => effects.selects_version = true,
            }

            if option == WingetOption::Location {
                effects.opt_ins.insert(OptIn::CustomInstallLocation);
            }
        }
    }

    return effects;

    fn strip_quotes(text: &str) -> &str {
        if 2 <= text.len() && text.starts_with('"') && text.ends_with('"') {
            &text[1..text.len() - 1]
        } else {
            text
        }
    }
}

/// Reads Scoop custom parameters ([§manager-scoop]).
///
/// [§manager-scoop]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-scoop
fn read_scoop_parameters(parameters: &[String]) -> ParameterEffects {
    let mut effects = ParameterEffects::default();

    for element in parameters {
        // An option name ends at the element's first `=` and matches ignoring ASCII case.
        let name = element.split('=').next().unwrap_or_default().to_ascii_lowercase();
        let short_cluster = name
            .strip_prefix('-')
            .and_then(|rest| rest.chars().next())
            .is_some_and(|second| second != '-');

        if name == "--skip-hash-check" || (short_cluster && name.contains('s')) {
            effects.opt_ins.insert(OptIn::SkipHashCheck);
        }

        if name == "--arch" || (short_cluster && name.contains('a')) {
            effects.architecture_unknown = true;
        }

        // An element not starting with `-` that contains `@` makes the version unknown, which a Scoop version always is.
    }

    effects
}

/// The package names an identifier names, and what it leaves unknown ([§package-identity]).
///
/// [§package-identity]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#package-identity
struct Identity {
    /// The names in canonical form, or `None` when the identity is unknown.
    names: Option<Vec<String>>,
    selects_version: bool,
    unknown_source: bool,
}

fn read_identity(manager: Manager, id: &str) -> Identity {
    let known = |names: Vec<&str>, selects_version: bool| Identity {
        names: Some(
            names
                .into_iter()
                .map(|name| canonical_package_name(manager, name))
                .collect(),
        ),
        selects_version,
        unknown_source: false,
    };

    match manager {
        Manager::Npm => {
            if is_npm_registry_name(id) {
                return known(vec![id], false);
            }
            if let Some((alias, target)) = id.split_once(':') {
                let (target, selects_version) = match target.rfind('@') {
                    Some(at) if 0 < at => (&target[..at], true),
                    _ => (target, false),
                };
                if is_npm_registry_name(alias) && is_npm_registry_name(target) {
                    return known(vec![alias, target], selects_version);
                }
            }
            unknown_npm_identity(id)
        }
        Manager::Bun => {
            if is_npm_registry_name(id) {
                known(vec![id], false)
            } else {
                unknown_npm_identity(id)
            }
        }
        Manager::Vcpkg => known(vec![vcpkg_port(id)], false),
        _ => match id.rfind('@') {
            Some(at) if 0 < at => known(vec![&id[..at]], true),
            _ => known(vec![id], false),
        },
    }
}

/// The identity of an Npm or Bun identifier that is neither a registry name nor an alias of registry names.
///
/// UNTESTED: whether such an identifier selects the version is not stated by the contract, which only makes its identity and source unknown.
/// This reading applies the default `name@version` rule, so that `name@1.2.3` is missing its version rather than checked against a version the command does not install.
/// When changing it, check the missing version check for such identifiers.
fn unknown_npm_identity(id: &str) -> Identity {
    Identity {
        names: None,
        selects_version: id.rfind('@').is_some_and(|at| 0 < at),
        unknown_source: true,
    }
}

/// Returns the port a Vcpkg identifier `port[features]:triplet` names ([§manager-vcpkg]).
///
/// UNTESTED: the contract shows only the full form, and this reading takes `[features]` and `:triplet` as optional, so `zlib:x64-windows` and `zlib[core]` name `zlib`.
/// When changing it, check the package identity of Vcpkg identifiers without features or a triplet.
///
/// [§manager-vcpkg]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-vcpkg
fn vcpkg_port(id: &str) -> &str {
    let end = id.find(['[', ':']).unwrap_or(id.len());
    &id[..end]
}

/// Returns whether `name` is an npm registry name ([§manager-npm]).
///
/// [§manager-npm]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-npm
fn is_npm_registry_name(name: &str) -> bool {
    let is_part = |part: &str| {
        part.bytes()
            .next()
            .is_some_and(|first| !matches!(first, b'-' | b'.' | b'_'))
            && part
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'-' | b'.' | b'_' | b'~'))
    };

    let lowercase = name.to_ascii_lowercase();
    let shape = match name.strip_prefix('@') {
        Some(scoped) => scoped
            .split_once('/')
            .is_some_and(|(scope, name)| is_part(scope) && is_part(name)),
        None => is_part(name),
    };

    shape
        && name.len() <= 214
        && !lowercase.ends_with(".tgz")
        && !lowercase.ends_with(".tar")
        && !lowercase.ends_with(".tar.gz")
}

/// Puts a package name, or the characters of a pattern between `*`, in `manager`'s canonical form ([§package-identity]).
///
/// [§package-identity]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#package-identity
pub(crate) fn canonical_package_name(manager: Manager, name: &str) -> String {
    match manager {
        Manager::Npm | Manager::Bun => name.to_owned(),
        Manager::Cargo => name.to_ascii_lowercase().replace('_', "-"),
        Manager::Pip => {
            let mut canonical = String::with_capacity(name.len());
            for c in name.chars() {
                if matches!(c, '-' | '_' | '.') {
                    // Every `-` already in the canonical form ends a run, so a run continues when the form ends with one.
                    if !canonical.ends_with('-') {
                        canonical.push('-');
                    }
                } else {
                    canonical.push(c.to_ascii_lowercase());
                }
            }
            canonical
        }
        _ => name.to_ascii_lowercase(),
    }
}

/// Returns the concrete version, or `None` when the version is unknown ([§missing-version]).
///
/// [§missing-version]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#missing-version
fn concrete_version(
    manager: Manager,
    version: Option<&str>,
    identity: &Identity,
    parameters: &ParameterEffects,
) -> Option<String> {
    let version = version?;

    if identity.selects_version || parameters.selects_version {
        return None;
    }

    let resolved_exactly = match manager {
        Manager::Scoop | Manager::Vcpkg => false,
        Manager::Npm | Manager::Bun | Manager::Cargo | Manager::Dotnet => {
            Version::parse(version).is_some_and(|version| 3 <= version.release_count())
        }
        Manager::PowerShell7 => powershell7_resolves_exactly(version),
        _ => true,
    };

    resolved_exactly.then(|| version.to_owned())
}

/// Returns whether PowerShell7 installs exactly `version` rather than resolving it.
///
/// UNTESTED: the contract says that `PowerShell7` needs a version, and this reading takes it as requiring a version of the contract's version scheme, so a range such as `[1.0,2.0]` is not concrete.
/// When changing it, check the missing version check for PowerShell7 requests.
fn powershell7_resolves_exactly(version: &str) -> bool {
    Version::parse(version).is_some()
}

/// Returns the scope, or `None` when it is unknown.
fn read_scope(
    manager: Manager,
    operation: Operation,
    requested: Option<Scope>,
    parameters: &ParameterEffects,
) -> Option<Scope> {
    match manager {
        Manager::Winget => {
            let mut settings = requested.map(Some).into_iter().chain(parameters.scopes.iter().copied());
            match (settings.next(), settings.next()) {
                (Some(scope), None) => scope,
                _ => None,
            }
        }
        Manager::Pip | Manager::PowerShell | Manager::PowerShell7 if operation == Operation::Uninstall => None,
        Manager::Chocolatey => Some(requested.unwrap_or(Scope::Machine)),
        _ => Some(requested.unwrap_or(Scope::User)),
    }
}

/// Returns the architecture, or `None` when it is unknown.
fn read_architecture(
    manager: Manager,
    requested: Option<Architecture>,
    source_name: &str,
    parameters: &ParameterEffects,
) -> Option<Architecture> {
    match manager {
        Manager::Winget => {
            let mut settings = requested
                .map(Some)
                .into_iter()
                .chain(parameters.architectures.iter().copied());
            match (settings.next(), settings.next()) {
                (Some(architecture), None) => architecture,
                _ => None,
            }
        }
        Manager::Scoop if parameters.architecture_unknown => None,
        Manager::Vcpkg => requested.or_else(|| {
            let triplet = source_name.to_ascii_lowercase();
            [
                ("x86-", Architecture::X86),
                ("x64-", Architecture::X64),
                ("arm64-", Architecture::Arm64),
            ]
            .into_iter()
            .find(|(prefix, _)| triplet.starts_with(prefix))
            .map(|(_, architecture)| architecture)
        }),
        Manager::Npm | Manager::Bun | Manager::Cargo | Manager::Pip => Some(requested.unwrap_or(Architecture::Neutral)),
        Manager::Dotnet if requested == Some(Architecture::Neutral) => None,
        Manager::Chocolatey if requested == Some(Architecture::X64) => None,
        Manager::PowerShell | Manager::PowerShell7 => Some(Architecture::Neutral),
        _ => requested,
    }
}

/// Returns the normalized form of `text` when it is a plain local path ([§install-location]).
///
/// [§install-location]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#install-location
fn normalize_plain_local_path(text: &str) -> Option<String> {
    let mut chars = text.chars();
    let drive = chars.next().filter(char::is_ascii_alphabetic)?;
    if chars.next() != Some(':') || !chars.next().is_some_and(is_separator) {
        return None;
    }

    let mut normalized = format!("{}:\\", drive.to_ascii_uppercase());
    let mut first = true;
    for segment in chars.as_str().split(is_separator).filter(|segment| !segment.is_empty()) {
        let valid = segment != "."
            && segment != ".."
            && !segment.starts_with(' ')
            && !segment.ends_with(['.', ' '])
            && !segment.chars().any(
                |c| matches!(c, '\u{0}'..='\u{1F}' | '\u{7F}'..='\u{9F}' | ':' | '*' | '?' | '"' | '<' | '>' | '|'),
            );
        if !valid {
            return None;
        }
        if !first {
            normalized.push('\\');
        }
        normalized.push_str(segment);
        first = false;
    }

    return Some(normalized);

    fn is_separator(c: char) -> bool {
        c == '\\' || c == '/'
    }
}

/// Normalizes a `CustomInstallLocation` pattern as an install location is normalized ([§grants]).
///
/// Separators become `\`, repeated separators collapse, a trailing separator is dropped unless it follows a drive's `:`, and a drive letter is uppercase.
///
/// [§grants]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#grants
pub(crate) fn normalize_location_pattern(pattern: &str) -> String {
    let mut normalized = String::with_capacity(pattern.len());
    for c in pattern.chars() {
        let c = if c == '/' { '\\' } else { c };
        if c == '\\' && normalized.ends_with('\\') {
            continue;
        }
        normalized.push(c);
    }

    let mut chars = normalized.chars();
    let drive = chars.next().filter(char::is_ascii_alphabetic);
    let has_drive = drive.is_some() && chars.next() == Some(':');

    if normalized.ends_with('\\') && !(has_drive && normalized.len() == 3) {
        normalized.pop();
    }

    if let (true, Some(drive)) = (has_drive, drive) {
        normalized.replace_range(..1, &drive.to_ascii_uppercase().to_string());
    }

    normalized
}
