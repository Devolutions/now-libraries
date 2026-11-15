//! Remote execution grant presented in the [`PACKAGE_GRANT_HEADER`](crate::PACKAGE_GRANT_HEADER) header.

use core::fmt;

/// Maximum grant length in characters.
pub const MAX_PACKAGE_GRANT_LENGTH: usize = 2048;

/// Opaque bearer grant authorizing one package operation.
///
/// The value is a secret: `Debug` redacts it, and there is no `Display`, `Serialize` or `Deserialize`.
/// INVARIANT: The value is 1 to [`MAX_PACKAGE_GRANT_LENGTH`] ASCII characters matching `[A-Za-z0-9][A-Za-z0-9._~:-]*`.
#[derive(Clone, PartialEq, Eq)]
pub struct PackageGrant(String);

/// Reason a value is not a valid [`PackageGrant`].
///
/// It never carries the rejected value.
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error)]
pub enum PackageGrantError {
    #[error("grant is empty")]
    Empty,
    #[error("grant exceeds the maximum length")]
    TooLong,
    #[error("grant contains characters outside the allowed set")]
    InvalidCharacters,
}

impl PackageGrant {
    pub fn parse(value: &str) -> Result<Self, PackageGrantError> {
        if value.is_empty() {
            return Err(PackageGrantError::Empty);
        }

        // INVARIANT: Every accepted character is ASCII, so the byte length equals the character count.
        if MAX_PACKAGE_GRANT_LENGTH < value.len() {
            return Err(PackageGrantError::TooLong);
        }

        let mut bytes = value.bytes();
        let first_is_alphanumeric = bytes.next().is_some_and(|byte| byte.is_ascii_alphanumeric());
        let rest_is_valid =
            bytes.all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'_' | b'~' | b':' | b'-'));
        if !(first_is_alphanumeric && rest_is_valid) {
            return Err(PackageGrantError::InvalidCharacters);
        }

        Ok(Self(value.to_owned()))
    }

    /// Returns the secret grant value.
    ///
    /// The caller must not log it or place it in a message.
    pub fn expose(&self) -> &str {
        &self.0
    }
}

impl fmt::Debug for PackageGrant {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("PackageGrant(<redacted>)")
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used)]
mod tests {
    use super::{MAX_PACKAGE_GRANT_LENGTH, PackageGrant, PackageGrantError};

    #[test]
    fn accepts_every_allowed_character_class_and_both_length_bounds() {
        for value in [
            "a".to_owned(),
            "Z9._~:-x".to_owned(),
            "0".repeat(MAX_PACKAGE_GRANT_LENGTH),
        ] {
            assert_eq!(PackageGrant::parse(&value).unwrap().expose(), value);
        }
    }

    #[test]
    fn rejects_values_outside_the_grammar_with_the_matching_reason() {
        let oversized = "a".repeat(MAX_PACKAGE_GRANT_LENGTH + 1);
        let cases = [
            ("", PackageGrantError::Empty),
            (oversized.as_str(), PackageGrantError::TooLong),
            (".abc", PackageGrantError::InvalidCharacters),
            ("-abc", PackageGrantError::InvalidCharacters),
            ("ab c", PackageGrantError::InvalidCharacters),
            (" abc", PackageGrantError::InvalidCharacters),
            ("ab/c", PackageGrantError::InvalidCharacters),
            ("abé", PackageGrantError::InvalidCharacters),
            ("ab\n", PackageGrantError::InvalidCharacters),
        ];

        for (value, expected) in cases {
            assert_eq!(PackageGrant::parse(value).unwrap_err(), expected, "value {value:?}");
        }
    }

    #[test]
    fn debug_and_error_output_never_contain_the_value() {
        let grant = PackageGrant::parse("secret-grant-value").unwrap();
        assert!(!format!("{grant:?}").contains("secret"));

        let error = PackageGrant::parse("secret grant value").unwrap_err();
        assert!(!format!("{error} {error:?}").contains("secret"));
    }
}
