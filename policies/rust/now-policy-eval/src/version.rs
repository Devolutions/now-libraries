//! The version scheme ([§version-scheme]).
//!
//! [§version-scheme]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#version-scheme

use core::cmp::Ordering;

/// A version of the contract's version scheme.
#[derive(Debug, Clone)]
pub(crate) struct Version {
    /// Release components without leading zeros, with trailing zero components removed.
    release: Vec<String>,
    /// The number of release components as written.
    release_count: usize,
    /// Pre-release identifiers; empty when the version has no pre-release.
    pre_release: Vec<String>,
}

impl Version {
    /// Reads `text` as a version, or returns `None` when it is not one.
    pub(crate) fn parse(text: &str) -> Option<Self> {
        let text = text.strip_prefix(['v', 'V']).unwrap_or(text);

        let (text, build) = match text.split_once('+') {
            Some((text, build)) => (text, Some(build)),
            None => (text, None),
        };
        if let Some(build) = build
            && !build
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'.')
        {
            return None;
        }

        let (release_text, pre_release_text) = match text.split_once('-') {
            Some((release, pre_release)) => (release, Some(pre_release)),
            None => (text, None),
        };

        let mut release = Vec::new();
        for component in release_text.split('.') {
            if component.is_empty() || !component.bytes().all(|b| b.is_ascii_digit()) {
                return None;
            }
            release.push(component.trim_start_matches('0').to_owned());
        }
        let release_count = release.len();
        while release.last().is_some_and(String::is_empty) {
            release.pop();
        }

        let mut pre_release = Vec::new();
        if let Some(pre_release_text) = pre_release_text {
            for identifier in pre_release_text.split('.') {
                let valid = !identifier.is_empty()
                    && identifier.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-')
                    && !(is_numeric(identifier) && 1 < identifier.len() && identifier.starts_with('0'));
                if !valid {
                    return None;
                }
                pre_release.push(identifier.to_owned());
            }
        }

        Some(Self {
            release,
            release_count,
            pre_release,
        })
    }

    /// Returns the number of release components as written.
    pub(crate) fn release_count(&self) -> usize {
        self.release_count
    }

    pub(crate) fn has_pre_release(&self) -> bool {
        !self.pre_release.is_empty()
    }
}

impl PartialEq for Version {
    fn eq(&self, other: &Self) -> bool {
        self.cmp(other) == Ordering::Equal
    }
}

impl Eq for Version {}

impl PartialOrd for Version {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for Version {
    fn cmp(&self, other: &Self) -> Ordering {
        // Trailing zero components are removed, so comparing the remaining components with missing ones as zero is a plain sequence comparison.
        let release = self
            .release
            .iter()
            .map(|component| (component.len(), component))
            .cmp(other.release.iter().map(|component| (component.len(), component)));

        release.then_with(|| match (self.has_pre_release(), other.has_pre_release()) {
            (false, false) => Ordering::Equal,
            (false, true) => Ordering::Greater,
            (true, false) => Ordering::Less,
            (true, true) => compare_pre_release(&self.pre_release, &other.pre_release),
        })
    }
}

fn is_numeric(identifier: &str) -> bool {
    identifier.bytes().all(|b| b.is_ascii_digit())
}

/// Compares pre-releases by Semantic Versioning 2.0.0 precedence.
fn compare_pre_release(a: &[String], b: &[String]) -> Ordering {
    for (a, b) in a.iter().zip(b) {
        let ordering = match (is_numeric(a), is_numeric(b)) {
            (true, true) => a.len().cmp(&b.len()).then_with(|| a.cmp(b)),
            (true, false) => Ordering::Less,
            (false, true) => Ordering::Greater,
            (false, false) => a.as_bytes().cmp(b.as_bytes()),
        };
        if ordering != Ordering::Equal {
            return ordering;
        }
    }
    a.len().cmp(&b.len())
}
