//! Reading and validating documents.

use rstest::rstest;

use crate::common::{METADATA, document, error, errors};

const MAX_SIZE: usize = 4_194_304;

/// Builds a document with `metadata` in place of the default metadata.
fn with_metadata(metadata: &str) -> String {
    format!(r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{metadata},"TrustedSources":[],"Rules":[]}}"#)
}

/// Builds a document with one rule.
fn with_rule(rule: &str) -> String {
    document("[]", &format!("[{rule}]"))
}

fn padded(text: &str, size: usize) -> Vec<u8> {
    let mut bytes = text.as_bytes().to_vec();
    bytes.resize(size, b' ');
    bytes
}

#[test]
fn minimal_document_is_valid() {
    // oracle: contract policies/CONTRACT.policy.md §document-keys
    assert_eq!(errors(document("[]", "[]").as_bytes()), []);
}

#[test]
fn document_at_the_size_limit_is_valid() {
    // oracle: contract policies/CONTRACT.policy.md §json
    assert_eq!(errors(&padded(&document("[]", "[]"), MAX_SIZE)), []);
}

#[test]
fn document_over_the_size_limit_reports_only_document_too_large() {
    // oracle: contract policies/CONTRACT.policy.md §errors
    // The document is also malformed, which DocumentTooLarge hides.
    let mut bytes = padded(&document("[]", "[]"), MAX_SIZE + 1);
    bytes.push(b',');
    assert_eq!(errors(&bytes), error("DocumentTooLarge", ""));
}

#[test]
fn byte_order_mark_counts_toward_the_size_limit() {
    // oracle: contract policies/CONTRACT.policy.md §json
    let mut bytes = b"\xEF\xBB\xBF".to_vec();
    bytes.extend(padded(&document("[]", "[]"), MAX_SIZE - 2));
    assert_eq!(errors(&bytes), error("DocumentTooLarge", ""));
}

#[test]
fn one_leading_byte_order_mark_is_ignored() {
    // oracle: contract policies/CONTRACT.policy.md §json
    let mut bytes = b"\xEF\xBB\xBF".to_vec();
    bytes.extend(document("[]", "[]").as_bytes());
    assert_eq!(errors(&bytes), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §json
#[case::second_byte_order_mark(b"\xEF\xBB\xBF\xEF\xBB\xBF{}".to_vec())]
#[case::invalid_utf8(b"{\"PolicyFormatVersion\":\"2.0.0\xFF\"}".to_vec())]
#[case::trailing_comma(b"{\"Rules\":[],}".to_vec())]
#[case::comment(b"{/* c */}".to_vec())]
#[case::single_quotes(b"{'Rules':[]}".to_vec())]
#[case::leading_zero(b"{\"a\":01}".to_vec())]
#[case::bare_fraction(b"{\"a\":1.}".to_vec())]
#[case::nan(b"{\"a\":NaN}".to_vec())]
#[case::unescaped_control_character(b"{\"a\":\"\t\"}".to_vec())]
#[case::unpaired_high_surrogate(br#"{"a":"\ud800"}"#.to_vec())]
#[case::unpaired_low_surrogate(br#"{"a":"\udc00x"}"#.to_vec())]
#[case::reversed_surrogates(br#"{"a":"\udc00\ud800"}"#.to_vec())]
#[case::second_value(b"{} {}".to_vec())]
#[case::invalid_escape(br#"{"a":"\x"}"#.to_vec())]
#[case::non_breaking_space_as_whitespace(b"{\xC2\xA0}".to_vec())]
fn malformed_documents_report_only_malformed_document(#[case] bytes: Vec<u8>) {
    assert_eq!(errors(&bytes), error("MalformedDocument", ""));
}

#[test]
fn nesting_of_64_levels_is_well_formed() {
    // oracle: contract policies/CONTRACT.policy.md §json
    // The top-level object is level 1, so 63 nested arrays reach level 64.
    let text = format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"TrustedSources":[],"Rules":[],"Deep":{}{}}}"#,
        "[".repeat(63),
        "]".repeat(63)
    );
    assert_eq!(errors(text.as_bytes()), error("UnknownKey", "/Deep"));
}

#[test]
fn nesting_of_65_levels_is_malformed() {
    // oracle: contract policies/CONTRACT.policy.md §json
    let text = format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"TrustedSources":[],"Rules":[],"Deep":{}{}}}"#,
        "[".repeat(64),
        "]".repeat(64)
    );
    assert_eq!(errors(text.as_bytes()), error("MalformedDocument", ""));
}

#[test]
fn paired_surrogate_escape_is_well_formed() {
    // oracle: contract policies/CONTRACT.policy.md §json
    let text = with_metadata(
        r#"{"Id":"test","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z","Description":"\ud83d\ude00"}"#,
    );
    assert_eq!(errors(text.as_bytes()), []);
}

#[rstest]
// oracle: spec RFC 8259 §7
// oracle: contract policies/CONTRACT.policy.md §json
// The key `k` followed by the escape names the same key as `k` followed by the `\uXXXX` escape of the decoded character.
#[case::quotation_mark(r#"\""#, '"')]
#[case::reverse_solidus(r"\\", '\\')]
#[case::solidus(r"\/", '/')]
#[case::backspace(r"\b", '\u{8}')]
#[case::form_feed(r"\f", '\u{c}')]
#[case::line_feed(r"\n", '\n')]
#[case::carriage_return(r"\r", '\r')]
#[case::tab(r"\t", '\t')]
fn two_character_escapes_decode_to_their_character(#[case] escape: &str, #[case] decoded: char) {
    let text = format!(r#"{{"k{escape}":1,"k\u{:04x}":2}}"#, u32::from(decoded));
    let pointer = format!("/k{}", decoded.to_string().replace('~', "~0").replace('/', "~1"));
    assert_eq!(errors(text.as_bytes()), error("DuplicateKey", &pointer));
}

#[test]
fn malformed_document_hides_duplicate_keys() {
    // oracle: contract policies/CONTRACT.policy.md §errors
    assert_eq!(errors(br#"{"a":1,"a":2,}"#), error("MalformedDocument", ""));
}

#[test]
fn keys_repeated_after_unescaping_are_duplicates() {
    // oracle: contract policies/CONTRACT.policy.md §json
    // Only DuplicateKey errors are reported, even though the format version is missing.
    assert_eq!(
        errors(br#"{"Rules":[],"\u0052ules":[]}"#),
        error("DuplicateKey", "/Rules")
    );
}

#[test]
fn duplicate_key_pointer_escapes_its_tokens() {
    // oracle: spec RFC 6901 §3
    // oracle: contract policies/CONTRACT.policy.md §errors
    let text = format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"TrustedSources":[],"Rules":[{{"a/b~":1,"a/b~":2}}]}}"#
    );
    assert_eq!(errors(text.as_bytes()), error("DuplicateKey", "/Rules/0/a~1b~0"));
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §format-version
#[case::other_version(r#""2.0.1""#)]
#[case::version_1(r#""1.0.0""#)]
#[case::number(r#"2"#)]
#[case::null(r#"null"#)]
fn unsupported_format_version_is_the_only_error(#[case] format_version: &str) {
    let text = format!(r#"{{"PolicyFormatVersion":{format_version},"Rules":{{}}}}"#);
    assert_eq!(
        errors(text.as_bytes()),
        error("UnsupportedFormatVersion", "/PolicyFormatVersion")
    );
}

#[test]
fn missing_format_version_is_the_only_error() {
    // oracle: contract policies/CONTRACT.policy.md §errors
    assert_eq!(errors(br#"{"Rules":{}}"#), error("MissingKey", "/PolicyFormatVersion"));
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §document-keys
#[case::metadata(r#"{"PolicyFormatVersion":"2.0.0","TrustedSources":[],"Rules":[]}"#, "/Metadata")]
#[case::trusted_sources(
    r#"{"PolicyFormatVersion":"2.0.0","Metadata":METADATA,"Rules":[]}"#,
    "/TrustedSources"
)]
#[case::rules(
    r#"{"PolicyFormatVersion":"2.0.0","Metadata":METADATA,"TrustedSources":[]}"#,
    "/Rules"
)]
fn missing_top_level_key(#[case] text: &str, #[case] pointer: &str) {
    let text = text.replace("METADATA", METADATA);
    assert_eq!(errors(text.as_bytes()), error("MissingKey", pointer));
}

#[test]
fn unknown_top_level_key() {
    // oracle: contract policies/CONTRACT.policy.md §errors
    let text =
        format!(r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"TrustedSources":[],"Rules":[],"rules":[]}}"#);
    assert_eq!(errors(text.as_bytes()), error("UnknownKey", "/rules"));
}

#[test]
fn enforcement_is_optional_and_accepts_audit_mode() {
    // oracle: contract policies/CONTRACT.policy.md §audit-mode
    let text = format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"Enforcement":{{"AuditMode":true}},"TrustedSources":[],"Rules":[]}}"#
    );
    assert_eq!(errors(text.as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §errors
#[case::audit_mode_string(r#"{"AuditMode":"true"}"#, "WrongType", "/Enforcement/AuditMode")]
#[case::audit_mode_null(r#"{"AuditMode":null}"#, "WrongType", "/Enforcement/AuditMode")]
#[case::unknown_key(r#"{"Mode":true}"#, "UnknownKey", "/Enforcement/Mode")]
#[case::not_an_object(r#"[]"#, "WrongType", "/Enforcement")]
fn invalid_enforcement(#[case] enforcement: &str, #[case] code: &str, #[case] pointer: &str) {
    let text = format!(
        r#"{{"PolicyFormatVersion":"2.0.0","Metadata":{METADATA},"Enforcement":{enforcement},"TrustedSources":[],"Rules":[]}}"#
    );
    assert_eq!(errors(text.as_bytes()), error(code, pointer));
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §metadata
#[case::lowest("1")]
#[case::highest("2147483647")]
fn revision_in_range_is_valid(#[case] revision: &str) {
    let text = with_metadata(&format!(
        r#"{{"Id":"test","Publisher":"Contoso","Revision":{revision},"PublishedAt":"2026-01-01T00:00:00Z"}}"#
    ));
    assert_eq!(errors(text.as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §metadata
#[case::zero("0")]
#[case::negative("-1")]
#[case::negative_zero("-0")]
#[case::above_range("2147483648")]
#[case::huge("123456789012345678901234567890")]
#[case::fraction("1.0")]
#[case::exponent("1e0")]
#[case::huge_exponent("1e999999999")]
fn revision_out_of_range_or_not_an_integer_is_invalid(#[case] revision: &str) {
    let text = with_metadata(&format!(
        r#"{{"Id":"test","Publisher":"Contoso","Revision":{revision},"PublishedAt":"2026-01-01T00:00:00Z"}}"#
    ));
    assert_eq!(errors(text.as_bytes()), error("InvalidValue", "/Metadata/Revision"));
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §metadata
#[case::revision_string(
    r#"{"Id":"test","Publisher":"Contoso","Revision":"1","PublishedAt":"2026-01-01T00:00:00Z"}"#,
    "WrongType",
    "/Metadata/Revision"
)]
#[case::empty_id(
    r#"{"Id":"","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z"}"#,
    "InvalidValue",
    "/Metadata/Id"
)]
#[case::empty_publisher(
    r#"{"Id":"test","Publisher":"","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z"}"#,
    "InvalidValue",
    "/Metadata/Publisher"
)]
#[case::null_publisher(
    r#"{"Id":"test","Publisher":null,"Revision":1,"PublishedAt":"2026-01-01T00:00:00Z"}"#,
    "WrongType",
    "/Metadata/Publisher"
)]
#[case::missing_id(
    r#"{"Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z"}"#,
    "MissingKey",
    "/Metadata/Id"
)]
#[case::missing_published_at(
    r#"{"Id":"test","Publisher":"Contoso","Revision":1}"#,
    "MissingKey",
    "/Metadata/PublishedAt"
)]
#[case::published_at_number(
    r#"{"Id":"test","Publisher":"Contoso","Revision":1,"PublishedAt":0}"#,
    "WrongType",
    "/Metadata/PublishedAt"
)]
#[case::empty_support_url(
    r#"{"Id":"test","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z","SupportUrl":""}"#,
    "InvalidValue",
    "/Metadata/SupportUrl"
)]
#[case::description_number(
    r#"{"Id":"test","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z","Description":1}"#,
    "WrongType",
    "/Metadata/Description"
)]
#[case::invalid_valid_from(
    r#"{"Id":"test","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z","ValidFrom":"2026-01-01"}"#,
    "InvalidValue",
    "/Metadata/ValidFrom"
)]
#[case::unknown_key(
    r#"{"Id":"test","Publisher":"Contoso","Revision":1,"PublishedAt":"2026-01-01T00:00:00Z","Version":"1"}"#,
    "UnknownKey",
    "/Metadata/Version"
)]
#[case::not_an_object(r#"[]"#, "WrongType", "/Metadata")]
fn invalid_metadata(#[case] metadata: &str, #[case] code: &str, #[case] pointer: &str) {
    assert_eq!(errors(with_metadata(metadata).as_bytes()), error(code, pointer));
}

#[test]
fn empty_description_is_valid() {
    // oracle: contract policies/CONTRACT.policy.md §metadata
    let metadata = METADATA.replace(r#""Id":"test""#, r#""Id":"test","Description":"""#);
    assert_eq!(errors(with_metadata(&metadata).as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §timestamps
#[case::utc("2026-01-31T23:59:59Z")]
#[case::positive_offset("2026-01-31T23:59:59+23:59")]
#[case::negative_offset("2026-01-31T23:59:59-05:00")]
#[case::one_fraction_digit("2026-01-31T23:59:59.5Z")]
#[case::nine_fraction_digits("2026-01-31T23:59:59.123456789Z")]
#[case::leap_day("2024-02-29T00:00:00Z")]
#[case::leap_day_of_400_year("2000-02-29T00:00:00Z")]
#[case::first_year("0001-01-01T00:00:00Z")]
#[case::last_year("9999-12-31T23:59:59Z")]
fn valid_timestamps(#[case] timestamp: &str) {
    let metadata = METADATA.replace("2026-01-01T00:00:00Z", timestamp);
    assert_eq!(errors(with_metadata(&metadata).as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §timestamps
#[case::lowercase_t("2026-01-31t00:00:00Z")]
#[case::lowercase_z("2026-01-31T00:00:00z")]
#[case::space_separator("2026-01-31 00:00:00Z")]
#[case::no_offset("2026-01-31T00:00:00")]
#[case::hour_24("2026-01-31T24:00:00Z")]
#[case::minute_60("2026-01-31T00:60:00Z")]
#[case::leap_second("2026-12-31T23:59:60Z")]
#[case::february_29_of_common_year("2026-02-29T00:00:00Z")]
#[case::february_29_of_1900("1900-02-29T00:00:00Z")]
#[case::april_31("2026-04-31T00:00:00Z")]
#[case::month_13("2026-13-01T00:00:00Z")]
#[case::day_0("2026-01-00T00:00:00Z")]
#[case::year_0("0000-01-01T00:00:00Z")]
#[case::ten_fraction_digits("2026-01-31T00:00:00.1234567890Z")]
#[case::empty_fraction("2026-01-31T00:00:00.Z")]
#[case::offset_24_hours("2026-01-31T00:00:00+24:00")]
#[case::offset_60_minutes("2026-01-31T00:00:00+00:60")]
#[case::offset_without_colon("2026-01-31T00:00:00+0000")]
#[case::short_year("226-01-31T00:00:00Z")]
#[case::date_only("2026-01-31")]
#[case::trailing_text("2026-01-31T00:00:00Zx")]
fn invalid_timestamps(#[case] timestamp: &str) {
    let metadata = METADATA.replace("2026-01-01T00:00:00Z", timestamp);
    assert_eq!(
        errors(with_metadata(&metadata).as_bytes()),
        error("InvalidValue", "/Metadata/PublishedAt")
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §validity-window
#[case::equal("2026-01-01T00:00:00Z", "2026-01-01T00:00:00Z")]
#[case::equal_instants_with_offsets("2026-01-01T02:00:00+02:00", "2026-01-01T00:00:00Z")]
#[case::earlier_instant_despite_later_clock("2026-01-01T10:00:00+02:00", "2026-01-01T09:00:00Z")]
fn valid_from_not_later_than_valid_until_is_valid(#[case] from: &str, #[case] until: &str) {
    let metadata = METADATA.replace(
        r#""Revision":1"#,
        &format!(r#""Revision":1,"ValidFrom":"{from}","ValidUntil":"{until}""#),
    );
    assert_eq!(errors(with_metadata(&metadata).as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §validity-window
#[case::one_nanosecond("2026-01-01T00:00:00.000000001Z", "2026-01-01T00:00:00Z")]
#[case::later_instant_despite_earlier_clock("2026-01-01T09:00:00-02:00", "2026-01-01T10:00:00Z")]
fn valid_from_later_than_valid_until_is_invalid_at_valid_until(#[case] from: &str, #[case] until: &str) {
    let metadata = METADATA.replace(
        r#""Revision":1"#,
        &format!(r#""Revision":1,"ValidFrom":"{from}","ValidUntil":"{until}""#),
    );
    assert_eq!(
        errors(with_metadata(&metadata).as_bytes()),
        error("InvalidValue", "/Metadata/ValidUntil")
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §source-selector
#[case::any_source(r#"{"Manager":"Winget","AnySource":true}"#)]
#[case::named_source(r#"{"Manager":"Scoop","Source":"main"}"#)]
#[case::manager_without_command(r#"{"Manager":"Apt","Source":"main"}"#)]
fn valid_source_selectors(#[case] selector: &str) {
    assert_eq!(errors(document(&format!("[{selector}]"), "[]").as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §source-selector
#[case::both_keys(
    r#"{"Manager":"Winget","Source":"winget","AnySource":true}"#,
    "ConflictingKeys",
    "/TrustedSources/0/AnySource"
)]
#[case::neither_key(r#"{"Manager":"Winget"}"#, "MissingKey", "/TrustedSources/0/Source")]
#[case::any_source_false(
    r#"{"Manager":"Winget","AnySource":false}"#,
    "InvalidValue",
    "/TrustedSources/0/AnySource"
)]
#[case::empty_source(r#"{"Manager":"Winget","Source":""}"#, "InvalidValue", "/TrustedSources/0/Source")]
#[case::lowercase_manager(
    r#"{"Manager":"winget","AnySource":true}"#,
    "InvalidValue",
    "/TrustedSources/0/Manager"
)]
#[case::unknown_manager(r#"{"Manager":"Nix","AnySource":true}"#, "InvalidValue", "/TrustedSources/0/Manager")]
#[case::missing_manager(r#"{"AnySource":true}"#, "MissingKey", "/TrustedSources/0/Manager")]
#[case::unknown_key(
    r#"{"Manager":"Winget","AnySource":true,"Url":"x"}"#,
    "UnknownKey",
    "/TrustedSources/0/Url"
)]
#[case::not_an_object(r#""Winget""#, "WrongType", "/TrustedSources/0")]
fn invalid_source_selectors(#[case] selector: &str, #[case] code: &str, #[case] pointer: &str) {
    assert_eq!(
        errors(document(&format!("[{selector}]"), "[]").as_bytes()),
        error(code, pointer)
    );
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §rules
#[case::single_character_id(r#"{"Id":"a","Decision":"Allow","Select":{}}"#)]
#[case::id_with_every_allowed_character(r#"{"Id":"0a.b_c:d-E","Decision":"Allow","Select":{}}"#)]
#[case::disabled(r#"{"Id":"a","Enabled":false,"Decision":"Deny","Select":{}}"#)]
#[case::reason(r#"{"Id":"a","Decision":"Deny","Reason":"","Select":{}}"#)]
#[case::grants(r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"Interactive":{},"CustomParameters":{"Patterns":["/q*"],"Except":["/qn"]}}}"#)]
#[case::forbids(
    r#"{"Id":"a","Decision":"Deny","Select":{},"Forbids":{"CustomInstallLocation":{"Patterns":["C:/Windows/*"]}}}"#
)]
#[case::except(r#"{"Id":"a","Decision":"Allow","Select":{},"Except":[{"Packages":["Contoso.*"]}]}"#)]
fn valid_rules(#[case] rule: &str) {
    assert_eq!(errors(with_rule(rule).as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §rules
#[case::id_starting_with_dot(r#"{"Id":".a","Decision":"Allow","Select":{}}"#, "InvalidValue", "/Rules/0/Id")]
#[case::empty_id(r#"{"Id":"","Decision":"Allow","Select":{}}"#, "InvalidValue", "/Rules/0/Id")]
#[case::id_with_space(r#"{"Id":"a b","Decision":"Allow","Select":{}}"#, "InvalidValue", "/Rules/0/Id")]
#[case::id_non_ascii(r#"{"Id":"é","Decision":"Allow","Select":{}}"#, "InvalidValue", "/Rules/0/Id")]
#[case::id_number(r#"{"Id":1,"Decision":"Allow","Select":{}}"#, "WrongType", "/Rules/0/Id")]
#[case::lowercase_decision(r#"{"Id":"a","Decision":"allow","Select":{}}"#, "InvalidValue", "/Rules/0/Decision")]
#[case::missing_decision(r#"{"Id":"a","Select":{}}"#, "MissingKey", "/Rules/0/Decision")]
#[case::missing_select(r#"{"Id":"a","Decision":"Allow"}"#, "MissingKey", "/Rules/0/Select")]
#[case::enabled_string(
    r#"{"Id":"a","Enabled":"false","Decision":"Allow","Select":{}}"#,
    "WrongType",
    "/Rules/0/Enabled"
)]
#[case::grants_on_deny(
    r#"{"Id":"a","Decision":"Deny","Select":{},"Grants":{"Interactive":{}}}"#,
    "ConflictingKeys",
    "/Rules/0/Grants"
)]
#[case::forbids_on_allow(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Forbids":{"Interactive":{}}}"#,
    "ConflictingKeys",
    "/Rules/0/Forbids"
)]
#[case::empty_except_entry(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Except":[{}]}"#,
    "InvalidValue",
    "/Rules/0/Except/0"
)]
#[case::empty_except(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Except":[]}"#,
    "InvalidValue",
    "/Rules/0/Except"
)]
#[case::empty_grants(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{}}"#,
    "InvalidValue",
    "/Rules/0/Grants"
)]
#[case::unknown_opt_in(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"PreRelease":{}}}"#,
    "UnknownKey",
    "/Rules/0/Grants/PreRelease"
)]
#[case::grant_with_key(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"Interactive":{"Patterns":["*"]}}}"#,
    "UnknownKey",
    "/Rules/0/Grants/Interactive/Patterns"
)]
#[case::grant_true(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"Interactive":true}}"#,
    "WrongType",
    "/Rules/0/Grants/Interactive"
)]
#[case::grant_without_patterns(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"CustomParameters":{}}}"#,
    "MissingKey",
    "/Rules/0/Grants/CustomParameters/Patterns"
)]
#[case::empty_patterns(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"CustomParameters":{"Patterns":[]}}}"#,
    "InvalidValue",
    "/Rules/0/Grants/CustomParameters/Patterns"
)]
#[case::empty_pattern(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"CustomParameters":{"Patterns":[""]}}}"#,
    "InvalidValue",
    "/Rules/0/Grants/CustomParameters/Patterns/0"
)]
#[case::empty_grant_except(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Grants":{"CustomInstallLocation":{"Patterns":["*"],"Except":[]}}}"#,
    "InvalidValue",
    "/Rules/0/Grants/CustomInstallLocation/Except"
)]
#[case::grant_unknown_key(
    r#"{"Id":"a","Decision":"Deny","Select":{},"Forbids":{"CustomInstallLocation":{"Patterns":["*"],"Values":[]}}}"#,
    "UnknownKey",
    "/Rules/0/Forbids/CustomInstallLocation/Values"
)]
#[case::unknown_rule_key(
    r#"{"Id":"a","Decision":"Allow","Select":{},"Priority":1}"#,
    "UnknownKey",
    "/Rules/0/Priority"
)]
#[case::not_an_object(r#"[]"#, "WrongType", "/Rules/0")]
fn invalid_rules(#[case] rule: &str, #[case] code: &str, #[case] pointer: &str) {
    assert_eq!(errors(with_rule(rule).as_bytes()), error(code, pointer));
}

#[test]
fn later_rule_with_an_equal_id_is_a_duplicate() {
    // oracle: contract policies/CONTRACT.policy.md §rules
    let text = document(
        "[]",
        r#"[{"Id":"a","Decision":"Allow","Select":{}},{"Id":"b","Decision":"Allow","Select":{}},{"Id":"a","Decision":"Deny","Select":{}}]"#,
    );
    assert_eq!(errors(text.as_bytes()), error("DuplicateRuleId", "/Rules/2/Id"));
}

#[test]
fn rule_ids_differing_in_case_are_distinct() {
    // oracle: contract policies/CONTRACT.policy.md §text-comparison
    let text = document(
        "[]",
        r#"[{"Id":"a","Decision":"Allow","Select":{}},{"Id":"A","Decision":"Allow","Select":{}}]"#,
    );
    assert_eq!(errors(text.as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §selection
#[case::every_condition(r#"{"Operations":["Install","Update"],"Sources":[{"Manager":"Winget","Source":"winget"}],"Packages":["Contoso.*"],"Version":{"Minimum":"1.0","Maximum":"v2.0.0-rc.1+build","Prerelease":"Exclude"},"Scopes":["User","Machine"],"Architectures":["X86","X64","Arm64","Neutral"],"Elevation":"NonElevated"}"#)]
#[case::equal_bounds_written_differently(r#"{"Operations":["Install"],"Version":{"Minimum":"1.2","Maximum":"1.2.0"}}"#)]
#[case::only_prerelease(r#"{"Operations":["Update"],"Version":{"Prerelease":"Exclude"}}"#)]
#[case::uninstall_without_version(r#"{"Operations":["Uninstall"]}"#)]
fn valid_selections(#[case] selection: &str) {
    let rule = format!(r#"{{"Id":"a","Decision":"Allow","Select":{selection}}}"#);
    assert_eq!(errors(with_rule(&rule).as_bytes()), []);
}

#[rstest]
// oracle: contract policies/CONTRACT.policy.md §selection
#[case::empty_operations(r#"{"Operations":[]}"#, "InvalidValue", "/Rules/0/Select/Operations")]
#[case::lowercase_operation(r#"{"Operations":["install"]}"#, "InvalidValue", "/Rules/0/Select/Operations/0")]
#[case::operations_string(r#"{"Operations":"Install"}"#, "WrongType", "/Rules/0/Select/Operations")]
#[case::empty_sources(r#"{"Sources":[]}"#, "InvalidValue", "/Rules/0/Select/Sources")]
#[case::empty_packages(r#"{"Packages":[]}"#, "InvalidValue", "/Rules/0/Select/Packages")]
#[case::empty_package_pattern(r#"{"Packages":[""]}"#, "InvalidValue", "/Rules/0/Select/Packages/0")]
#[case::unknown_scope(r#"{"Scopes":["Global"]}"#, "InvalidValue", "/Rules/0/Select/Scopes/0")]
#[case::lowercase_architecture(r#"{"Architectures":["x64"]}"#, "InvalidValue", "/Rules/0/Select/Architectures/0")]
#[case::requested_elevation_value(r#"{"Elevation":"Standard"}"#, "InvalidValue", "/Rules/0/Select/Elevation")]
#[case::elevation_list(r#"{"Elevation":["Elevated"]}"#, "WrongType", "/Rules/0/Select/Elevation")]
#[case::unknown_condition(r#"{"Manager":"Winget"}"#, "UnknownKey", "/Rules/0/Select/Manager")]
#[case::version_without_operations(r#"{"Version":{"Minimum":"1.0"}}"#, "MissingKey", "/Rules/0/Select/Operations")]
#[case::version_with_uninstall(
    r#"{"Operations":["Install","Uninstall"],"Version":{"Minimum":"1.0"}}"#,
    "ConflictingKeys",
    "/Rules/0/Select/Version"
)]
#[case::empty_version_condition(
    r#"{"Operations":["Install"],"Version":{}}"#,
    "InvalidValue",
    "/Rules/0/Select/Version"
)]
#[case::minimum_not_a_version(
    r#"{"Operations":["Install"],"Version":{"Minimum":"latest"}}"#,
    "InvalidValue",
    "/Rules/0/Select/Version/Minimum"
)]
#[case::maximum_with_empty_pre_release_identifier(
    r#"{"Operations":["Install"],"Version":{"Maximum":"1.0.0-a..b"}}"#,
    "InvalidValue",
    "/Rules/0/Select/Version/Maximum"
)]
#[case::maximum_with_leading_zero_pre_release(
    r#"{"Operations":["Install"],"Version":{"Maximum":"1.0.0-01"}}"#,
    "InvalidValue",
    "/Rules/0/Select/Version/Maximum"
)]
#[case::minimum_above_maximum(
    r#"{"Operations":["Install"],"Version":{"Minimum":"1.10","Maximum":"1.9"}}"#,
    "InvalidValue",
    "/Rules/0/Select/Version/Maximum"
)]
#[case::release_above_its_pre_release(
    r#"{"Operations":["Install"],"Version":{"Minimum":"1.0.0","Maximum":"1.0.0-rc"}}"#,
    "InvalidValue",
    "/Rules/0/Select/Version/Maximum"
)]
#[case::prerelease_include(
    r#"{"Operations":["Install"],"Version":{"Prerelease":"Include"}}"#,
    "InvalidValue",
    "/Rules/0/Select/Version/Prerelease"
)]
#[case::minimum_number(
    r#"{"Operations":["Install"],"Version":{"Minimum":1}}"#,
    "WrongType",
    "/Rules/0/Select/Version/Minimum"
)]
fn invalid_selections(#[case] selection: &str, #[case] code: &str, #[case] pointer: &str) {
    let rule = format!(r#"{{"Id":"a","Decision":"Allow","Select":{selection}}}"#);
    assert_eq!(errors(with_rule(&rule).as_bytes()), error(code, pointer));
}

#[test]
fn version_condition_in_an_except_entry_needs_operations() {
    // oracle: contract policies/CONTRACT.policy.md §version-condition
    let rule = r#"{"Id":"a","Decision":"Allow","Select":{},"Except":[{"Version":{"Maximum":"1.0"}}]}"#;
    assert_eq!(
        errors(with_rule(rule).as_bytes()),
        error("MissingKey", "/Rules/0/Except/0/Operations")
    );
}
