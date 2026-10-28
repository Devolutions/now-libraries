//! Text comparison and patterns ([§text-comparison], [§patterns]).
//!
//! [§text-comparison]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#text-comparison
//! [§patterns]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#patterns

use crate::unicode_tables::{DEFAULT_IGNORABLE_CODE_POINT, SIMPLE_UPPERCASE, WHITE_SPACE};

/// Returns the simple uppercase mapping of `c` from `UnicodeData.txt` of Unicode 16.0.0, or `c` when it has none.
pub(crate) fn simple_uppercase(c: char) -> char {
    SIMPLE_UPPERCASE
        .binary_search_by_key(&u32::from(c), |(code_point, _)| *code_point)
        .ok()
        .and_then(|idx| char::from_u32(SIMPLE_UPPERCASE[idx].1))
        .unwrap_or(c)
}

/// Returns whether `a` and `b` are equal ignoring case ([§text-comparison]).
///
/// [§text-comparison]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#text-comparison
pub(crate) fn eq_ignore_case(a: &str, b: &str) -> bool {
    a.chars().map(simple_uppercase).eq(b.chars().map(simple_uppercase))
}

/// Returns whether `c` has the `White_Space` property in `PropList.txt` of Unicode 16.0.0.
pub(crate) fn is_white_space(c: char) -> bool {
    in_ranges(WHITE_SPACE, c)
}

/// Returns whether `c` has the `Default_Ignorable_Code_Point` property in `DerivedCoreProperties.txt` of Unicode 16.0.0.
pub(crate) fn is_default_ignorable(c: char) -> bool {
    in_ranges(DEFAULT_IGNORABLE_CODE_POINT, c)
}

fn in_ranges(ranges: &[(u32, u32)], c: char) -> bool {
    let c = u32::from(c);
    ranges
        .binary_search_by(|(first, last)| {
            if c < *first {
                core::cmp::Ordering::Greater
            } else if *last < c {
                core::cmp::Ordering::Less
            } else {
                core::cmp::Ordering::Equal
            }
        })
        .is_ok()
}

/// Returns whether `pattern` matches the whole of `value` when characters compare ignoring case.
pub(crate) fn pattern_matches_ignore_case(pattern: &str, value: &str) -> bool {
    let pattern: Vec<char> = pattern.chars().map(simple_uppercase).collect();
    let value: Vec<char> = value.chars().map(simple_uppercase).collect();
    wildcard_matches(&pattern, &value)
}

/// Returns whether `pattern` matches the whole of `value` when characters compare exactly.
pub(crate) fn pattern_matches_exact(pattern: &str, value: &str) -> bool {
    let pattern: Vec<char> = pattern.chars().collect();
    let value: Vec<char> = value.chars().collect();
    wildcard_matches(&pattern, &value)
}

/// Applies `canonical` to the characters of `pattern` between `*`, keeping every `*`.
pub(crate) fn map_pattern_segments(pattern: &str, canonical: impl Fn(&str) -> String) -> String {
    pattern.split('*').map(canonical).collect::<Vec<_>>().join("*")
}

/// Matches `value` against `pattern`, where `*` matches any sequence of zero or more characters.
fn wildcard_matches(pattern: &[char], value: &[char]) -> bool {
    // Greedy matching that backtracks only to the most recent `*`, which is enough because a later `*` can absorb whatever an earlier one could.
    let mut p = 0;
    let mut v = 0;
    let mut star: Option<(usize, usize)> = None;

    while v < value.len() {
        if p < pattern.len() && pattern[p] == '*' {
            star = Some((p, v));
            p += 1;
        } else if p < pattern.len() && pattern[p] == value[v] {
            p += 1;
            v += 1;
        } else if let Some((star_p, star_v)) = star {
            p = star_p + 1;
            v = star_v + 1;
            star = Some((star_p, star_v + 1));
        } else {
            return false;
        }
    }

    pattern[p..].iter().all(|c| *c == '*')
}
