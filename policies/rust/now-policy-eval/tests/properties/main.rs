//! Properties of the evaluator over generated policies and requests, and of the reader over malformed input.

use now_policy_eval::{
    Check, CustomInstallLocation, CustomParameters, Decision, Elevation, Manager, Operation, OptIns, Outcome, Package,
    Policy, Request, Scope, Source, evaluate, parse_policy,
};
use proptest::prelude::*;
use proptest::sample::select;
use {rstest as _, serde_json as _};

mod generators;

use generators::{GenPolicy, Shuffler};

proptest! {
    #![proptest_config(ProptestConfig::with_cases(2000))]

    #[test]
    fn adding_an_allow_rule_never_denies_an_allowed_request(
        policy in generators::policy(),
        extra in generators::rule(Just(false)),
        position in any::<prop::sample::Index>(),
        request in generators::request(),
        time in generators::time(),
    ) {
        // oracle: property monotonic
        // oracle: contract policies/CONTRACT.policy.md §monotonicity
        let (before, after) = with_extra_rule(&policy, extra, position, &request, time);
        if before.outcome == Outcome::Allow {
            prop_assert_eq!(after.outcome, Outcome::Allow, "before: {:?}\nafter: {:?}", before, after);
        }
    }

    #[test]
    fn adding_a_deny_rule_never_allows_a_denied_request(
        policy in generators::policy(),
        extra in generators::rule(Just(true)),
        position in any::<prop::sample::Index>(),
        request in generators::request(),
        time in generators::time(),
    ) {
        // oracle: property monotonic
        // oracle: contract policies/CONTRACT.policy.md §monotonicity
        let (before, after) = with_extra_rule(&policy, extra, position, &request, time);
        if before.outcome == Outcome::Deny {
            prop_assert_eq!(after.outcome, Outcome::Deny, "before: {:?}\nafter: {:?}", before, after);
        }
    }

    #[test]
    fn reordering_lists_changes_no_decision(
        policy in generators::policy(),
        seed in any::<u64>(),
        request in generators::request(),
        time in generators::time(),
    ) {
        // oracle: contract policies/CONTRACT.policy.md §order-independence
        let ids = policy.default_ids();
        let (reordered, reordered_ids) = policy.reordered(&ids, &mut Shuffler::new(seed));

        let original = comparable(decide(&policy.parse(&ids), &request, time));
        let reordered = comparable(decide(&reordered.parse(&reordered_ids), &request, time));
        prop_assert_eq!(original, reordered);
    }

    #[test]
    fn arbitrary_bytes_give_a_policy_or_errors(bytes in prop::collection::vec(any::<u8>(), 0..512)) {
        // oracle: contract policies/CONTRACT.policy.md §errors
        if let Err(errors) = parse_policy(&bytes) {
            prop_assert!(!errors.is_empty());
        }
    }

    #[test]
    fn truncated_documents_give_errors(policy in generators::policy(), cut in any::<prop::sample::Index>()) {
        // oracle: contract policies/CONTRACT.policy.md §json
        // A proper prefix of an object written without trailing whitespace is not well-formed.
        let text = policy.to_json(&policy.default_ids());
        let errors = parse_policy(&text.as_bytes()[..cut.index(text.len())]).err();
        prop_assert!(errors.is_some_and(|errors| !errors.is_empty()));
    }

    #[test]
    fn mutated_documents_give_a_policy_or_errors(
        policy in generators::policy(),
        mutations in prop::collection::vec((any::<prop::sample::Index>(), any::<u8>(), 0u8..3), 1..8),
    ) {
        // oracle: contract policies/CONTRACT.policy.md §errors
        let mut bytes = policy.to_json(&policy.default_ids()).into_bytes();
        for (position, byte, kind) in mutations {
            let idx = position.index(bytes.len());
            match kind {
                0 => bytes[idx] = byte,
                1 => bytes.insert(idx, byte),
                _ => {
                    bytes.remove(idx);
                }
            }
            if bytes.is_empty() {
                break;
            }
        }
        if let Err(errors) = parse_policy(&bytes) {
            prop_assert!(!errors.is_empty());
        }
    }

    #[test]
    fn requests_with_arbitrary_text_are_decided(
        policy in generators::policy(),
        request in arbitrary_request(),
        time in generators::time(),
    ) {
        // oracle: contract policies/CONTRACT.policy.md §decision
        // A request that is not well-formed gets an error, and every other one gets a decision, whatever its text.
        // The outcome of a decision follows the check and audit flag.
        if let Ok(decision) = evaluate(&policy.parse(&policy.default_ids()), &request, time) {
            let allowed = decision.check == Check::AllowRules || decision.audited;
            prop_assert_eq!(decision.outcome == Outcome::Allow, allowed);
        }
    }
}

/// Evaluates a request that the generators build well-formed.
fn decide(policy: &Policy, request: &Request, time: now_policy_eval::Timestamp) -> Decision {
    evaluate(policy, request, time).expect("generated request is well-formed")
}

/// Evaluates `request` against `policy` and against `policy` with `extra` inserted at `position` under an unused id.
fn with_extra_rule(
    policy: &GenPolicy,
    extra: generators::GenRule,
    position: prop::sample::Index,
    request: &Request,
    time: now_policy_eval::Timestamp,
) -> (Decision, Decision) {
    let ids = policy.default_ids();
    let before = decide(&policy.parse(&ids), request, time);

    let position = position.index(policy.rules.len() + 1);
    let mut extended = policy.clone();
    extended.rules.insert(position, extra);
    let mut extended_ids = ids;
    extended_ids.insert(position, "extra".to_owned());
    let after = decide(&extended.parse(&extended_ids), request, time);

    (before, after)
}

/// A decision with its rules sorted, since reordering may change only the order in which a decision names rules.
fn comparable(mut decision: Decision) -> Decision {
    decision.rules.sort_by(|a, b| a.id.cmp(&b.id));
    decision
}

/// Requests whose strings are arbitrary text, mostly in the shape of real requests, and whose other members come from their closed sets.
fn arbitrary_request() -> impl Strategy<Value = Request> {
    let text = || prop_oneof![".{0,12}", "[-a-zA-Z0-9:@\\\\/=\"]{0,12}"];
    (
        select(Operation::ALL),
        select(Manager::ALL),
        text(),
        text(),
        prop::option::of(text()),
        prop::option::of(select(Scope::ALL)),
        select(Elevation::ALL),
        prop::option::of(text()),
        prop::collection::vec(text(), 0..5),
    )
        .prop_map(
            |(operation, manager, source, id, version, scope, elevation, location, parameters)| Request {
                operation,
                manager,
                source: Source { name: source },
                package: Package {
                    id,
                    version,
                    architecture: None,
                },
                scope,
                elevation,
                opt_ins: OptIns {
                    custom_install_location: location.map(|path| CustomInstallLocation { path }),
                    custom_parameters: Some(CustomParameters { values: parameters }),
                    ..OptIns::default()
                },
            },
        )
}
