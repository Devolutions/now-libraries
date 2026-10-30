//! The public timestamp type.

use now_policy_eval::Timestamp;
use rstest::rstest;

use crate::common::time;

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §timestamps
#[case::offsets_applied("2026-01-01T02:00:00+02:00", "2026-01-01T00:00:00Z")]
#[case::negative_offset_across_midnight("2025-12-31T19:00:00-05:00", "2026-01-01T00:00:00Z")]
#[case::trailing_fraction_zeros("2026-01-01T00:00:00.5Z", "2026-01-01T00:00:00.500000000Z")]
fn equal_instants_compare_equal(#[case] a: &str, #[case] b: &str) {
    assert_eq!(time(a), time(b));
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §timestamps
#[case::nanosecond("2026-01-01T00:00:00Z", "2026-01-01T00:00:00.000000001Z")]
#[case::later_clock_earlier_instant("2026-01-01T10:00:00+02:00", "2026-01-01T09:00:00Z")]
#[case::leap_day_then_march("2024-02-29T23:59:59Z", "2024-03-01T00:00:00Z")]
#[case::year_boundary("0001-12-31T23:59:59Z", "0002-01-01T00:00:00Z")]
#[case::offset_beyond_first_year("0001-01-01T00:00:00+23:59", "0001-01-01T00:00:00Z")]
fn earlier_instant_compares_lower(#[case] earlier: &str, #[case] later: &str) {
    assert!(time(earlier) < time(later));
}

#[test]
fn invalid_text_is_an_error() {
    // oracle: contract policies/CONTRACT.policy.md §timestamps
    assert!("2026-02-30T00:00:00Z".parse::<Timestamp>().is_err());
}
