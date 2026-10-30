//! Evaluating a request against a policy ([§evaluation]).
//!
//! [§evaluation]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#evaluation

use crate::document::{OptInPatterns, Policy, Rule, Selection, SourceSelector, VersionCondition};
use crate::reading::{InstallLocation, ReadRequest, read};
use crate::request::{BadRequest, Request};
use crate::text::{eq_ignore_case, map_pattern_segments, pattern_matches_exact, pattern_matches_ignore_case};
use crate::timestamp::Timestamp;
use crate::values::{Manager, Operation, OptIn, OptInSet, RuleDecision};
use crate::version::Version;

/// Whether the broker runs the request, `Allow` or `Deny` ([§decision]).
///
/// [§decision]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#def-decision
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Outcome {
    Allow,
    Deny,
}

impl Outcome {
    /// Returns the contract's name for the outcome.
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Allow => "Allow",
            Self::Deny => "Deny",
        }
    }
}

/// The check that produced a decision ([§precedence]).
///
/// The order of the variants is the precedence of the checks, highest first.
///
/// [§precedence]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#precedence
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum Check {
    /// The request is well-formed, but the evaluator cannot tell what the broker would run for it ([§unreadable]).
    ///
    /// An unreadable request gets a `Deny` decision, not an error.
    ///
    /// [§unreadable]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#def-unreadable
    UnreadableRequest,
    ValidityWindow,
    InstallLocation,
    MissingVersion,
    UntrustedSource,
    DenyRule,
    UngrantedOptIn,
    DefaultDeny,
    AllowRules,
}

impl Check {
    /// Returns the contract's name for the check.
    pub fn as_str(self) -> &'static str {
        match self {
            Self::UnreadableRequest => "UnreadableRequest",
            Self::ValidityWindow => "ValidityWindow",
            Self::InstallLocation => "InstallLocation",
            Self::MissingVersion => "MissingVersion",
            Self::UntrustedSource => "UntrustedSource",
            Self::DenyRule => "DenyRule",
            Self::UngrantedOptIn => "UngrantedOptIn",
            Self::DefaultDeny => "DefaultDeny",
            Self::AllowRules => "AllowRules",
        }
    }

    /// Returns whether audit mode lets a request through when this check produces its decision.
    fn audit_mode_lets_through(self) -> bool {
        matches!(
            self,
            Self::UntrustedSource | Self::DenyRule | Self::UngrantedOptIn | Self::DefaultDeny
        )
    }
}

/// A rule a decision names, with its `Reason` when it has one.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct DecisionRule {
    pub id: String,
    pub reason: Option<String>,
}

/// The result of evaluating one well-formed request: an outcome, and the check, rules and opt-ins that produced it ([§decision]).
///
/// Every well-formed request gets a decision, an unreadable one included.
/// A result that has no decision is an error ([§error]).
///
/// [§decision]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#def-decision
/// [§error]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#def-error
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct Decision {
    pub outcome: Outcome,
    pub check: Check,
    /// Rules in the order of the policy's `Rules`.
    pub rules: Vec<DecisionRule>,
    /// Opt-ins in the order of [`OptIn`].
    pub opt_ins: Vec<OptIn>,
    /// Whether audit mode let a denied request run.
    pub audited: bool,
}

/// Evaluates `request` against `policy` at the evaluation time `time`.
///
/// A request that is not well-formed gets an error and no decision ([§request-input]): this returns [`BadRequest`], whatever the policy says.
/// An unreadable request is well-formed and gets a `Deny` decision with the check `UnreadableRequest`.
///
/// [§request-input]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#request-input
pub fn evaluate(policy: &Policy, request: &Request, time: Timestamp) -> Result<Decision, BadRequest> {
    if !request.is_well_formed() {
        return Err(BadRequest);
    }

    Ok(decide_well_formed(policy, request, time))
}

fn decide_well_formed(policy: &Policy, request: &Request, time: Timestamp) -> Decision {
    let Ok(request) = read(request) else {
        return decide(policy, Check::UnreadableRequest, Vec::new(), OptInSet::default());
    };

    let in_force = !policy.valid_from.is_some_and(|valid_from| time < valid_from)
        && !policy.valid_until.is_some_and(|valid_until| valid_until < time);
    if !in_force {
        return decide(policy, Check::ValidityWindow, Vec::new(), OptInSet::default());
    }

    if request.install_location == InstallLocation::Fails {
        return decide(policy, Check::InstallLocation, Vec::new(), OptInSet::default());
    }

    if request.missing_version {
        return decide(policy, Check::MissingVersion, Vec::new(), OptInSet::default());
    }

    let installs = matches!(request.operation, Operation::Install | Operation::Update);
    let trusted = policy
        .trusted_sources
        .iter()
        .any(|selector| selector_matches(selector, &request) == Match::Yes);
    if installs && !trusted {
        return decide(policy, Check::UntrustedSource, Vec::new(), OptInSet::default());
    }

    let mut denying_rules = Vec::new();
    let mut denied_opt_ins = OptInSet::default();
    for rule in policy.rules.iter().filter(|rule| rule.decision == RuleDecision::Deny) {
        if !is_applicable(rule, &request) {
            continue;
        }
        match &rule.opt_ins {
            None => denying_rules.push(rule),
            Some(forbids) => {
                let mut denies = false;
                for (opt_in, forbid) in forbids {
                    if request.opt_ins.contains(*opt_in) && covers_request(*opt_in, forbid, &request) {
                        denied_opt_ins.insert(*opt_in);
                        denies = true;
                    }
                }
                if denies {
                    denying_rules.push(rule);
                }
            }
        }
    }
    if !denying_rules.is_empty() {
        return decide(policy, Check::DenyRule, denying_rules, denied_opt_ins);
    }

    let applicable_allow_rules: Vec<&Rule> = policy
        .rules
        .iter()
        .filter(|rule| rule.decision == RuleDecision::Allow && is_applicable(rule, &request))
        .collect();
    if applicable_allow_rules.is_empty() {
        return decide(policy, Check::DefaultDeny, Vec::new(), request.opt_ins);
    }

    let mut ungranted = OptInSet::default();
    for opt_in in request.opt_ins.iter() {
        if !is_granted(opt_in, &applicable_allow_rules, &request) {
            ungranted.insert(opt_in);
        }
    }
    if ungranted.iter().next().is_some() {
        return decide(policy, Check::UngrantedOptIn, applicable_allow_rules, ungranted);
    }

    decide(policy, Check::AllowRules, applicable_allow_rules, OptInSet::default())
}

/// Builds the decision a check produces, naming `rules` in policy order and `opt_ins` in opt-in order.
fn decide(policy: &Policy, check: Check, rules: Vec<&Rule>, opt_ins: OptInSet) -> Decision {
    let audited = check.audit_mode_lets_through() && policy.audit_mode;
    let outcome = if check == Check::AllowRules || audited {
        Outcome::Allow
    } else {
        Outcome::Deny
    };

    Decision {
        outcome,
        check,
        rules: rules
            .into_iter()
            .map(|rule| DecisionRule {
                id: rule.id.clone(),
                reason: rule.reason.clone(),
            })
            .collect(),
        opt_ins: opt_ins.iter().collect(),
        audited,
    }
}

/// Whether a condition, a selection or an `Except` list matches ([§applicability]).
///
/// [§applicability]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#applicability
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Match {
    Yes,
    No,
    Unknown,
}

impl Match {
    /// Combines entries of which one matching suffices: list conditions and `Except` lists.
    fn any(entries: impl Iterator<Item = Match>) -> Match {
        let mut result = Match::No;
        for entry in entries {
            match entry {
                Match::Yes => return Match::Yes,
                Match::Unknown => result = Match::Unknown,
                Match::No => {}
            }
        }
        result
    }

    /// Combines conditions that must all hold: the conditions of a selection.
    fn all(conditions: impl Iterator<Item = Match>) -> Match {
        let mut result = Match::Yes;
        for condition in conditions {
            match condition {
                Match::No => return Match::No,
                Match::Unknown => result = Match::Unknown,
                Match::Yes => {}
            }
        }
        result
    }

    fn from_bool(holds: bool) -> Match {
        if holds { Match::Yes } else { Match::No }
    }
}

fn is_applicable(rule: &Rule, request: &ReadRequest) -> bool {
    if !rule.enabled {
        return false;
    }

    let select = selection_matches(&rule.select, request);
    let except = Match::any(
        rule.except
            .iter()
            .map(|selection| selection_matches(selection, request)),
    );

    match rule.decision {
        RuleDecision::Allow => select == Match::Yes && except == Match::No,
        RuleDecision::Deny => select != Match::No && except != Match::Yes,
    }
}

fn selection_matches(selection: &Selection, request: &ReadRequest) -> Match {
    let operations = selection
        .operations
        .as_ref()
        .map(|operations| Match::from_bool(operations.contains(&request.operation)));

    let sources = selection
        .sources
        .as_ref()
        .map(|sources| Match::any(sources.iter().map(|selector| selector_matches(selector, request))));

    let packages = selection
        .packages
        .as_ref()
        .map(|patterns| packages_match(patterns, request));

    let version = selection
        .version
        .as_ref()
        .map(|condition| version_matches(condition, request));

    let scopes = selection.scopes.as_ref().map(|scopes| match request.scope {
        Some(scope) => Match::from_bool(scopes.contains(&scope)),
        None => Match::Unknown,
    });

    let architectures = selection
        .architectures
        .as_ref()
        .map(|architectures| match request.architecture {
            Some(architecture) => Match::from_bool(architectures.contains(&architecture)),
            None => Match::Unknown,
        });

    let elevation = selection
        .elevation
        .map(|elevation| Match::from_bool(elevation == request.elevation));

    Match::all(
        [operations, sources, packages, version, scopes, architectures, elevation]
            .into_iter()
            .flatten(),
    )
}

/// Matches a source selector against a request ([§source-selector]).
///
/// [§source-selector]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#source-selector
fn selector_matches(selector: &SourceSelector, request: &ReadRequest) -> Match {
    if selector.manager != request.manager {
        return Match::No;
    }

    match (&selector.source, &request.source) {
        (None, _) => Match::Yes,
        // A Scoop manifest source compares with exact characters, and a name that differs only in case is unknown.
        (Some(name), Some(source)) if request.manifest_source => {
            if name == source {
                Match::Yes
            } else if eq_ignore_case(name, source) {
                Match::Unknown
            } else {
                Match::No
            }
        }
        (Some(name), Some(source)) => Match::from_bool(eq_ignore_case(name, source)),
        (Some(_), None) => Match::Unknown,
    }
}

/// Matches `Packages` patterns against the request's package names ([§package-identity]).
///
/// [§package-identity]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#package-identity
fn packages_match(patterns: &[String], request: &ReadRequest) -> Match {
    let Some(names) = &request.package_names else {
        return Match::Unknown;
    };
    let patterns: Vec<String> = patterns
        .iter()
        .map(|pattern| map_pattern_segments(pattern, |segment| request.canonical_form.apply(segment)))
        .collect();

    let mut all_match = true;
    let mut none_match = true;
    for name in names {
        let name_match = if patterns.iter().any(|pattern| pattern_matches_exact(pattern, name)) {
            Match::Yes
        } else if patterns
            .iter()
            .any(|pattern| pattern_matches_ignore_case(pattern, name))
        {
            Match::Unknown
        } else {
            Match::No
        };
        all_match &= name_match == Match::Yes;
        none_match &= name_match == Match::No;
    }

    if all_match {
        Match::Yes
    } else if none_match {
        Match::No
    } else {
        Match::Unknown
    }
}

/// Matches a version condition against the request's version ([§version-condition]).
///
/// [§version-condition]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#version-condition
fn version_matches(condition: &VersionCondition, request: &ReadRequest) -> Match {
    let Some(version) = request.version.as_deref().and_then(Version::parse) else {
        return Match::Unknown;
    };

    let is_prerelease = version.has_prerelease() && request.manager != Manager::Winget;

    Match::from_bool(
        condition.minimum.as_ref().is_none_or(|minimum| minimum <= &version)
            && condition.maximum.as_ref().is_none_or(|maximum| &version <= maximum)
            && !(condition.exclude_prerelease && is_prerelease),
    )
}

/// Returns whether a request that uses `opt_in` is covered by a grant or forbid of it ([§grants]).
///
/// [§grants]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#grants
fn covers_request(opt_in: OptIn, patterns: &OptInPatterns, request: &ReadRequest) -> bool {
    match opt_in {
        OptIn::CustomParameters => request
            .custom_parameters
            .iter()
            .any(|parameter| covers_value(patterns, parameter)),
        OptIn::CustomInstallLocation => match &request.install_location {
            InstallLocation::Passes(location) => covers_value(patterns, location),
            InstallLocation::NotUsed | InstallLocation::Fails => false,
        },
        _ => true,
    }
}

/// Returns whether `opt_in`, which the request uses, is granted by the applicable Allow rules ([§opt-in-evaluation]).
///
/// [§opt-in-evaluation]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#opt-in-evaluation
fn is_granted(opt_in: OptIn, rules: &[&Rule], request: &ReadRequest) -> bool {
    let grants: Vec<&OptInPatterns> = rules
        .iter()
        .filter_map(|rule| rule.opt_ins.as_ref())
        .flatten()
        .filter(|(granted, _)| *granted == opt_in)
        .map(|(_, grant)| grant)
        .collect();

    let covered = |value: &str| grants.iter().any(|grant| covers_value(grant, value));

    match opt_in {
        OptIn::CustomParameters => request.custom_parameters.iter().all(|parameter| covered(parameter)),
        OptIn::CustomInstallLocation => match &request.install_location {
            InstallLocation::Passes(location) => covered(location),
            InstallLocation::NotUsed | InstallLocation::Fails => false,
        },
        _ => !grants.is_empty(),
    }
}

/// Returns whether `value` matches one of the `Patterns` and none of the `Except` patterns, ignoring case.
fn covers_value(patterns: &OptInPatterns, value: &str) -> bool {
    patterns
        .patterns
        .iter()
        .any(|pattern| pattern_matches_ignore_case(pattern, value))
        && !patterns
            .except
            .iter()
            .any(|pattern| pattern_matches_ignore_case(pattern, value))
}
