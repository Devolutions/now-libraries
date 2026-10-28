use now_policy_eval::{
    Check, CustomInstallLocation, CustomParameters, Decision, Elevation, Manager, Operation, OptIns, Outcome, Package,
    Policy, Request, Source, Timestamp, parse_policy,
};

/// The metadata every test document uses unless the test is about metadata.
pub(crate) const METADATA: &str =
    r#"{"Id":"test","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z"}"#;

/// The evaluation time every test uses unless the test is about time.
pub(crate) const NOW: &str = "2026-06-01T00:00:00Z";

/// Builds a document from its trusted sources and rules, written as JSON lists.
pub(crate) fn document(trusted_sources: &str, rules: &str) -> String {
    format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"TrustedSources":{trusted_sources},"Rules":{rules}}}"#
    )
}

pub(crate) fn policy(text: &str) -> Policy {
    match parse_policy(text.as_bytes()) {
        Ok(policy) => policy,
        Err(errors) => panic!("test policy is invalid: {errors:?}\n{text}"),
    }
}

/// Returns the `(code, pointer)` of each validation error, sorted, or an empty list for a valid document.
pub(crate) fn errors(bytes: &[u8]) -> Vec<(String, String)> {
    let mut errors: Vec<(String, String)> = match parse_policy(bytes) {
        Ok(_) => Vec::new(),
        Err(errors) => errors
            .into_iter()
            .map(|error| (error.code.as_str().to_owned(), error.pointer))
            .collect(),
    };
    errors.sort();
    errors
}

pub(crate) fn error(code: &str, pointer: &str) -> Vec<(String, String)> {
    vec![(code.to_owned(), pointer.to_owned())]
}

pub(crate) fn time(text: &str) -> Timestamp {
    text.parse().expect("test timestamp")
}

/// A plain WinGet install of `Contoso.Tool` 1.2.3 from the `winget` source.
pub(crate) fn winget_install() -> Request {
    let mut request = request("Install", "Winget", "winget", "Contoso.Tool");
    request.package.version = Some("1.2.3".to_owned());
    request
}

/// A plain non-elevated request through `manager` for `id` from `source`, with no version, scope, architecture or opt-in.
pub(crate) fn request(operation: &str, manager: &str, source: &str, id: &str) -> Request {
    Request {
        operation: Operation::from_name(operation).expect("test operation"),
        manager: Manager::from_name(manager).expect("test manager"),
        source: Source {
            name: source.to_owned(),
        },
        package: Package {
            id: id.to_owned(),
            version: None,
            architecture: None,
        },
        scope: None,
        elevation: Elevation::NonElevated,
        opt_ins: OptIns::default(),
    }
}

/// Returns `request` with the custom parameters `values`.
pub(crate) fn with_parameters(mut request: Request, values: &[&str]) -> Request {
    request.opt_ins.custom_parameters = Some(CustomParameters {
        values: values.iter().map(|value| (*value).to_owned()).collect(),
    });
    request
}

/// Returns `request` with the install location `path`.
pub(crate) fn with_location(mut request: Request, path: &str) -> Request {
    request.opt_ins.custom_install_location = Some(CustomInstallLocation { path: path.to_owned() });
    request
}

/// A policy that trusts every source of every manager the broker builds commands for and allows everything with no grants.
pub(crate) const TRUST_ALL: &str = r#"[
    {"Manager":"Winget","AnySource":true},
    {"Manager":"PowerShell","AnySource":true},
    {"Manager":"PowerShell7","AnySource":true},
    {"Manager":"Bun","AnySource":true},
    {"Manager":"Cargo","AnySource":true},
    {"Manager":"Chocolatey","AnySource":true},
    {"Manager":"Dotnet","AnySource":true},
    {"Manager":"Npm","AnySource":true},
    {"Manager":"Pip","AnySource":true},
    {"Manager":"Scoop","AnySource":true},
    {"Manager":"Vcpkg","AnySource":true}
]"#;

pub(crate) fn evaluate_at(policy_text: &str, request: &Request, at: &str) -> Decision {
    now_policy_eval::evaluate(&policy(policy_text), request, time(at))
}

pub(crate) fn evaluate(policy_text: &str, request: &Request) -> Decision {
    evaluate_at(policy_text, request, NOW)
}

/// Evaluates `request` under a policy that trusts every source and has the given rules.
pub(crate) fn evaluate_rules(rules: &str, request: &Request) -> Decision {
    evaluate(&document(TRUST_ALL, rules), request)
}

/// Returns the check of the decision for `request` under a policy that trusts every source and has the given rules.
pub(crate) fn check_of(rules: &str, request: &Request) -> Check {
    evaluate_rules(rules, request).check
}

/// A compact view of a decision: outcome, check, rule ids with reasons, opt-in names and audited.
pub(crate) fn summary(decision: &Decision) -> (&'static str, &'static str, Vec<String>, Vec<&'static str>, bool) {
    (
        match decision.outcome {
            Outcome::Allow => "Allow",
            Outcome::Deny => "Deny",
        },
        decision.check.as_str(),
        decision
            .rules
            .iter()
            .map(|rule| match &rule.reason {
                Some(reason) => format!("{}: {reason}", rule.id),
                None => rule.id.clone(),
            })
            .collect(),
        decision.opt_ins.iter().map(|opt_in| opt_in.as_str()).collect(),
        decision.audited,
    )
}

/// A catch-all Allow rule with no grants.
pub(crate) const ALLOW_ALL: &str = r#"[{"Id":"all","Decision":"Allow","Select":{}}]"#;
