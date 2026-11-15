//! Runs the language-neutral policy conformance suite in `policies/test-data/policy-conformance`.
//!
//! The case format is defined by that directory's `README.md`.
//! Every failing case is reported by its `name`.
//! The test fails when `documents` or `evaluation` is missing, unreadable or has no case, and when a file or case carries a key the README does not define.

use core::fmt::Write as _;
use std::collections::{BTreeSet, HashMap};
use std::path::{Path, PathBuf};

use now_policy_eval::{
    Architecture, CustomInstallLocation, CustomParameters, Decision, Elevation, InstallerArguments,
    InstallerArgumentsMode, KillBeforeOperation, Manager, Operation, OptIns, Package, Policy, PrePostCommands, Request,
    Scope, Source, Timestamp, evaluate, parse_policy,
};
use serde_json::Value;
use serde_json::value::RawValue;
use {proptest as _, rstest as _};

type RawObject = HashMap<String, Box<RawValue>>;

/// The keys every case may carry.
const COMMON_CASE_KEYS: [&str; 3] = ["name", "oracle", "note"];

/// One directory of the suite and what its files and cases may contain.
struct Kind {
    dir: &'static str,
    file_keys: &'static [&'static str],
    /// The keys a case may carry besides the common ones.
    case_keys: &'static [&'static str],
    run_case: fn(&RawObject, &RawObject) -> Result<(), String>,
}

const KINDS: [Kind; 2] = [
    Kind {
        dir: "documents",
        file_keys: &["cases"],
        case_keys: &["document", "text", "base64", "padTo", "expect"],
        run_case: run_document_case,
    },
    Kind {
        dir: "evaluation",
        file_keys: &["cases", "policy", "time"],
        case_keys: &["policy", "time", "request", "expect"],
        run_case: run_evaluation_case,
    },
];

#[test]
fn conformance_suite() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../test-data/policy-conformance");

    let mut names = BTreeSet::new();
    let mut failures = Vec::new();
    let mut count = 0;

    for kind in &KINDS {
        let dir = root.join(kind.dir);
        let mut kind_count = 0;

        match json_files(&dir) {
            Ok(paths) => {
                for path in paths {
                    kind_count += run_file(kind, &path, &mut names, &mut failures);
                }
            }
            Err(error) => failures.push(format!("{}: {error}", dir.display())),
        }

        if kind_count == 0 {
            failures.push(format!("{}: the suite has no case", dir.display()));
        }
        count += kind_count;
    }

    if !failures.is_empty() {
        let mut report = format!("{} conformance failures ({count} cases run):\n", failures.len());
        for failure in &failures {
            let _ = writeln!(report, "- {failure}");
        }
        panic!("{report}");
    }
}

/// Runs every case of the file at `path`, recording failures, and returns the number of cases it holds.
fn run_file(kind: &Kind, path: &Path, names: &mut BTreeSet<String>, failures: &mut Vec<String>) -> usize {
    let file_name = path.display().to_string();
    let file = match read_file(path) {
        Ok(file) => file,
        Err(error) => {
            failures.push(format!("{file_name}: {error}"));
            return 0;
        }
    };

    if let Err(error) = reject_unknown_keys(&file, "the file", kind.file_keys) {
        failures.push(format!("{file_name}: {error}"));
    }

    let cases = match file
        .get("cases")
        .map(|cases| serde_json::from_str::<Vec<RawObject>>(cases.get()))
    {
        Some(Ok(cases)) => cases,
        Some(Err(error)) => {
            failures.push(format!("{file_name}: `cases` is not a list of objects: {error}"));
            return 0;
        }
        None => {
            failures.push(format!("{file_name}: no `cases`"));
            return 0;
        }
    };

    for (idx, case) in cases.iter().enumerate() {
        let name = match string_field(case, "name") {
            Ok(Some(name)) => name,
            _ => format!("{file_name} case {idx} (no name)"),
        };

        let result = reject_unknown_keys(case, "the case", &[&COMMON_CASE_KEYS[..], kind.case_keys].concat())
            .and_then(|()| check_common_fields(case, names))
            .and_then(|()| (kind.run_case)(&file, case));
        if let Err(error) = result {
            failures.push(format!("{name}: {error}"));
        }
    }

    cases.len()
}

/// Returns the `*.json` files of `dir`, sorted, or an error when `dir` cannot be listed.
fn json_files(dir: &Path) -> Result<Vec<PathBuf>, String> {
    let entries = std::fs::read_dir(dir).map_err(|error| format!("cannot list: {error}"))?;

    let mut files = Vec::new();
    for entry in entries {
        let path = entry.map_err(|error| format!("cannot list: {error}"))?.path();
        if path.extension().is_some_and(|extension| extension == "json") {
            files.push(path);
        }
    }
    files.sort();
    Ok(files)
}

/// Fails on the first key of `object`, in sorted order, that the README does not define for `part`.
fn reject_unknown_keys(object: &RawObject, part: &str, known: &[&str]) -> Result<(), String> {
    let mut unknown: Vec<&String> = object.keys().filter(|key| !known.contains(&key.as_str())).collect();
    unknown.sort();
    match unknown.first() {
        Some(key) => Err(format!("unknown key `{key}` in {part}")),
        None => Ok(()),
    }
}

fn read_file(path: &Path) -> Result<RawObject, String> {
    let text = std::fs::read_to_string(path).map_err(|error| format!("cannot read: {error}"))?;
    serde_json::from_str(&text).map_err(|error| format!("not a JSON object: {error}"))
}

fn check_common_fields(case: &RawObject, names: &mut BTreeSet<String>) -> Result<(), String> {
    let name = string_field(case, "name")?.ok_or("no `name`")?;
    if !names.insert(name.clone()) {
        return Err(format!("the name `{name}` is not unique across the suite"));
    }
    string_field(case, "oracle")?.ok_or("no `oracle`")?;
    Ok(())
}

fn run_document_case(_file: &RawObject, case: &RawObject) -> Result<(), String> {
    let inputs: Vec<&str> = ["document", "text", "base64"]
        .into_iter()
        .filter(|key| case.contains_key(*key))
        .collect();

    let mut bytes = match inputs.as_slice() {
        ["document"] => case["document"].get().as_bytes().to_vec(),
        ["text"] => string_field(case, "text")?.unwrap_or_default().into_bytes(),
        ["base64"] => decode_base64(&string_field(case, "base64")?.unwrap_or_default())?,
        _ => return Err("needs exactly one of `document`, `text` and `base64`".to_owned()),
    };

    if let Some(pad_to) = case.get("padTo") {
        let pad_to: usize =
            serde_json::from_str(pad_to.get()).map_err(|error| format!("`padTo` is not a byte count: {error}"))?;
        if bytes.len() < pad_to {
            bytes.resize(pad_to, b' ');
        }
    }

    let actual: BTreeSet<(String, String)> = match parse_policy(&bytes) {
        Ok(_) => BTreeSet::new(),
        Err(errors) => errors
            .into_iter()
            .map(|error| (error.code.as_str().to_owned(), error.pointer))
            .collect(),
    };

    let expect = value_field(case, "expect")?.ok_or("no `expect`")?;
    let expect = expect.as_object().ok_or("`expect` is not an object")?;

    if expect.get("valid") == Some(&Value::Bool(true)) && expect.len() == 1 {
        return if actual.is_empty() {
            Ok(())
        } else {
            Err(format!("expected a valid document, got {actual:?}"))
        };
    }

    if let (Some(errors), 1) = (expect.get("errors"), expect.len()) {
        let expected = parse_errors(errors)?;
        return if actual == expected {
            Ok(())
        } else {
            Err(format!("expected exactly {expected:?}, got {actual:?}"))
        };
    }

    if let (Some(errors), 1) = (expect.get("someOf"), expect.len()) {
        let allowed = parse_errors(errors)?;
        return if !actual.is_empty() && actual.is_subset(&allowed) {
            Ok(())
        } else {
            Err(format!("expected a non-empty subset of {allowed:?}, got {actual:?}"))
        };
    }

    Err("`expect` is not one of `valid`, `errors` and `someOf`".to_owned())
}

fn parse_errors(errors: &Value) -> Result<BTreeSet<(String, String)>, String> {
    let errors = errors.as_array().ok_or("the expected errors are not a list")?;
    errors
        .iter()
        .map(|error| {
            let code = error.get("code").and_then(Value::as_str);
            let pointer = error.get("pointer").and_then(Value::as_str);
            match (code, pointer, error.as_object().map(serde_json::Map::len)) {
                (Some(code), Some(pointer), Some(2)) => Ok((code.to_owned(), pointer.to_owned())),
                _ => Err(format!("invalid expected error: {error}")),
            }
        })
        .collect()
}

fn run_evaluation_case(file: &RawObject, case: &RawObject) -> Result<(), String> {
    let policy_text = case
        .get("policy")
        .or_else(|| file.get("policy"))
        .ok_or("no `policy` in the case or its file")?;
    let policy: Policy =
        parse_policy(policy_text.get().as_bytes()).map_err(|errors| format!("the policy is invalid: {errors:?}"))?;

    let time = match string_field(case, "time")? {
        Some(time) => time,
        None => string_field(file, "time")?.ok_or("no `time` in the case or its file")?,
    };
    let time: Timestamp = time.parse().map_err(|_| format!("invalid time `{time}`"))?;

    let request = value_field(case, "request")?.ok_or("no `request`")?;
    let request = read_request(&request)?;

    let expect = value_field(case, "expect")?.ok_or("no `expect`")?;
    let expected = read_decision(&expect)?;

    let decision =
        evaluate(&policy, &request, time).map_err(|error| format!("{error}: the suite has no error case"))?;
    let actual = decision_value(&decision);
    if actual == expected {
        Ok(())
    } else {
        Err(format!("expected {expected}, got {actual}"))
    }
}

/// Converts a decision to the suite's JSON form.
fn decision_value(decision: &Decision) -> Value {
    let rules: Vec<Value> = decision
        .rules
        .iter()
        .map(|rule| {
            let mut object = serde_json::Map::new();
            object.insert("id".to_owned(), Value::from(rule.id.as_str()));
            if let Some(reason) = &rule.reason {
                object.insert("reason".to_owned(), Value::from(reason.as_str()));
            }
            Value::Object(object)
        })
        .collect();

    serde_json::json!({
        "outcome": decision.outcome.as_str(),
        "check": decision.check.as_str(),
        "rules": rules,
        "optIns": decision.opt_ins.iter().map(|opt_in| opt_in.as_str()).collect::<Vec<_>>(),
        "audited": decision.audited,
    })
}

fn read_decision(expect: &Value) -> Result<Value, String> {
    let object = expect.as_object().ok_or("`expect` is not an object")?;
    for key in object.keys() {
        if !["outcome", "check", "rules", "optIns", "audited"].contains(&key.as_str()) {
            return Err(format!("unknown decision key `{key}`"));
        }
    }
    for key in ["outcome", "check", "rules", "optIns", "audited"] {
        if !object.contains_key(key) {
            return Err(format!("the decision has no `{key}`"));
        }
    }
    Ok(expect.clone())
}

type JsonObject = serde_json::Map<String, Value>;

/// Reads the suite's request object into a [`Request`] as the README defines it.
///
/// A request that is not well-formed fails the case: an unknown or missing member, a value outside its domain, or `Scope` `Machine` with `Elevation` `NonElevated`.
fn read_request(value: &Value) -> Result<Request, String> {
    let object = fields(
        value,
        "request",
        &[
            "Operation",
            "Manager",
            "Source",
            "Package",
            "Scope",
            "Elevation",
            "OptIns",
        ],
    )?;

    let source = fields(required(object, "Source")?, "Source", &["Name"])?;
    let package = fields(
        required(object, "Package")?,
        "Package",
        &["Id", "Version", "Architecture"],
    )?;

    let id = bounded_string(package, "Id", 1, 256)?;
    if let Some(character) = id
        .chars()
        .find(|c| !(c.is_ascii_alphanumeric() || ".+_@/:[],#$%{}-".contains(*c)))
    {
        return Err(format!(
            "`Id` has the character `{character}`, which the contract does not allow"
        ));
    }

    let scope = optional_enum(object, "Scope", Scope::from_name)?;
    let elevation = enum_value(object, "Elevation", Elevation::from_name)?;
    if scope == Some(Scope::Machine) && elevation == Elevation::NonElevated {
        return Err("`Scope` `Machine` contradicts `Elevation` `NonElevated`".to_owned());
    }

    Ok(Request {
        operation: enum_value(object, "Operation", Operation::from_name)?,
        manager: enum_value(object, "Manager", Manager::from_name)?,
        source: Source {
            name: bounded_string(source, "Name", 1, 128)?,
        },
        package: Package {
            id,
            version: optional_bounded_string(package, "Version", 1, 128)?,
            architecture: optional_enum(package, "Architecture", Architecture::from_name)?,
        },
        scope,
        elevation,
        opt_ins: match object.get("OptIns") {
            Some(opt_ins) => read_opt_ins(opt_ins)?,
            None => OptIns::default(),
        },
    })
}

fn read_opt_ins(value: &Value) -> Result<OptIns, String> {
    let object = fields(
        value,
        "OptIns",
        &[
            "Interactive",
            "SkipHashCheck",
            "CustomInstallLocation",
            "CustomParameters",
            "InstallerArguments",
            "PrePostCommands",
            "KillBeforeOperation",
            "UninstallPrevious",
            "NoUpgrade",
        ],
    )?;

    // A key without a value is present as `{}`.
    let present = |key: &str| -> Result<bool, String> {
        match object.get(key) {
            None => Ok(false),
            Some(value) => fields(value, key, &[]).map(|_| true),
        }
    };
    let valued = |key: &str, known: &[&str]| object.get(key).map(|value| fields(value, key, known)).transpose();

    let mut opt_ins = OptIns {
        interactive: present("Interactive")?,
        skip_hash_check: present("SkipHashCheck")?,
        uninstall_previous: present("UninstallPrevious")?,
        no_upgrade: present("NoUpgrade")?,
        ..OptIns::default()
    };

    if let Some(location) = valued("CustomInstallLocation", &["Path"])? {
        opt_ins.custom_install_location = Some(CustomInstallLocation {
            path: bounded_string(location, "Path", 1, 2048)?,
        });
    }
    if let Some(parameters) = valued("CustomParameters", &["Values"])? {
        opt_ins.custom_parameters = Some(CustomParameters {
            values: bounded_list(parameters, "Values", 64, 512, |_| true)?,
        });
    }
    if let Some(arguments) = valued("InstallerArguments", &["Arguments", "Mode"])? {
        opt_ins.installer_arguments = Some(InstallerArguments {
            arguments: bounded_string(arguments, "Arguments", 1, 2048)?,
            mode: enum_value(arguments, "Mode", InstallerArgumentsMode::from_name)?,
        });
    }
    if let Some(commands) = valued("PrePostCommands", &["Pre", "Post"])? {
        let pre = optional_bounded_string(commands, "Pre", 1, 2048)?;
        let post = optional_bounded_string(commands, "Post", 1, 2048)?;
        if pre.is_none() && post.is_none() {
            return Err("`PrePostCommands` has neither `Pre` nor `Post`".to_owned());
        }
        opt_ins.pre_post_commands = Some(PrePostCommands { pre, post });
    }
    if let Some(kill) = valued("KillBeforeOperation", &["Processes"])? {
        opt_ins.kill_before_operation = Some(KillBeforeOperation {
            processes: bounded_list(kill, "Processes", 64, 128, |process| !process.contains(['*', '?']))?,
        });
    }

    Ok(opt_ins)
}

fn fields<'a>(value: &'a Value, part: &str, known: &[&str]) -> Result<&'a JsonObject, String> {
    let object = value.as_object().ok_or_else(|| format!("`{part}` is not an object"))?;
    for key in object.keys() {
        if !known.contains(&key.as_str()) {
            return Err(format!("unknown key `{key}` in `{part}`"));
        }
    }
    Ok(object)
}

fn required<'a>(object: &'a JsonObject, key: &str) -> Result<&'a Value, String> {
    object.get(key).ok_or_else(|| format!("no `{key}`"))
}

fn optional_bounded_string(object: &JsonObject, key: &str, min: usize, max: usize) -> Result<Option<String>, String> {
    match object.get(key) {
        None => Ok(None),
        Some(Value::String(text)) if (min..=max).contains(&text.len()) => Ok(Some(text.clone())),
        Some(other) => Err(format!("`{key}` is not a string of {min} to {max} bytes: {other}")),
    }
}

fn bounded_string(object: &JsonObject, key: &str, min: usize, max: usize) -> Result<String, String> {
    optional_bounded_string(object, key, min, max)?.ok_or_else(|| format!("no `{key}`"))
}

/// Reads a list of 1 to `max_count` strings of 1 to `max_len` bytes that all satisfy `allowed`.
fn bounded_list(
    object: &JsonObject,
    key: &str,
    max_count: usize,
    max_len: usize,
    allowed: impl Fn(&str) -> bool,
) -> Result<Vec<String>, String> {
    let elements = match required(object, key)? {
        Value::Array(elements) if (1..=max_count).contains(&elements.len()) => elements,
        other => return Err(format!("`{key}` is not a list of 1 to {max_count} strings: {other}")),
    };

    elements
        .iter()
        .map(|element| match element {
            Value::String(text) if (1..=max_len).contains(&text.len()) && allowed(text) => Ok(text.clone()),
            other => Err(format!("`{key}` has an element outside its domain: {other}")),
        })
        .collect()
}

fn optional_enum<T>(object: &JsonObject, key: &str, from_name: fn(&str) -> Option<T>) -> Result<Option<T>, String> {
    match object.get(key) {
        None => Ok(None),
        Some(Value::String(name)) => from_name(name)
            .map(Some)
            .ok_or_else(|| format!("`{key}` has the value `{name}`, which the contract does not define")),
        Some(other) => Err(format!("`{key}` is not a string: {other}")),
    }
}

fn enum_value<T>(object: &JsonObject, key: &str, from_name: fn(&str) -> Option<T>) -> Result<T, String> {
    optional_enum(object, key, from_name)?.ok_or_else(|| format!("no `{key}`"))
}

fn value_field(object: &RawObject, key: &str) -> Result<Option<Value>, String> {
    object
        .get(key)
        .map(|raw| serde_json::from_str(raw.get()).map_err(|error| format!("`{key}` is not JSON: {error}")))
        .transpose()
}

fn string_field(object: &RawObject, key: &str) -> Result<Option<String>, String> {
    match value_field(object, key)? {
        None => Ok(None),
        Some(Value::String(text)) => Ok(Some(text)),
        Some(other) => Err(format!("`{key}` is not a string: {other}")),
    }
}

/// Decodes standard base64 with padding, as RFC 4648 section 4 defines it.
fn decode_base64(text: &str) -> Result<Vec<u8>, String> {
    let invalid = || format!("invalid base64 `{text}`");

    if !text.len().is_multiple_of(4) {
        return Err(invalid());
    }

    let mut bytes = Vec::with_capacity(text.len() / 4 * 3);
    let chunk_count = text.len() / 4;
    for (idx, chunk) in text.as_bytes().chunks(4).enumerate() {
        let padding = chunk.iter().rev().take_while(|b| **b == b'=').count();
        if 2 < padding || (padding != 0 && idx + 1 != chunk_count) {
            return Err(invalid());
        }

        let mut group: u32 = 0;
        for b in &chunk[..4 - padding] {
            let sextet = match b {
                b'A'..=b'Z' => b - b'A',
                b'a'..=b'z' => b - b'a' + 26,
                b'0'..=b'9' => b - b'0' + 52,
                b'+' => 62,
                b'/' => 63,
                _ => return Err(invalid()),
            };
            group = (group << 6) | u32::from(sextet);
        }
        group <<= 6 * padding;

        let decoded = group.to_be_bytes();
        bytes.extend_from_slice(&decoded[1..4 - padding]);
    }

    Ok(bytes)
}

/// A request that is not well-formed gets no decision, so a runner reports it and never expects `Deny` of it.
#[test]
fn request_that_is_not_well_formed_is_reported_not_denied() {
    // oracle: contract policies/CONTRACT.policy.md §error
    let case: RawObject = serde_json::from_str(
        r#"{
            "policy": {
                "PolicyFormatVersion": "2.0.0",
                "Metadata": {"Id": "t", "Publisher": "t", "Revision": 1, "PublishedAt": "2026-01-01T00:00:00Z"},
                "TrustedSources": [],
                "Rules": []
            },
            "time": "2026-06-01T00:00:00Z",
            "request": {
                "Operation": "Install",
                "Manager": "Winget",
                "Source": {"Name": "winget"},
                "Package": {"Id": "Contoso.Tool", "Version": "1.2.3"},
                "Scope": "Machine",
                "Elevation": "NonElevated"
            },
            "expect": {"outcome": "Deny", "check": "UnreadableRequest", "rules": [], "optIns": [], "audited": false}
        }"#,
    )
    .expect("test case is JSON");

    let result = run_evaluation_case(&RawObject::new(), &case);

    assert!(
        result.is_err_and(|error| error.contains("contradicts")),
        "the case must fail as a request that is not well-formed, not run as a decision"
    );
}
