//! Reading a request the way the broker executes it ([§reading], [§managers]).
//!
//! This module is the only place that knows how the broker builds commands, so a change to a command changes only this module.
//!
//! [§reading]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#reading
//! [§managers]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#managers

use crate::request::{OptIns, Request};
use crate::text::{is_default_ignorable, is_white_space};
use crate::values::{Architecture, Elevation, Manager, Operation, OptIn, OptInSet, Scope};
use crate::version::{Version, is_semantic_version};

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
    /// Whether the source is a Scoop manifest URL or path, which compares with exact characters ([§manager-scoop]).
    ///
    /// [§manager-scoop]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-scoop
    pub(crate) manifest_source: bool,
    /// The package names in their manager's canonical form, or `None` when the manager's reading makes the package identity unknown.
    pub(crate) package_names: Option<Vec<String>>,
    pub(crate) canonical_form: CanonicalForm,
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
    /// The request carries one location, a plain local path, normalized.
    Passes(String),
    /// The request fails the install location check.
    Fails,
}

/// The request is unreadable ([§reading]).
///
/// [§reading]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#reading
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct Unreadable;

/// Reads the well-formed `request` the way the broker executes it.
pub(crate) fn read(request: &Request) -> Result<ReadRequest, Unreadable> {
    let operation = request.operation;
    let manager = request.manager;
    let used = used_opt_ins(&request.opt_ins);
    let custom_parameters: &[String] = request
        .opt_ins
        .custom_parameters
        .as_ref()
        .map_or(&[], |parameters| &parameters.values);

    if !(command_is_built(request, used, custom_parameters) && version_is_readable(request)) {
        return Err(Unreadable);
    }

    let parameters = match manager {
        Manager::Winget => read_winget_parameters(operation, custom_parameters)?,
        Manager::Scoop
            if custom_parameters
                .iter()
                .any(|parameter| scoop_parameter_is_refused(parameter)) =>
        {
            return Err(Unreadable);
        }
        _ => ParameterEffects::default(),
    };

    let manifest_source = manager == Manager::Scoop && is_scoop_manifest(&request.source.name);
    let identity = read_identity(manager, &request.package.id);
    let canonical_form = canonical_form(manager, manifest_source);

    // No uninstall command is limited to packages installed from the source.
    // An Update is read with an unknown source under PowerShell, PowerShell7, Scoop and Vcpkg, as each manager's reading states.
    // UNTESTED: the source of an Uninstall is unknown.
    // A valid policy names a source only in a selection without `Uninstall` and never consults `TrustedSources` for an uninstall, so no evaluation shows this value.
    // Keep it unknown when changing this code: a known source would let a rule that names a source match an uninstall if validation ever allowed one.
    let source_is_unknown = operation == Operation::Uninstall
        || (operation == Operation::Update
            && matches!(
                manager,
                Manager::PowerShell | Manager::PowerShell7 | Manager::Scoop | Manager::Vcpkg
            ));
    let source = (!source_is_unknown).then(|| request.source.name.clone());

    // The commands that update or remove a Vcpkg port also change the ports that depend on it, whichever triplet they are built for.
    let vcpkg_port_is_unspecific = manager == Manager::Vcpkg && operation != Operation::Install;

    let version = concrete_version(manager, request.package.version.as_deref(), &identity, &parameters);
    let missing_version = matches!(operation, Operation::Install | Operation::Update)
        && !matches!(manager, Manager::Scoop | Manager::Vcpkg)
        && version.is_none();

    let scope = read_scope(manager, operation, request.scope, &parameters);
    let architecture = read_architecture(
        manager,
        operation,
        request.package.architecture,
        &request.source.name,
        &parameters,
    );

    // A request that may act machine-wide without saying so in `Scope` is unreadable when it runs non-elevated.
    let scope_unknown_by_parameter = !parameters.scopes.is_empty() && scope.is_none();
    if request.elevation == Elevation::NonElevated && (scope == Some(Scope::Machine) || scope_unknown_by_parameter) {
        return Err(Unreadable);
    }

    let install_location = match &request.opt_ins.custom_install_location {
        None => InstallLocation::NotUsed,
        Some(location) => {
            normalize_plain_local_path(&location.path).map_or(InstallLocation::Fails, InstallLocation::Passes)
        }
    };

    Ok(ReadRequest {
        operation,
        manager,
        source,
        manifest_source,
        package_names: (!vcpkg_port_is_unspecific)
            .then(|| identity.names.iter().map(|name| canonical_form.apply(name)).collect()),
        canonical_form,
        version,
        missing_version,
        scope,
        architecture,
        elevation: request.elevation,
        opt_ins: used,
        custom_parameters: custom_parameters.to_vec(),
        install_location,
    })
}

/// Returns the opt-ins a request uses, which are exactly the keys of its `OptIns` ([§opt-ins]).
///
/// [§opt-ins]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#opt-ins
fn used_opt_ins(opt_ins: &OptIns) -> OptInSet {
    let mut used = OptInSet::default();
    for (opt_in, present) in [
        (OptIn::Interactive, opt_ins.interactive),
        (OptIn::SkipHashCheck, opt_ins.skip_hash_check),
        (OptIn::CustomInstallLocation, opt_ins.custom_install_location.is_some()),
        (OptIn::CustomParameters, opt_ins.custom_parameters.is_some()),
        (OptIn::InstallerArguments, opt_ins.installer_arguments.is_some()),
        (OptIn::PrePostCommands, opt_ins.pre_post_commands.is_some()),
        (OptIn::KillBeforeOperation, opt_ins.kill_before_operation.is_some()),
        (OptIn::UninstallPrevious, opt_ins.uninstall_previous),
        (OptIn::NoUpgrade, opt_ins.no_upgrade),
    ] {
        if present {
            used.insert(opt_in);
        }
    }
    used
}

/// Returns whether the broker builds a command for `request`, apart from the cases that concern its version and custom parameters ([§reading], [§managers]).
///
/// [§reading]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#reading
/// [§managers]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#managers
fn command_is_built(request: &Request, used: OptInSet, custom_parameters: &[String]) -> bool {
    let manager = request.manager;
    let id = request.package.id.as_str();
    let source = request.source.name.as_str();
    let uninstall = request.operation == Operation::Uninstall;
    let elevated = request.elevation == Elevation::Elevated;
    let architecture = request.package.architecture;
    let has_concrete_architecture = matches!(
        architecture,
        Some(Architecture::X86 | Architecture::X64 | Architecture::Arm64)
    );
    let refuses = |opt_ins: &[OptIn]| opt_ins.iter().any(|opt_in| used.contains(*opt_in));

    let installer_arguments = request
        .opt_ins
        .installer_arguments
        .as_ref()
        .map(|arguments| arguments.arguments.as_str());
    let location = request
        .opt_ins
        .custom_install_location
        .as_ref()
        .map(|location| location.path.as_str());

    // The broker cannot pass U+0000, U+000A and U+000D to a manager.
    let unpassable = |text: &str| text.contains(['\0', '\n', '\r']);
    let source_distinct = !source.starts_with(is_white_space)
        && !source.ends_with(is_white_space)
        && !source.chars().any(is_default_ignorable);
    let id_reads_as_an_option = matches!(manager, Manager::Winget | Manager::Dotnet) && id.starts_with('-');

    let common = !unpassable(source)
        && !unpassable(id)
        && !custom_parameters.iter().any(|parameter| unpassable(parameter))
        && !custom_parameters
            .iter()
            .any(|parameter| has_non_ascii_option_name(parameter))
        && !installer_arguments.is_some_and(unpassable)
        && source_distinct
        && (matches!(manager, Manager::Winget | Manager::Scoop) || !used.contains(OptIn::CustomParameters))
        && (manager == Manager::Winget || !used.contains(OptIn::InstallerArguments))
        && !id_reads_as_an_option;
    if !common {
        return false;
    }

    match manager {
        Manager::Winget => {
            !has_batch_character(id)
                && !location.is_some_and(has_batch_character)
                && !custom_parameters.iter().any(|parameter| has_batch_character(parameter))
                && !installer_arguments.is_some_and(has_batch_character)
                && is_plain_source_name(source)
        }
        Manager::PowerShell | Manager::PowerShell7 => is_plain_source_name(source) && !id.contains(['[', ']']),
        Manager::Scoop => {
            !elevated
                && !refuses(&[
                    OptIn::Interactive,
                    OptIn::CustomInstallLocation,
                    OptIn::NoUpgrade,
                    OptIn::UninstallPrevious,
                ])
                && !(uninstall && used.contains(OptIn::SkipHashCheck))
                && !(request.operation != Operation::Install && architecture.is_some())
                && !(request.operation == Operation::Install && architecture == Some(Architecture::Neutral))
                && !id.contains(['[', ']'])
                && !id.starts_with('-')
                && scoop_sources_are_readable(request.operation, source, id)
        }
        Manager::Vcpkg => {
            !elevated
                && !refuses(&[
                    OptIn::Interactive,
                    OptIn::SkipHashCheck,
                    OptIn::CustomInstallLocation,
                    OptIn::NoUpgrade,
                    OptIn::UninstallPrevious,
                ])
                && architecture.is_none()
                && is_vcpkg_triplet(source)
                && parse_vcpkg_port_spec(id).is_some_and(|(_, triplet)| triplet.is_none_or(|triplet| triplet == source))
        }
        Manager::Npm | Manager::Bun => {
            let (only_source, identifier_is_known) = if manager == Manager::Npm {
                ("npm", npm_names(id).is_some())
            } else {
                ("Bun", is_npm_registry_name(id))
            };
            let uninstall_version_unpassable =
                manager == Manager::Npm && uninstall && request.package.version.as_deref().is_some_and(unpassable);

            source.eq_ignore_ascii_case(only_source)
                && !elevated
                && !refuses(&[
                    OptIn::Interactive,
                    OptIn::SkipHashCheck,
                    OptIn::CustomInstallLocation,
                    OptIn::NoUpgrade,
                    OptIn::UninstallPrevious,
                ])
                && !has_concrete_architecture
                && identifier_is_known
                && !id.contains('%')
                && !uninstall_version_unpassable
        }
        Manager::Cargo => {
            source.eq_ignore_ascii_case("crates.io")
                && !elevated
                && !refuses(&[
                    OptIn::Interactive,
                    OptIn::SkipHashCheck,
                    OptIn::NoUpgrade,
                    OptIn::UninstallPrevious,
                    OptIn::PrePostCommands,
                    OptIn::KillBeforeOperation,
                ])
                && !has_concrete_architecture
                && id.len() <= 64
                && !id.starts_with('-')
                && id
                    .bytes()
                    .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'-' | b'_'))
                && !location.is_some_and(has_batch_character)
                && !(uninstall && request.package.version.is_some())
        }
        Manager::Pip => {
            source.eq_ignore_ascii_case("pip")
                && !elevated
                && !refuses(&[
                    OptIn::Interactive,
                    OptIn::SkipHashCheck,
                    OptIn::CustomInstallLocation,
                    OptIn::NoUpgrade,
                    OptIn::UninstallPrevious,
                ])
                && !has_concrete_architecture
                && id.bytes().next().is_some_and(|b| b.is_ascii_alphanumeric())
                && id.bytes().next_back().is_some_and(|b| b.is_ascii_alphanumeric())
                && id
                    .bytes()
                    .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'_' | b'-'))
                && !(uninstall && request.package.version.is_some())
        }
        Manager::Dotnet => {
            (uninstall || source.eq_ignore_ascii_case("nuget.org"))
                && request.scope != Some(Scope::Machine)
                && !refuses(&[
                    OptIn::Interactive,
                    OptIn::SkipHashCheck,
                    OptIn::NoUpgrade,
                    OptIn::UninstallPrevious,
                ])
                && !(uninstall && architecture.is_some())
        }
        Manager::Chocolatey => {
            let lowercase_id = id.to_ascii_lowercase();

            request.scope != Some(Scope::User)
                && !matches!(architecture, Some(Architecture::Arm64 | Architecture::Neutral))
                && !(uninstall && architecture.is_some())
                && !refuses(&[OptIn::CustomInstallLocation, OptIn::NoUpgrade, OptIn::UninstallPrevious])
                && !(uninstall && used.contains(OptIn::SkipHashCheck))
                && id.len() <= 100
                && !id.starts_with(['-', '.'])
                && id
                    .bytes()
                    .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'-' | b'_'))
                && !lowercase_id.ends_with(".config")
                && !lowercase_id.ends_with(".nupkg")
                && lowercase_id != "all"
                && (uninstall || is_plain_source_name(source))
        }
        Manager::Apt | Manager::Dnf | Manager::Flatpak | Manager::Homebrew | Manager::Pacman | Manager::Snap => false,
    }
}

/// Returns whether the request's `Package.Version`, when present, is one its manager can pin and has characters the broker passes ([§managers]).
///
/// [§managers]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#managers
fn version_is_readable(request: &Request) -> bool {
    let Some(version) = request.package.version.as_deref() else {
        return true;
    };

    match request.manager {
        Manager::Scoop | Manager::Vcpkg => false,
        Manager::Bun => is_semantic_version(version),
        Manager::Pip => version
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'!' | b'+' | b'-' | b'_')),
        manager => {
            let added = match manager {
                Manager::Npm => "~*",
                Manager::Cargo => "=~*",
                Manager::Dotnet => "[](),*",
                Manager::PowerShell7 => "[](),* ",
                _ => "",
            };
            request.operation == Operation::Uninstall
                || (!version.starts_with('-')
                    && version
                        .chars()
                        .all(|c| c.is_ascii_alphanumeric() || ".-+_".contains(c) || added.contains(c)))
        }
    }
}

/// Returns whether `text` has a character the broker cannot pass to a manager that it runs through a batch file ([§managers]).
///
/// [§managers]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#managers
fn has_batch_character(text: &str) -> bool {
    text.contains(['"', '%', '!', '^', '&', '|', '<', '>'])
}

/// Returns whether `name` is a plain source name ([§managers]).
///
/// [§managers]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#managers
fn is_plain_source_name(name: &str) -> bool {
    name.bytes().next().is_some_and(|first| first.is_ascii_alphanumeric())
        && name
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'-' | b'_' | b' '))
}

/// What a request's custom parameters do to its properties, according to its manager's reading.
#[derive(Debug, Default)]
struct ParameterEffects {
    /// Each scope an option sets, `None` when it sets no valid scope.
    scopes: Vec<Option<Scope>>,
    /// Each architecture an option sets, `None` when it sets no valid architecture.
    architectures: Vec<Option<Architecture>>,
    selects_version: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum WingetOption {
    /// Would ask for `CustomInstallLocation`.
    Location,
    Scope,
    Architecture,
    /// Would ask for `Interactive`.
    Interactive,
    /// Would ask for `InstallerArguments`.
    InstallerArguments,
    /// Would ask for `SkipHashCheck`.
    SkipHashCheck,
    /// Would ask for `NoUpgrade`.
    NoUpgrade,
    /// Would ask for `UninstallPrevious`.
    UninstallPrevious,
    Version,
    /// Would change the source, or the source of the package's dependencies.
    Source,
    /// Upgrades every installed package that has an update when the operation is an Update.
    UpgradeAll,
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
            ("source", WingetOption::Source),
            ("manifest", WingetOption::Source),
            ("dependency-source", WingetOption::Source),
            ("all", WingetOption::UpgradeAll),
            ("recurse", WingetOption::UpgradeAll),
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
            's' | 'm' => Some(Self::Source),
            'r' => Some(Self::UpgradeAll),
            _ => None,
        }
    }

    fn takes_value(self) -> bool {
        matches!(
            self,
            Self::Location | Self::Scope | Self::Architecture | Self::InstallerArguments | Self::Version | Self::Source
        )
    }
}

/// Reads WinGet custom parameters, or fails when one would ask for an opt-in, change the source or upgrade every package ([§manager-winget]).
///
/// [§manager-winget]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-winget
fn read_winget_parameters(operation: Operation, parameters: &[String]) -> Result<ParameterEffects, Unreadable> {
    let mut effects = ParameterEffects::default();
    let mut idx = 0;

    while idx < parameters.len() {
        let element = parameters[idx].as_str();
        idx += 1;

        if element == "--" {
            break;
        }

        let names = element.split('=').next().unwrap_or_default();
        let (named_options, takes_value): (Vec<WingetOption>, bool) = if let Some(long) = names.strip_prefix("--") {
            (WingetOption::from_long(long).into_iter().collect(), true)
        } else if let Some(shorts) = names.strip_prefix('-').filter(|_| 1 < element.len()) {
            (
                shorts.chars().filter_map(WingetOption::from_short).collect(),
                shorts.chars().count() == 1,
            )
        } else {
            continue;
        };

        for option in named_options {
            let value = if option.takes_value() && takes_value {
                match element.split_once('=') {
                    Some((_, value)) => Some(value.to_owned()),
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
                WingetOption::Location
                | WingetOption::Interactive
                | WingetOption::InstallerArguments
                | WingetOption::SkipHashCheck
                | WingetOption::NoUpgrade
                | WingetOption::UninstallPrevious
                | WingetOption::Source => return Err(Unreadable),
                WingetOption::UpgradeAll if operation == Operation::Update => return Err(Unreadable),
                WingetOption::UpgradeAll => {}
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
                WingetOption::Version => effects.selects_version = true,
            }
        }
    }

    Ok(effects)
}

/// Returns whether a Scoop custom parameter makes the request unreadable ([§manager-scoop]).
///
/// [§manager-scoop]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-scoop
fn scoop_parameter_is_refused(parameter: &str) -> bool {
    if matches!(parameter, "-" | "--" | "--%") || !parameter.starts_with('-') || parameter.chars().any(is_white_space) {
        return true;
    }

    match parameter.strip_prefix("--") {
        // A long option's name ends at the element's first `=` and matches ignoring ASCII case.
        Some(long) => {
            let name = long.split('=').next().unwrap_or_default().to_ascii_lowercase();
            matches!(name.as_str(), "global" | "all" | "arch" | "skip-hash-check")
        }
        // Every character of a short cluster after its `-`, including `=` and what follows, names an option.
        None => {
            let names = parameter.to_ascii_lowercase();
            !parameter.is_ascii() || names.contains(['g', 'a', 's'])
        }
    }
}

/// Returns whether a custom parameter is an option whose name has a code point above U+007F ([§reading]).
///
/// A manager that compares option names ignoring case may read such a name as an ASCII option.
///
/// [§reading]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#reading
fn has_non_ascii_option_name(parameter: &str) -> bool {
    parameter.strip_prefix('-').is_some_and(|rest| {
        let name = rest.trim_start_matches('-').split('=').next().unwrap_or_default();
        !name.is_ascii()
    })
}

/// Returns whether `text` is a Scoop manifest URL or local path ([§manager-scoop]).
///
/// [§manager-scoop]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-scoop
fn is_scoop_manifest(text: &str) -> bool {
    let after_slashes = text.trim_start_matches('/');
    text.contains("...")
        || text.contains(":\\")
        || text.contains("\\\\")
        || ["http://", "https://", "ftp://", "ftps://"]
            .iter()
            .any(|scheme| starts_with_ignore_ascii_case(after_slashes, scheme))
}

/// Returns whether another text fetches the same manifest URL, which a `Source` selector naming this one would not match ([§manager-scoop]).
///
/// A percent-encoded character, a fragment and an explicit port each allow another text.
///
/// [§manager-scoop]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-scoop
fn has_alternate_spelling(url: &str) -> bool {
    let has_port = ["http://", "https://"].iter().any(|scheme| {
        starts_with_ignore_ascii_case(url, scheme) && {
            let rest = &url[scheme.len()..];
            let authority = rest.split(['/', '\\', '?', '#']).next().unwrap_or(rest);
            authority.contains(':')
        }
    });

    url.contains(['%', '#']) || has_port
}

fn starts_with_ignore_ascii_case(text: &str, prefix: &str) -> bool {
    text.get(..prefix.len())
        .is_some_and(|head| head.eq_ignore_ascii_case(prefix))
}

/// Returns whether the request's source name and identifier name exactly one Scoop manifest to install or one app ([§manager-scoop]).
///
/// [§manager-scoop]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-scoop
fn scoop_sources_are_readable(operation: Operation, source: &str, id: &str) -> bool {
    let is_word = |text: &str| {
        !text.is_empty()
            && text
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'-' | b'_' | b'.'))
    };

    let names_one_entry = if is_scoop_manifest(source) || is_scoop_manifest(id) {
        // Scoop acts on an installed app it derives from the URL of an Update or Uninstall, which the request does not name, and may split the text after an `@` off the URL as a version.
        operation == Operation::Install
            && id == source
            && (starts_with_ignore_ascii_case(id, "http://") || starts_with_ignore_ascii_case(id, "https://"))
            && !id.contains('@')
            // The source equals the identifier here, so one check covers both.
            && !has_alternate_spelling(id)
    } else {
        is_word(source) && is_word(id) && !id.to_ascii_lowercase().ends_with(".json")
    };

    names_one_entry && !source.ends_with('.') && !id.ends_with('.')
}

/// The package names an identifier names, and whether it selects a version ([§package-identity]).
///
/// [§package-identity]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#package-identity
struct Identity {
    /// The names as written, which the caller puts in canonical form.
    names: Vec<String>,
    selects_version: bool,
}

/// Reads the identifier of a request whose command [`command_is_built`] accepted.
fn read_identity(manager: Manager, id: &str) -> Identity {
    match manager {
        Manager::Npm => Identity {
            names: npm_names(id)
                .unwrap_or_default()
                .into_iter()
                .map(str::to_owned)
                .collect(),
            selects_version: false,
        },
        Manager::Vcpkg => Identity {
            names: parse_vcpkg_port_spec(id)
                .map(|(port, _)| port.to_owned())
                .into_iter()
                .collect(),
            selects_version: false,
        },
        // WinGet and PowerShell receive the identifier whole, a bucket's app names contain no `@`, and a manifest URL or path is fetched as given.
        Manager::Bun | Manager::Scoop | Manager::Winget | Manager::PowerShell | Manager::PowerShell7 => Identity {
            names: vec![id.to_owned()],
            selects_version: false,
        },
        _ => match id.rfind('@') {
            Some(at) if 0 < at => Identity {
                names: vec![id[..at].to_owned()],
                selects_version: true,
            },
            _ => Identity {
                names: vec![id.to_owned()],
                selects_version: false,
            },
        },
    }
}

/// Returns the names an Npm identifier names, or `None` when it is neither a registry name nor an alias ([§manager-npm]).
///
/// [§manager-npm]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-npm
fn npm_names(id: &str) -> Option<Vec<&str>> {
    if is_npm_registry_name(id) {
        return Some(vec![id]);
    }

    let (alias, target) = id.split_once(':')?;
    let target = match target.rfind('@') {
        Some(at) if 0 < at => &target[..at],
        _ => target,
    };
    (is_npm_registry_name(alias) && is_npm_registry_name(target)).then(|| vec![alias, target])
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

/// Returns whether `text` is a Vcpkg triplet ([§manager-vcpkg]).
///
/// [§manager-vcpkg]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-vcpkg
fn is_vcpkg_triplet(text: &str) -> bool {
    !text.is_empty()
        && text
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'-' | b'_' | b'.'))
}

/// Splits a Vcpkg port spec into its port and its triplet, or returns `None` when `id` is not a port spec ([§manager-vcpkg]).
///
/// [§manager-vcpkg]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#manager-vcpkg
fn parse_vcpkg_port_spec(id: &str) -> Option<(&str, Option<&str>)> {
    let is_name = |text: &str| {
        !text.is_empty()
            && !text.starts_with('-')
            && text
                .bytes()
                .all(|b| b.is_ascii_lowercase() || b.is_ascii_digit() || b == b'-')
    };

    let (head, triplet) = match id.split_once(':') {
        Some((head, triplet)) => (head, Some(triplet)),
        None => (id, None),
    };
    let (port, features) = match head.split_once('[') {
        Some((port, features)) => (port, Some(features.strip_suffix(']')?)),
        None => (head, None),
    };

    (is_name(port)
        && features.is_none_or(|features| features.split(',').all(is_name))
        && triplet.is_none_or(is_vcpkg_triplet))
    .then_some((port, triplet))
}

/// How a manager puts a package name in canonical form ([§package-identity]).
///
/// [§package-identity]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#package-identity
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum CanonicalForm {
    /// ASCII letters in lowercase.
    Lowercase,
    /// The name as written.
    Exact,
    /// ASCII letters in lowercase and `_` as `-`.
    Cargo,
    /// ASCII letters in lowercase and each run of `-`, `_` and `.` as one `-`.
    Pip,
}

fn canonical_form(manager: Manager, manifest_source: bool) -> CanonicalForm {
    match manager {
        Manager::Npm | Manager::Bun | Manager::Winget => CanonicalForm::Exact,
        Manager::Scoop if manifest_source => CanonicalForm::Exact,
        Manager::Cargo => CanonicalForm::Cargo,
        Manager::Pip => CanonicalForm::Pip,
        _ => CanonicalForm::Lowercase,
    }
}

impl CanonicalForm {
    /// Puts a package name, or the characters of a pattern between `*`, in canonical form.
    pub(crate) fn apply(self, name: &str) -> String {
        match self {
            Self::Exact => name.to_owned(),
            Self::Lowercase => name.to_ascii_lowercase(),
            Self::Cargo => name.to_ascii_lowercase().replace('_', "-"),
            Self::Pip => {
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
        }
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
        Manager::Npm | Manager::Bun => is_semantic_version(version),
        Manager::Cargo | Manager::Dotnet => Version::parse(version).is_some_and(|version| 3 <= version.release_count()),
        Manager::PowerShell7 => Version::parse(version).is_some(),
        Manager::Pip => version
            .split('.')
            .all(|component| !component.is_empty() && component.bytes().all(|b| b.is_ascii_digit())),
        _ => true,
    };

    resolved_exactly.then(|| version.to_owned())
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
    operation: Operation,
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
        Manager::Vcpkg if operation != Operation::Install => None,
        Manager::Vcpkg => {
            let triplet = source_name.to_ascii_lowercase();
            [
                ("x86-", Architecture::X86),
                ("x64-", Architecture::X64),
                ("arm64-", Architecture::Arm64),
            ]
            .into_iter()
            .find(|(prefix, _)| triplet.starts_with(prefix))
            .map(|(_, architecture)| architecture)
        }
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
// UNTESTED: this function reads only the text of the path and never asks the operating system what a drive or path refers to.
// Both implementations must give the same result on any machine, and the suite cannot run on every drive layout and environment.
// When changing it, check that no file-system, environment or operating-system API is called on this path.
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
/// Each `/` becomes `\`, repeated `\` collapse, and a trailing `\` is dropped unless it follows a `:`.
/// The drive letter keeps its case, because patterns match ignoring case.
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

    if normalized.ends_with('\\') && !normalized.ends_with(":\\") {
        normalized.pop();
    }

    normalized
}
