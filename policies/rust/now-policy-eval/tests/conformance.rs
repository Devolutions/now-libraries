//! Runs the language-neutral policy conformance suite in `policies/test-data/policy-conformance`.
//!
//! The case format is defined by that directory's `README.md`.
//! Every failing case is reported by its `name`.
//! The test fails when `documents` or `evaluation` is missing, unreadable or has no case, and when a file or case carries a key the README does not define.

use core::fmt::Write as _;
use std::collections::{BTreeSet, HashMap};
use std::path::{Path, PathBuf};

use now_policy_eval::{Decision, Policy, Request, Timestamp, evaluate, parse_policy};
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
        let mut report = format!("{} of {count} conformance cases failed:\n", failures.len());
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

    let actual = decision_value(&evaluate(&policy, &request, time));
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

/// Reads the suite's request object into a [`Request`], rejecting keys and types the format does not define.
fn read_request(value: &Value) -> Result<Request, String> {
    let object = fields(
        value,
        "request",
        &["Operation", "Manager", "Source", "Package", "Options", "Client"],
    )?;

    let mut request = Request {
        operation: required_string(object, "Operation")?,
        manager: required_string(object, "Manager")?,
        ..Request::default()
    };

    let source = fields(
        object.get("Source").ok_or("the request has no `Source`")?,
        "Source",
        &["Name", "Url"],
    )?;
    request.source.name = required_string(source, "Name")?;
    request.source.url = optional_string(source, "Url")?;

    let package = fields(
        object.get("Package").ok_or("the request has no `Package`")?,
        "Package",
        &["Id", "Version", "Architecture", "Channel"],
    )?;
    request.package.id = required_string(package, "Id")?;
    request.package.version = optional_string(package, "Version")?;
    request.package.architecture = optional_string(package, "Architecture")?;
    request.package.channel = optional_string(package, "Channel")?;

    if let Some(options) = object.get("Options") {
        let options = fields(
            options,
            "Options",
            &[
                "Scope",
                "Interactive",
                "SkipHashCheck",
                "PreRelease",
                "UninstallPrevious",
                "NoUpgrade",
                "CustomInstallLocation",
                "CustomParameters",
                "PreOperationCommand",
                "PostOperationCommand",
                "KillBeforeOperation",
            ],
        )?;
        let target = &mut request.options;
        target.scope = optional_string(options, "Scope")?;
        target.interactive = boolean(options, "Interactive")?;
        target.skip_hash_check = boolean(options, "SkipHashCheck")?;
        target.pre_release = boolean(options, "PreRelease")?;
        target.uninstall_previous = boolean(options, "UninstallPrevious")?;
        target.no_upgrade = boolean(options, "NoUpgrade")?;
        target.custom_install_location = optional_string(options, "CustomInstallLocation")?;
        target.custom_parameters = string_list(options, "CustomParameters")?;
        target.pre_operation_command = optional_string(options, "PreOperationCommand")?;
        target.post_operation_command = optional_string(options, "PostOperationCommand")?;
        target.kill_before_operation = string_list(options, "KillBeforeOperation")?;
    }

    let client = fields(
        object.get("Client").ok_or("the request has no `Client`")?,
        "Client",
        &["RequestedElevation"],
    )?;
    request.client.requested_elevation = required_string(client, "RequestedElevation")?;

    Ok(request)
}

fn fields<'a>(value: &'a Value, part: &str, known: &[&str]) -> Result<&'a serde_json::Map<String, Value>, String> {
    let object = value.as_object().ok_or_else(|| format!("`{part}` is not an object"))?;
    for key in object.keys() {
        if !known.contains(&key.as_str()) {
            return Err(format!("unknown key `{key}` in `{part}`"));
        }
    }
    Ok(object)
}

fn required_string(object: &serde_json::Map<String, Value>, key: &str) -> Result<String, String> {
    optional_string(object, key)?.ok_or_else(|| format!("no `{key}`"))
}

fn optional_string(object: &serde_json::Map<String, Value>, key: &str) -> Result<Option<String>, String> {
    match object.get(key) {
        None => Ok(None),
        Some(Value::String(text)) => Ok(Some(text.clone())),
        Some(other) => Err(format!("`{key}` is not a string: {other}")),
    }
}

fn boolean(object: &serde_json::Map<String, Value>, key: &str) -> Result<bool, String> {
    match object.get(key) {
        None => Ok(false),
        Some(Value::Bool(flag)) => Ok(*flag),
        Some(other) => Err(format!("`{key}` is not a boolean: {other}")),
    }
}

fn string_list(object: &serde_json::Map<String, Value>, key: &str) -> Result<Vec<String>, String> {
    match object.get(key) {
        None => Ok(Vec::new()),
        Some(Value::Array(elements)) => elements
            .iter()
            .map(|element| {
                element
                    .as_str()
                    .map(str::to_owned)
                    .ok_or_else(|| format!("`{key}` has an element that is not a string: {element}"))
            })
            .collect(),
        Some(other) => Err(format!("`{key}` is not a list: {other}")),
    }
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
