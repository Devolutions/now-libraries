//! The package request the evaluator decides ([§request-input]).
//!
//! The types mirror the package request of the wire contract ([§package-request]) without its `RequestId`.
//! A member from a closed set is an enum, so a value outside the set cannot be built.
//! Lengths, character sets and the other limits of the wire contract are not types: [`Request`] says what the evaluator does with a request that breaks one.
//!
//! [§request-input]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#request-input
//! [§package-request]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.wire.md#package-request

use core::fmt;

use crate::values::{Architecture, Elevation, InstallerArgumentsMode, Manager, Operation, Scope};

/// A request to install, update or uninstall one package through one manager.
///
/// The types allow requests that the wire contract rejects as `BadRequest` ([§package-request]): a member outside its lengths, an empty list or string, a `Package.Id` outside its character set, `PrePostCommands` with neither `Pre` nor `Post`, a `*` or `?` in `Processes`, and `Scope` `Machine` with `Elevation` `NonElevated`.
/// [`evaluate`](crate::evaluate) returns [`BadRequest`] for such a request, whatever the policy says.
///
/// [§package-request]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.wire.md#package-request
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Request {
    pub operation: Operation,
    pub manager: Manager,
    pub source: Source,
    pub package: Package,
    /// The scope the request names, or `None` to leave it to the manager's reading.
    pub scope: Option<Scope>,
    pub elevation: Elevation,
    pub opt_ins: OptIns,
}

/// The request is not well-formed under the wire contract ([§request-input]), so it gets no decision.
///
/// This is an error ([§error]), not a decision.
///
/// [§request-input]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#request-input
/// [§error]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#def-error
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BadRequest;

impl fmt::Display for BadRequest {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("request is not well-formed")
    }
}

impl core::error::Error for BadRequest {}

/// The source of the package.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Source {
    /// A name of 1 to 128 bytes.
    pub name: String,
}

/// The package a request names.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Package {
    /// A string of 1 to 256 ASCII letters, digits and `.`, `_`, `+`, `@`, `/`, `:`, `[`, `]`, `,`, `#`, `$`, `%`, `{`, `}` and `-`.
    pub id: String,
    /// A string of 1 to 128 bytes.
    pub version: Option<String>,
    pub architecture: Option<Architecture>,
}

/// The opt-ins a request asks for, one member per key of the wire contract's `OptIns` ([§opt-ins]).
///
/// A request asks for an opt-in exactly when its member is set.
/// An opt-in with no value is a `bool`, because the wire value is `{}`, which says nothing beyond its presence.
///
/// [§opt-ins]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.wire.md#opt-ins
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct OptIns {
    pub interactive: bool,
    pub skip_hash_check: bool,
    pub custom_install_location: Option<CustomInstallLocation>,
    pub custom_parameters: Option<CustomParameters>,
    pub installer_arguments: Option<InstallerArguments>,
    pub pre_post_commands: Option<PrePostCommands>,
    pub kill_before_operation: Option<KillBeforeOperation>,
    pub uninstall_previous: bool,
    pub no_upgrade: bool,
}

/// Where the package is installed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CustomInstallLocation {
    /// A string of 1 to 2048 bytes.
    pub path: String,
}

/// Parameters the manager receives as they are.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CustomParameters {
    /// 1 to 64 strings of 1 to 512 bytes.
    pub values: Vec<String>,
}

/// Arguments the installer itself receives.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallerArguments {
    /// A string of 1 to 2048 bytes.
    pub arguments: String,
    pub mode: InstallerArgumentsMode,
}

/// Commands the broker runs around the manager process.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PrePostCommands {
    /// A string of 1 to 2048 bytes.
    pub pre: Option<String>,
    /// A string of 1 to 2048 bytes; at least one of `pre` and `post` is set.
    pub post: Option<String>,
}

/// Processes the broker stops before the manager process.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct KillBeforeOperation {
    /// 1 to 64 image names of 1 to 128 bytes, without `*` or `?`.
    pub processes: Vec<String>,
}

impl Request {
    /// Returns whether the request is well-formed under the wire contract ([§package-request]).
    ///
    /// [§package-request]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.wire.md#package-request
    pub(crate) fn is_well_formed(&self) -> bool {
        let within = |text: &str, max: usize| (1..=max).contains(&text.len());
        let package_id_character = |b: u8| {
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
        };

        let opt_ins = &self.opt_ins;
        let strings_within = |strings: &[String], max_count: usize, max_len: usize| {
            (1..=max_count).contains(&strings.len()) && strings.iter().all(|text| within(text, max_len))
        };

        !(self.scope == Some(Scope::Machine) && self.elevation == Elevation::NonElevated)
            && within(&self.source.name, 128)
            && within(&self.package.id, 256)
            && self.package.id.bytes().all(package_id_character)
            && self
                .package
                .version
                .as_deref()
                .is_none_or(|version| within(version, 128))
            && opt_ins
                .custom_install_location
                .as_ref()
                .is_none_or(|location| within(&location.path, 2048))
            && opt_ins
                .custom_parameters
                .as_ref()
                .is_none_or(|parameters| strings_within(&parameters.values, 64, 512))
            && opt_ins
                .installer_arguments
                .as_ref()
                .is_none_or(|arguments| within(&arguments.arguments, 2048))
            && opt_ins.pre_post_commands.as_ref().is_none_or(|commands| {
                (commands.pre.is_some() || commands.post.is_some())
                    && commands
                        .pre
                        .iter()
                        .chain(&commands.post)
                        .all(|command| within(command, 2048))
            })
            && opt_ins.kill_before_operation.as_ref().is_none_or(|kill| {
                strings_within(&kill.processes, 64, 128)
                    && kill.processes.iter().all(|process| !process.contains(['*', '?']))
            })
    }
}
