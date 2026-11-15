//! Conformance of the broker grant success response, checked at the JSON seam.
//!
//! Every expected value comes from the response section of the Gateway package-management
//! contract, pinned at commit c79e7bd775370b061b5939d0ac8442c7215de869.
//! Each negative case is the valid control with exactly one named defect.
//!
//! Checked clauses:
//! - Required non-null members and their exact discriminators `ResponseKind` and `ResponseVersion`.
//! - `Server`: `ServerVersion` of 1 to 128 characters and the two `Transport` values.
//! - `ReceivedAt`, `CompletedAt` and `Operation.SubmittedAt` as RFC 3339 UTC timestamps.
//! - `Request` with optional nullable `Manager`, `Source`, `PackageId` and `Operation`.
//! - `Authorization` containing only `Kind: RemoteGrant`.
//! - `Operation` with the six statuses and an optional nullable `EventChannel`
//!   of `Kind: LocalPipe` and a `Path` of 1 to 1,024 characters.
//! - `Policy`, `Decision`, `RuleId` and `Diagnostics` forbidden, including as null.
//! - Unknown and duplicate members rejected at every object level.
//!
//! Intentional gaps, which this suite does not decide:
//! - Grant execution, grant routing and request validation.
//! - Server authentication of the pipe.
//! - Privacy of the event channel and the semantics of `Source`.
//! - The `ErrorResponse` shape and HTTP status handling.
//! - A zero-output event stream, which is not part of the response.
//! - Any bound on `PackageId` or `OperationId`, which the contract does not state.

#![allow(clippy::type_complexity, unused_crate_dependencies)]

use now_policy_api::GrantExecutionResponse;
use serde_json::{Value, json};

const CONTRACT: &str = "Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response";

fn control() -> Value {
    json!({
        "ResponseKind": "GrantExecutionResponse",
        "ResponseVersion": "1.0",
        "Server": { "ServerVersion": "1.2.3", "Transport": "HttpNamedPipe" },
        "RequestId": "0b3e6f0e-5d58-4a0e-9d2c-1f4f1c6c8a11",
        "ReceivedAt": "2026-11-15T05:07:42Z",
        "CompletedAt": "2026-11-15T05:07:43Z",
        "Request": {
            "Manager": "Winget",
            "Source": "winget",
            "PackageId": "Microsoft.VisualStudioCode",
            "Operation": "Install"
        },
        "Authorization": { "Kind": "RemoteGrant" },
        "Operation": {
            "OperationId": "op-7f3a",
            "Status": "Running",
            "SubmittedAt": "2026-11-15T05:07:42Z",
            "EventChannel": { "Kind": "LocalPipe", "Path": "\\\\.\\pipe\\Devolutions.Now.PackageBroker.events.op-7f3a" }
        }
    })
}

fn without_channel() -> Value {
    let mut value = control();
    remove(&mut value, "/Operation/EventChannel");
    value
}

fn set(value: &mut Value, pointer: &str, new: Value) {
    *value
        .pointer_mut(pointer)
        .unwrap_or_else(|| panic!("control has no {pointer}")) = new;
}

fn remove(value: &mut Value, pointer: &str) {
    let (parent, key) = pointer.rsplit_once('/').expect("pointer has a parent");
    let parent = value
        .pointer_mut(parent)
        .unwrap_or_else(|| panic!("control has no {parent}"));
    parent
        .as_object_mut()
        .expect("parent is an object")
        .remove(key)
        .unwrap_or_else(|| panic!("control has no {pointer}"));
}

fn insert(value: &mut Value, pointer: &str, key: &str, new: Value) {
    let parent = value
        .pointer_mut(pointer)
        .unwrap_or_else(|| panic!("control has no {pointer}"));
    parent
        .as_object_mut()
        .expect("parent is an object")
        .insert(key.to_owned(), new);
}

fn parse(json: &str) -> Result<GrantExecutionResponse, serde_json::Error> {
    serde_json::from_str(json)
}

fn assert_valid(name: &str, value: &Value) {
    let json = value.to_string();
    let parsed =
        parse(&json).unwrap_or_else(|error| panic!("{name}: {CONTRACT} accepts this response: {error}\n{json}"));
    // oracle: property round-trip
    let reserialized = serde_json::to_value(&parsed).expect("serialize response");
    assert_eq!(
        &reserialized, value,
        "{name}: serialization must preserve the wire shape"
    );
}

fn assert_rejected_all(cases: Vec<(String, String)>) {
    let accepted: Vec<_> = cases
        .iter()
        .filter(|(_, json)| parse(json).is_ok())
        .map(|(name, _)| name.as_str())
        .collect();
    assert!(
        accepted.is_empty(),
        "{CONTRACT} rejects these, but they were accepted: {accepted:?}"
    );
}

fn mutated(cases: &[(&str, &dyn Fn(&mut Value))]) -> Vec<(String, String)> {
    cases
        .iter()
        .map(|(name, mutate)| {
            let mut value = control();
            mutate(&mut value);
            ((*name).to_owned(), value.to_string())
        })
        .collect()
}

#[test]
fn valid_responses_are_accepted() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    assert_valid("with event channel", &control());
    assert_valid("without event channel", &without_channel());

    let mut explicit_null_channel = control();
    set(&mut explicit_null_channel, "/Operation/EventChannel", Value::Null);
    parse(&explicit_null_channel.to_string()).expect("optional nullable EventChannel may be null");

    let mut empty_request = control();
    set(&mut empty_request, "/Request", json!({}));
    assert_valid("empty Request", &empty_request);

    let mut null_request_members = control();
    for member in ["Manager", "Source", "PackageId", "Operation"] {
        set(&mut null_request_members, &format!("/Request/{member}"), Value::Null);
    }
    parse(&null_request_members.to_string()).expect("optional nullable Request members may be null");
}

#[test]
fn enumerated_values_are_accepted() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    for transport in ["HttpNamedPipe", "HttpLoopbackSimulator"] {
        let mut value = control();
        set(&mut value, "/Server/Transport", json!(transport));
        assert_valid(transport, &value);
    }
    for status in ["Starting", "Running", "Canceling", "Completed", "Failed", "Canceled"] {
        let mut value = control();
        set(&mut value, "/Operation/Status", json!(status));
        assert_valid(status, &value);
    }
    for operation in ["Install", "Update", "Uninstall"] {
        let mut value = control();
        set(&mut value, "/Request/Operation", json!(operation));
        assert_valid(operation, &value);
    }
}

#[test]
fn field_bounds_are_inclusive() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    for version in ["1".to_owned(), "x".repeat(128), "\u{e9}".repeat(128)] {
        let mut value = control();
        set(&mut value, "/Server/ServerVersion", json!(version));
        assert_valid(
            &format!("ServerVersion of {} characters", version.chars().count()),
            &value,
        );
    }
    for path in ["p".to_owned(), "p".repeat(1024), "\u{e9}".repeat(1024)] {
        let mut value = control();
        set(&mut value, "/Operation/EventChannel/Path", json!(path));
        assert_valid(&format!("Path of {} characters", path.chars().count()), &value);
    }
}

#[test]
fn bounds_violations_are_rejected() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    assert_rejected_all(mutated(&[
        ("empty ServerVersion", &|v| set(v, "/Server/ServerVersion", json!(""))),
        ("129-character ServerVersion", &|v| {
            set(v, "/Server/ServerVersion", json!("x".repeat(129)))
        }),
        ("129 two-byte-character ServerVersion", &|v| {
            set(v, "/Server/ServerVersion", json!("\u{e9}".repeat(129)));
        }),
        ("empty Path", &|v| set(v, "/Operation/EventChannel/Path", json!(""))),
        ("1025-character Path", &|v| {
            set(v, "/Operation/EventChannel/Path", json!("p".repeat(1025)))
        }),
    ]));
}

#[test]
fn missing_and_null_required_members_are_rejected() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    let required = [
        "/ResponseKind",
        "/ResponseVersion",
        "/Server",
        "/Server/ServerVersion",
        "/Server/Transport",
        "/RequestId",
        "/ReceivedAt",
        "/CompletedAt",
        "/Request",
        "/Authorization",
        "/Authorization/Kind",
        "/Operation",
        "/Operation/OperationId",
        "/Operation/Status",
        "/Operation/SubmittedAt",
        "/Operation/EventChannel/Kind",
        "/Operation/EventChannel/Path",
    ];
    let mut cases = Vec::new();
    for pointer in required {
        let mut missing = control();
        remove(&mut missing, pointer);
        cases.push((format!("missing {pointer}"), missing.to_string()));

        let mut null = control();
        set(&mut null, pointer, Value::Null);
        cases.push((format!("null {pointer}"), null.to_string()));
    }
    assert_rejected_all(cases);
}

#[test]
fn discriminators_and_enumerations_are_exact() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    assert_rejected_all(mutated(&[
        ("ErrorResponse kind", &|v| {
            set(v, "/ResponseKind", json!("ErrorResponse"))
        }),
        ("lowercase kind", &|v| {
            set(v, "/ResponseKind", json!("grantexecutionresponse"))
        }),
        ("empty kind", &|v| set(v, "/ResponseKind", json!(""))),
        ("numeric kind", &|v| set(v, "/ResponseKind", json!(1))),
        ("version 1.1", &|v| set(v, "/ResponseVersion", json!("1.1"))),
        ("version 1", &|v| set(v, "/ResponseVersion", json!("1"))),
        ("version 2.0", &|v| set(v, "/ResponseVersion", json!("2.0"))),
        ("numeric version", &|v| set(v, "/ResponseVersion", json!(1.0))),
        ("local authorization", &|v| {
            set(v, "/Authorization/Kind", json!("LocalPolicy"))
        }),
        ("lowercase authorization", &|v| {
            set(v, "/Authorization/Kind", json!("remotegrant"))
        }),
        ("empty authorization", &|v| set(v, "/Authorization/Kind", json!(""))),
        ("unknown transport", &|v| set(v, "/Server/Transport", json!("Http"))),
        ("lowercase transport", &|v| {
            set(v, "/Server/Transport", json!("httpnamedpipe"))
        }),
        ("unknown status", &|v| set(v, "/Operation/Status", json!("Pending"))),
        ("lowercase status", &|v| set(v, "/Operation/Status", json!("running"))),
        ("numeric status", &|v| set(v, "/Operation/Status", json!(1))),
        ("unknown request operation", &|v| {
            set(v, "/Request/Operation", json!("Remove"))
        }),
        ("lowercase request operation", &|v| {
            set(v, "/Request/Operation", json!("install"))
        }),
        ("unknown manager", &|v| set(v, "/Request/Manager", json!("NotAManager"))),
        ("unknown channel kind", &|v| {
            set(v, "/Operation/EventChannel/Kind", json!("NamedPipe"))
        }),
        ("lowercase channel kind", &|v| {
            set(v, "/Operation/EventChannel/Kind", json!("localpipe"))
        }),
    ]));
}

#[test]
fn invalid_nested_values_are_rejected() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    assert_rejected_all(mutated(&[
        ("numeric ServerVersion", &|v| set(v, "/Server/ServerVersion", json!(1))),
        ("numeric RequestId", &|v| set(v, "/RequestId", json!(1))),
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §operations
        // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
        ("non-UUID RequestId", &|v| set(v, "/RequestId", json!("not-a-uuid"))),
        ("empty RequestId", &|v| set(v, "/RequestId", json!(""))),
        ("numeric OperationId", &|v| set(v, "/Operation/OperationId", json!(1))),
        ("numeric Source", &|v| set(v, "/Request/Source", json!(1))),
        ("numeric PackageId", &|v| set(v, "/Request/PackageId", json!(1))),
        ("numeric Path", &|v| set(v, "/Operation/EventChannel/Path", json!(1))),
        ("array Request", &|v| set(v, "/Request", json!([]))),
        ("string Server", &|v| set(v, "/Server", json!("HttpNamedPipe"))),
        ("string Authorization", &|v| {
            set(v, "/Authorization", json!("RemoteGrant"))
        }),
        ("string EventChannel", &|v| {
            set(v, "/Operation/EventChannel", json!("LocalPipe"))
        }),
        ("array Operation", &|v| set(v, "/Operation", json!([]))),
    ]));
}

#[test]
fn timestamps_must_be_rfc3339_utc() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    let invalid = [
        ("word", json!("yesterday")),
        ("empty", json!("")),
        ("number", json!(1_763_182_062)),
        ("space separator", json!("2026-11-15 05:07:42Z")),
        ("no offset", json!("2026-11-15T05:07:42")),
        ("non-UTC offset", json!("2026-11-15T14:07:42+09:00")),
        ("impossible date", json!("2026-02-30T05:07:42Z")),
    ];
    let mut cases = Vec::new();
    for pointer in ["/ReceivedAt", "/CompletedAt", "/Operation/SubmittedAt"] {
        for (name, bad) in &invalid {
            let mut value = control();
            set(&mut value, pointer, bad.clone());
            cases.push((format!("{name} {pointer}"), value.to_string()));
        }
    }
    assert_rejected_all(cases);
}

#[test]
fn forbidden_members_are_rejected_even_as_null() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    let mut cases = Vec::new();
    for member in ["Policy", "Decision", "RuleId", "Diagnostics"] {
        for (shape, forbidden) in [("null", Value::Null), ("value", json!("Allow")), ("object", json!({}))] {
            for (variant, base) in [("with channel", control()), ("without channel", without_channel())] {
                let mut value = base;
                insert(&mut value, "", member, forbidden.clone());
                cases.push((format!("{shape} {member} {variant}"), value.to_string()));
            }
        }
    }
    assert_rejected_all(cases);
}

#[test]
fn unknown_members_are_rejected_at_every_object_level() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    let extras = [
        ("Extra", json!(true)),
        ("Grant", json!("secret")),
        ("Stdout", json!("")),
        ("CommandPreview", Value::Null),
        ("kind", json!("RemoteGrant")),
    ];
    let mut cases = Vec::new();
    for level in [
        "",
        "/Server",
        "/Request",
        "/Authorization",
        "/Operation",
        "/Operation/EventChannel",
    ] {
        for (member, extra) in &extras {
            let mut value = control();
            insert(&mut value, level, member, extra.clone());
            cases.push((format!("{member} at '{level}'"), value.to_string()));
        }
    }
    assert_rejected_all(cases);
}

#[test]
fn duplicate_members_are_rejected_at_every_object_level() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    let control_text = control().to_string();
    let mut cases = Vec::new();
    for (level, fragment) in [
        ("top", r#""ResponseVersion":"1.0""#),
        ("Server", r#""Transport":"HttpNamedPipe""#),
        ("Request", r#""Operation":"Install""#),
        ("Authorization", r#""Kind":"RemoteGrant""#),
        ("Operation", r#""Status":"Running""#),
        ("EventChannel", r#""Kind":"LocalPipe""#),
    ] {
        assert_eq!(
            control_text.matches(fragment).count(),
            1,
            "{fragment} must anchor exactly one member"
        );
        cases.push((
            format!("duplicate in {level}"),
            control_text.replacen(fragment, &format!("{fragment},{fragment}"), 1),
        ));
    }
    assert_rejected_all(cases);
}

#[test]
fn non_object_and_malformed_documents_are_rejected() {
    // oracle: contract Devolutions/devolutions-gateway@c79e7bd775370b061b5939d0ac8442c7215de869:crates/agent-channel-proto/CONTRACT.package-management.md §broker-grant-response
    let valid = control().to_string();
    assert_rejected_all(vec![
        ("array".to_owned(), "[]".to_owned()),
        ("string".to_owned(), "\"GrantExecutionResponse\"".to_owned()),
        ("null".to_owned(), "null".to_owned()),
        ("empty".to_owned(), String::new()),
        ("truncated".to_owned(), valid[..valid.len() - 1].to_owned()),
        ("trailing content".to_owned(), format!("{valid} {{}}")),
    ]);
}
