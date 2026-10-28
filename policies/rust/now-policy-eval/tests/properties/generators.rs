//! Generators of valid policies, requests and evaluation times over a small vocabulary, so that generated rules often apply to generated requests.

use core::fmt::Write as _;

use now_policy_eval::{
    Architecture, CustomInstallLocation, CustomParameters, Elevation, InstallerArguments, InstallerArgumentsMode,
    KillBeforeOperation, Manager, Operation, OptIns, Package, Policy, PrePostCommands, Request, Scope, Source,
    Timestamp, parse_policy,
};
use proptest::prelude::*;
use proptest::sample::{select, subsequence};

const MANAGERS: &[&str] = &["Winget", "Chocolatey", "Npm", "Scoop", "Pip"];
const SOURCES: &[&str] = &["winget", "chocolatey", "npm", "main", "pip", "other"];
const OPERATIONS: &[&str] = &["Install", "Update", "Uninstall"];
const PACKAGE_PATTERNS: &[&str] = &["Contoso.*", "contoso.tool", "*", "react", "React", "Fab*", "my-*"];
const PACKAGE_IDS: &[&str] = &[
    "Contoso.Tool",
    "react",
    "React",
    "my-react:react",
    "Fabrikam.App",
    "git",
    "a@1.0",
];
/// Versions in ascending order, so that a bound pair taken in order is a valid range.
const BOUNDS: &[&str] = &["0.9", "1.0", "1.2.3-beta", "1.2.3", "2.0"];
const VERSIONS: &[&str] = &["1.0", "1.2.3", "1.2.3-beta", "2.0.0", "3.0.0", "1.2.3", "latest"];
const SCOPES: &[&str] = &["User", "Machine"];
const ARCHITECTURES: &[&str] = &["X86", "X64", "Arm64", "Neutral"];
const ELEVATIONS: &[&str] = &["NonElevated", "Elevated"];
const SIMPLE_OPT_INS: &[&str] = &[
    "Interactive",
    "SkipHashCheck",
    "InstallerArguments",
    "PrePostCommands",
    "KillBeforeOperation",
    "UninstallPrevious",
    "NoUpgrade",
];
const PARAMETER_PATTERNS: &[&str] = &["/quiet", "/Q*", "--override", "*", "/log=*", "-i"];
const LOCATION_PATTERNS: &[&str] = &["C:/Apps/*", "C:\\Apps", "*", "D:/*", "c:\\apps\\tool"];
const PARAMETERS: &[&str] = &["/quiet", "/log=x", "--override", "-i", "--location", "C:\\Apps", "-s"];
const LOCATIONS: &[&str] = &["C:\\Apps\\Tool", "D:\\x", "c:/apps", "relative"];
const TIMES: &[&str] = &[
    "2026-01-01T00:00:00Z",
    "2026-03-01T00:00:00Z",
    "2026-06-01T00:00:00Z",
    "2027-01-01T00:00:00Z",
];

#[derive(Debug, Clone)]
pub(crate) struct GenSelector {
    manager: &'static str,
    source: Option<&'static str>,
}

#[derive(Debug, Clone)]
pub(crate) struct GenVersion {
    minimum: Option<&'static str>,
    maximum: Option<&'static str>,
    exclude_prerelease: bool,
}

#[derive(Debug, Clone, Default)]
pub(crate) struct GenSelection {
    operations: Option<Vec<&'static str>>,
    sources: Option<Vec<GenSelector>>,
    packages: Option<Vec<&'static str>>,
    version: Option<GenVersion>,
    scopes: Option<Vec<&'static str>>,
    architectures: Option<Vec<&'static str>>,
    elevation: Option<&'static str>,
}

#[derive(Debug, Clone)]
pub(crate) struct GenOptIn {
    name: &'static str,
    patterns: Vec<&'static str>,
    except: Vec<&'static str>,
}

#[derive(Debug, Clone)]
pub(crate) struct GenRule {
    pub(crate) deny: bool,
    enabled: Option<bool>,
    reason: Option<&'static str>,
    select: GenSelection,
    except: Vec<GenSelection>,
    opt_ins: Vec<GenOptIn>,
}

#[derive(Debug, Clone)]
pub(crate) struct GenPolicy {
    valid_from: Option<&'static str>,
    valid_until: Option<&'static str>,
    audit_mode: Option<bool>,
    trusted_sources: Vec<GenSelector>,
    pub(crate) rules: Vec<GenRule>,
}

fn selector() -> impl Strategy<Value = GenSelector> {
    (select(MANAGERS), prop::option::weighted(0.6, select(SOURCES)))
        .prop_map(|(manager, source)| GenSelector { manager, source })
}

fn non_empty(values: &'static [&'static str]) -> impl Strategy<Value = Vec<&'static str>> {
    subsequence(values, 1..=values.len())
}

fn selection(non_trivial: bool) -> impl Strategy<Value = GenSelection> {
    let version = (
        prop::option::of(0..BOUNDS.len()),
        prop::option::of(0..BOUNDS.len()),
        any::<bool>(),
    )
        .prop_filter_map("a version condition constrains something", |(low, high, exclude)| {
            let (low, high) = match (low, high) {
                (Some(low), Some(high)) => (Some(low.min(high)), Some(low.max(high))),
                other => other,
            };
            (low.is_some() || high.is_some() || exclude).then(|| GenVersion {
                minimum: low.map(|idx| BOUNDS[idx]),
                maximum: high.map(|idx| BOUNDS[idx]),
                exclude_prerelease: exclude,
            })
        });

    (
        prop::option::weighted(0.25, non_empty(OPERATIONS)),
        prop::option::weighted(0.25, prop::collection::vec(selector(), 1..3)),
        prop::option::weighted(0.3, non_empty(PACKAGE_PATTERNS)),
        prop::option::weighted(0.2, version),
        prop::option::weighted(0.2, non_empty(SCOPES)),
        prop::option::weighted(0.2, non_empty(ARCHITECTURES)),
        prop::option::weighted(0.2, select(ELEVATIONS)),
    )
        .prop_map(
            |(operations, sources, packages, version, scopes, architectures, elevation)| {
                let mut selection = GenSelection {
                    operations,
                    sources,
                    packages,
                    version,
                    scopes,
                    architectures,
                    elevation,
                };
                // A version condition needs operations that exclude Uninstall.
                if selection.version.is_some() {
                    let mut operations: Vec<&'static str> = selection
                        .operations
                        .take()
                        .unwrap_or_default()
                        .into_iter()
                        .filter(|operation| *operation != "Uninstall")
                        .collect();
                    if operations.is_empty() {
                        operations.push("Install");
                    }
                    selection.operations = Some(operations);
                }
                selection
            },
        )
        .prop_filter("an Except entry is not empty", move |selection| {
            !non_trivial || !selection.is_empty()
        })
}

impl GenSelection {
    fn is_empty(&self) -> bool {
        self.operations.is_none()
            && self.sources.is_none()
            && self.packages.is_none()
            && self.version.is_none()
            && self.scopes.is_none()
            && self.architectures.is_none()
            && self.elevation.is_none()
    }
}

fn opt_in() -> impl Strategy<Value = GenOptIn> {
    prop_oneof![
        select(SIMPLE_OPT_INS).prop_map(|name| GenOptIn {
            name,
            patterns: Vec::new(),
            except: Vec::new(),
        }),
        (non_empty(PARAMETER_PATTERNS), subsequence(PARAMETER_PATTERNS, 0..2)).prop_map(|(patterns, except)| {
            GenOptIn {
                name: "CustomParameters",
                patterns,
                except,
            }
        }),
        (non_empty(LOCATION_PATTERNS), subsequence(LOCATION_PATTERNS, 0..2)).prop_map(|(patterns, except)| {
            GenOptIn {
                name: "CustomInstallLocation",
                patterns,
                except,
            }
        }),
    ]
}

/// Generates a rule that is valid in any policy, given an id distinct from the others.
pub(crate) fn rule(deny: impl Strategy<Value = bool>) -> impl Strategy<Value = GenRule> {
    (
        deny,
        prop::option::weighted(0.2, any::<bool>()),
        prop::option::of(select(&["", "Reason"][..])),
        selection(false),
        prop::collection::vec(selection(true), 0..2),
        prop::collection::vec(opt_in(), 0..4),
    )
        .prop_map(|(deny, enabled, reason, select, except, opt_ins)| {
            // A grant or forbid object has each opt-in once.
            let mut unique: Vec<GenOptIn> = Vec::new();
            for opt_in in opt_ins {
                if !unique.iter().any(|existing| existing.name == opt_in.name) {
                    unique.push(opt_in);
                }
            }
            GenRule {
                deny,
                enabled,
                reason,
                select,
                except,
                opt_ins: unique,
            }
        })
}

pub(crate) fn policy() -> impl Strategy<Value = GenPolicy> {
    (
        prop::option::weighted(0.3, 0usize..2),
        prop::option::weighted(0.3, 2usize..4),
        prop::option::of(any::<bool>()),
        prop::collection::vec(selector(), 0..8),
        prop::collection::vec(rule(prop::bool::weighted(0.35)), 0..6),
    )
        .prop_map(
            |(valid_from, valid_until, audit_mode, trusted_sources, rules)| GenPolicy {
                valid_from: valid_from.map(|idx| TIMES[idx]),
                valid_until: valid_until.map(|idx| TIMES[idx]),
                audit_mode,
                trusted_sources,
                rules,
            },
        )
}

pub(crate) fn time() -> impl Strategy<Value = Timestamp> {
    select(TIMES).prop_map(|text| text.parse().expect("generator timestamp"))
}

pub(crate) fn request() -> impl Strategy<Value = Request> {
    let opt_ins = (
        prop::option::of(select(SCOPES)),
        [
            prop::bool::weighted(0.15),
            prop::bool::weighted(0.15),
            prop::bool::weighted(0.15),
            prop::bool::weighted(0.15),
        ],
        prop::option::weighted(0.2, select(LOCATIONS)),
        prop::option::weighted(0.3, subsequence(PARAMETERS, 1..3)),
        prop::option::weighted(0.2, select(&["cmd"][..])),
        prop::option::weighted(0.2, select(&["tool.exe"][..])),
        prop::option::weighted(0.15, select(InstallerArgumentsMode::ALL)),
    );

    (
        select(OPERATIONS),
        select(MANAGERS),
        prop::option::weighted(0.15, select(SOURCES)),
        select(PACKAGE_IDS),
        prop::option::weighted(0.8, select(VERSIONS)),
        prop::option::of(select(ARCHITECTURES)),
        select(ELEVATIONS),
        opt_ins,
    )
        .prop_map(
            |(operation, manager, source, id, version, architecture, elevation, opt_ins)| {
                let (scope, flags, location, parameters, pre_command, kill, installer_arguments_mode) = opt_ins;
                let mut request = request_through(operation, manager);
                // Most requests use the source their manager reads, so that few are unreadable.
                let usual_source = match manager {
                    "Winget" => "winget",
                    "Chocolatey" => "chocolatey",
                    "Npm" => "npm",
                    "Pip" => "pip",
                    _ => "main",
                };
                request.source.name = source.unwrap_or(usual_source).to_owned();
                request.package.id = id.to_owned();
                // Scoop cannot pin a version, so it rarely gets one.
                if manager != "Scoop" {
                    request.package.version = version.map(str::to_owned);
                }
                request.package.architecture =
                    architecture.map(|name| Architecture::from_name(name).expect("generator architecture"));
                request.scope = scope.map(|name| Scope::from_name(name).expect("generator scope"));
                request.elevation = Elevation::from_name(elevation).expect("generator elevation");
                // A machine-wide request is elevated, as the wire contract requires.
                if request.scope == Some(Scope::Machine) {
                    request.elevation = Elevation::Elevated;
                }
                request.opt_ins.interactive = flags[0];
                request.opt_ins.skip_hash_check = flags[1];
                request.opt_ins.uninstall_previous = flags[2];
                request.opt_ins.no_upgrade = flags[3];
                request.opt_ins.custom_install_location =
                    location.map(|path| CustomInstallLocation { path: path.to_owned() });
                // Only WinGet and Scoop read custom parameters, and only WinGet reads installer arguments.
                if matches!(manager, "Winget" | "Scoop") {
                    request.opt_ins.custom_parameters = parameters.map(|values| CustomParameters {
                        values: values.into_iter().map(str::to_owned).collect(),
                    });
                }
                if manager == "Winget" {
                    request.opt_ins.installer_arguments = installer_arguments_mode.map(|mode| InstallerArguments {
                        arguments: "/q".to_owned(),
                        mode,
                    });
                }
                request.opt_ins.pre_post_commands = pre_command.map(|command| PrePostCommands {
                    pre: Some(command.to_owned()),
                    post: None,
                });
                request.opt_ins.kill_before_operation = kill.map(|process| KillBeforeOperation {
                    processes: vec![process.to_owned()],
                });
                request
            },
        )
}

/// A request through `manager` for `operation` with a placeholder package and no opt-in.
fn request_through(operation: &str, manager: &str) -> Request {
    Request {
        operation: Operation::from_name(operation).expect("generator operation"),
        manager: Manager::from_name(manager).expect("generator manager"),
        source: Source { name: String::new() },
        package: Package {
            id: String::new(),
            version: None,
            architecture: None,
        },
        scope: None,
        elevation: Elevation::NonElevated,
        opt_ins: OptIns::default(),
    }
}

/// Reorders the entries of every list with a deterministic generator seeded by `seed`.
pub(crate) struct Shuffler(u64);

impl Shuffler {
    pub(crate) fn new(seed: u64) -> Self {
        Self(seed | 1)
    }

    fn next(&mut self) -> u64 {
        // xorshift64
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 7;
        self.0 ^= self.0 << 17;
        self.0
    }

    pub(crate) fn shuffle<T>(&mut self, items: &mut [T]) {
        for idx in (1..items.len()).rev() {
            let bound = u64::try_from(idx + 1).expect("small list");
            let other = usize::try_from(self.next() % bound).expect("small index");
            items.swap(idx, other);
        }
    }
}

impl GenPolicy {
    /// Writes the policy as a document, with the ids `r0`, `r1` and so on in rule order unless `ids` gives them.
    pub(crate) fn to_json(&self, ids: &[String]) -> String {
        let mut out = String::from(
            r#"{"PolicyFormatVersion":"2.0.0","Metadata":{"Id":"generated","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z""#,
        );
        if let Some(valid_from) = self.valid_from {
            let _ = write!(out, r#","ValidFrom":"{valid_from}""#);
        }
        if let Some(valid_until) = self.valid_until {
            let _ = write!(out, r#","ValidUntil":"{valid_until}""#);
        }
        out.push('}');
        if let Some(audit_mode) = self.audit_mode {
            let _ = write!(out, r#","Enforcement":{{"AuditMode":{audit_mode}}}"#);
        }
        out.push_str(r#","TrustedSources":["#);
        write_list(&mut out, &self.trusted_sources, write_selector);
        out.push_str(r#"],"Rules":["#);
        for (idx, rule) in self.rules.iter().enumerate() {
            if idx != 0 {
                out.push(',');
            }
            write_rule(&mut out, &ids[idx], rule);
        }
        out.push_str("]}");
        out
    }

    pub(crate) fn default_ids(&self) -> Vec<String> {
        (0..self.rules.len()).map(|idx| format!("r{idx}")).collect()
    }

    pub(crate) fn parse(&self, ids: &[String]) -> Policy {
        let text = self.to_json(ids);
        match parse_policy(text.as_bytes()) {
            Ok(policy) => policy,
            Err(errors) => panic!("generated policy is invalid: {errors:?}\n{text}"),
        }
    }

    /// Returns a copy with every list, rule list included, in another order chosen by `shuffler`.
    pub(crate) fn reordered(&self, ids: &[String], shuffler: &mut Shuffler) -> (GenPolicy, Vec<String>) {
        let mut policy = self.clone();
        shuffler.shuffle(&mut policy.trusted_sources);

        let mut rules: Vec<(GenRule, String)> = policy.rules.into_iter().zip(ids.iter().cloned()).collect();
        shuffler.shuffle(&mut rules);
        for (rule, _) in &mut rules {
            reorder_selection(&mut rule.select, shuffler);
            for except in &mut rule.except {
                reorder_selection(except, shuffler);
            }
            shuffler.shuffle(&mut rule.except);
            for opt_in in &mut rule.opt_ins {
                shuffler.shuffle(&mut opt_in.patterns);
                shuffler.shuffle(&mut opt_in.except);
            }
            shuffler.shuffle(&mut rule.opt_ins);
        }

        let (rules, ids) = rules.into_iter().unzip();
        policy.rules = rules;
        (policy, ids)
    }
}

fn reorder_selection(selection: &mut GenSelection, shuffler: &mut Shuffler) {
    if let Some(operations) = &mut selection.operations {
        shuffler.shuffle(operations);
    }
    if let Some(sources) = &mut selection.sources {
        shuffler.shuffle(sources);
    }
    if let Some(packages) = &mut selection.packages {
        shuffler.shuffle(packages);
    }
    if let Some(scopes) = &mut selection.scopes {
        shuffler.shuffle(scopes);
    }
    if let Some(architectures) = &mut selection.architectures {
        shuffler.shuffle(architectures);
    }
}

fn write_list<T>(out: &mut String, items: &[T], write: fn(&mut String, &T)) {
    for (idx, item) in items.iter().enumerate() {
        if idx != 0 {
            out.push(',');
        }
        write(out, item);
    }
}

fn write_string(out: &mut String, text: &&'static str) {
    out.push('"');
    for c in text.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            c => out.push(c),
        }
    }
    out.push('"');
}

fn write_selector(out: &mut String, selector: &GenSelector) {
    let _ = write!(out, r#"{{"Manager":"{}""#, selector.manager);
    match selector.source {
        Some(source) => {
            out.push_str(r#","Source":"#);
            write_string(out, &source);
        }
        None => out.push_str(r#","AnySource":true"#),
    }
    out.push('}');
}

fn write_selection(out: &mut String, selection: &GenSelection) {
    let mut members: Vec<String> = Vec::new();
    let mut strings = |key: &str, values: &Option<Vec<&'static str>>| {
        if let Some(values) = values {
            let mut member = format!(r#""{key}":["#);
            write_list(&mut member, values, write_string);
            member.push(']');
            members.push(member);
        }
    };
    strings("Operations", &selection.operations);
    strings("Packages", &selection.packages);
    strings("Scopes", &selection.scopes);
    strings("Architectures", &selection.architectures);
    if let Some(sources) = &selection.sources {
        let mut member = String::from(r#""Sources":["#);
        write_list(&mut member, sources, write_selector);
        member.push(']');
        members.push(member);
    }
    if let Some(version) = &selection.version {
        let mut fields = Vec::new();
        if let Some(minimum) = version.minimum {
            fields.push(format!(r#""Minimum":"{minimum}""#));
        }
        if let Some(maximum) = version.maximum {
            fields.push(format!(r#""Maximum":"{maximum}""#));
        }
        if version.exclude_prerelease {
            fields.push(r#""Prerelease":"Exclude""#.to_owned());
        }
        members.push(format!(r#""Version":{{{}}}"#, fields.join(",")));
    }
    if let Some(elevation) = selection.elevation {
        members.push(format!(r#""Elevation":"{elevation}""#));
    }
    let _ = write!(out, "{{{}}}", members.join(","));
}

fn write_rule(out: &mut String, id: &str, rule: &GenRule) {
    let decision = if rule.deny { "Deny" } else { "Allow" };
    let _ = write!(out, r#"{{"Id":"{id}","Decision":"{decision}""#);
    if let Some(enabled) = rule.enabled {
        let _ = write!(out, r#","Enabled":{enabled}"#);
    }
    if let Some(reason) = rule.reason {
        let _ = write!(out, r#","Reason":"{reason}""#);
    }
    out.push_str(r#","Select":"#);
    write_selection(out, &rule.select);
    if !rule.except.is_empty() {
        out.push_str(r#","Except":["#);
        write_list(out, &rule.except, write_selection);
        out.push(']');
    }
    if !rule.opt_ins.is_empty() {
        let key = if rule.deny { "Forbids" } else { "Grants" };
        let _ = write!(out, r#","{key}":{{"#);
        for (idx, opt_in) in rule.opt_ins.iter().enumerate() {
            if idx != 0 {
                out.push(',');
            }
            let _ = write!(out, r#""{}":{{"#, opt_in.name);
            if !opt_in.patterns.is_empty() {
                out.push_str(r#""Patterns":["#);
                write_list(out, &opt_in.patterns, write_string);
                out.push(']');
                if !opt_in.except.is_empty() {
                    out.push_str(r#","Except":["#);
                    write_list(out, &opt_in.except, write_string);
                    out.push(']');
                }
            }
            out.push('}');
        }
        out.push('}');
    }
    out.push('}');
}
