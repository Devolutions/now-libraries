//! Reading and validating a policy document ([§document], [§errors]).
//!
//! [§document]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#document
//! [§errors]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#errors

use core::fmt;
use std::collections::HashSet;

use crate::json::{self, Value};
use crate::reading::normalize_location_pattern;
use crate::timestamp::Timestamp;
use crate::values::{Architecture, Elevation, Manager, Operation, OptIn, RuleDecision, Scope};
use crate::version::Version;

/// The largest document, in bytes, counting a byte order mark ([§json]).
///
/// [§json]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#json
const MAX_DOCUMENT_SIZE: usize = 4 * 1024 * 1024;

const UTF8_BOM: &[u8] = b"\xEF\xBB\xBF";

const SUPPORTED_FORMAT_VERSION: &str = "2.0.0";

/// A valid policy document in format 2.x.
#[derive(Debug, Clone)]
pub struct Policy {
    pub(crate) valid_from: Option<Timestamp>,
    pub(crate) valid_until: Option<Timestamp>,
    pub(crate) audit_mode: bool,
    pub(crate) trusted_sources: Vec<SourceSelector>,
    pub(crate) rules: Vec<Rule>,
}

#[derive(Debug, Clone)]
pub(crate) struct SourceSelector {
    pub(crate) manager: Manager,
    /// The source name, or `None` for `AnySource`.
    pub(crate) source: Option<String>,
}

#[derive(Debug, Clone)]
pub(crate) struct Rule {
    pub(crate) id: String,
    pub(crate) enabled: bool,
    pub(crate) decision: RuleDecision,
    pub(crate) reason: Option<String>,
    pub(crate) select: Selection,
    pub(crate) except: Vec<Selection>,
    /// The rule's grants on an Allow rule, or its forbids on a Deny rule; `None` when the rule has neither key.
    pub(crate) opt_ins: Option<Vec<(OptIn, OptInPatterns)>>,
}

/// The `Patterns` and `Except` of a grant or forbid; both are empty for an opt-in whose grant has no keys.
#[derive(Debug, Clone, Default)]
pub(crate) struct OptInPatterns {
    pub(crate) patterns: Vec<String>,
    pub(crate) except: Vec<String>,
}

#[derive(Debug, Clone, Default)]
pub(crate) struct Selection {
    pub(crate) operations: Option<Vec<Operation>>,
    pub(crate) sources: Option<Vec<SourceSelector>>,
    pub(crate) packages: Option<Vec<String>>,
    pub(crate) version: Option<VersionCondition>,
    pub(crate) scopes: Option<Vec<Scope>>,
    pub(crate) architectures: Option<Vec<Architecture>>,
    pub(crate) elevation: Option<Elevation>,
}

#[derive(Debug, Clone, Default)]
pub(crate) struct VersionCondition {
    pub(crate) minimum: Option<Version>,
    pub(crate) maximum: Option<Version>,
    pub(crate) exclude_prerelease: bool,
}

/// A validation error code ([§errors]).
///
/// [§errors]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#errors
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum ErrorCode {
    DocumentTooLarge,
    MalformedDocument,
    DuplicateKey,
    UnsupportedFormatVersion,
    MissingKey,
    UnknownKey,
    ConflictingKeys,
    WrongType,
    InvalidValue,
    DuplicateRuleId,
}

impl ErrorCode {
    /// Returns the contract's name for the code.
    pub fn as_str(self) -> &'static str {
        match self {
            Self::DocumentTooLarge => "DocumentTooLarge",
            Self::MalformedDocument => "MalformedDocument",
            Self::DuplicateKey => "DuplicateKey",
            Self::UnsupportedFormatVersion => "UnsupportedFormatVersion",
            Self::MissingKey => "MissingKey",
            Self::UnknownKey => "UnknownKey",
            Self::ConflictingKeys => "ConflictingKeys",
            Self::WrongType => "WrongType",
            Self::InvalidValue => "InvalidValue",
            Self::DuplicateRuleId => "DuplicateRuleId",
        }
    }
}

impl fmt::Display for ErrorCode {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.as_str())
    }
}

/// A validation error: its code and an RFC 6901 JSON Pointer to where it is, `""` for the whole document.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct ValidationError {
    pub code: ErrorCode,
    pub pointer: String,
}

impl fmt::Display for ValidationError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{} at \"{}\"", self.code, self.pointer)
    }
}

impl core::error::Error for ValidationError {}

/// Reads the bytes of a policy document.
///
/// Returns the policy when the document is valid, and otherwise a non-empty list of its validation errors.
pub fn parse_policy(bytes: &[u8]) -> Result<Policy, Vec<ValidationError>> {
    if !(bytes.len() <= MAX_DOCUMENT_SIZE) {
        return Err(vec![ValidationError {
            code: ErrorCode::DocumentTooLarge,
            pointer: String::new(),
        }]);
    }

    let malformed = || {
        vec![ValidationError {
            code: ErrorCode::MalformedDocument,
            pointer: String::new(),
        }]
    };

    let bytes = bytes.strip_prefix(UTF8_BOM).unwrap_or(bytes);
    let text = core::str::from_utf8(bytes).map_err(|_| malformed())?;
    let document = json::parse(text).map_err(|_| malformed())?;

    let Value::Object(top) = &document.value else {
        return Err(malformed());
    };

    if !document.duplicate_keys.is_empty() {
        return Err(document
            .duplicate_keys
            .into_iter()
            .map(|pointer| ValidationError {
                code: ErrorCode::DuplicateKey,
                pointer,
            })
            .collect());
    }

    let top = Members {
        members: top,
        pointer: String::new(),
    };

    let mut v = Validator { errors: Vec::new() };

    match top.get("PolicyFormatVersion") {
        None => {
            v.error(ErrorCode::MissingKey, top.member_pointer("PolicyFormatVersion"));
            return Err(v.errors);
        }
        Some((Value::String(format_version), _)) if format_version == SUPPORTED_FORMAT_VERSION => {}
        Some((_, pointer)) => {
            v.error(ErrorCode::UnsupportedFormatVersion, pointer);
            return Err(v.errors);
        }
    }

    let policy = v.policy(&top);

    if v.errors.is_empty() { Ok(policy) } else { Err(v.errors) }
}

/// The members of an object, with the pointer of the object.
struct Members<'a> {
    members: &'a [(String, Value)],
    pointer: String,
}

impl<'a> Members<'a> {
    fn member_pointer(&self, key: &str) -> String {
        let mut pointer = self.pointer.clone();
        json::push_token(&mut pointer, key);
        pointer
    }

    fn get(&self, key: &str) -> Option<(&'a Value, String)> {
        self.members
            .iter()
            .find(|(name, _)| name == key)
            .map(|(_, value)| (value, self.member_pointer(key)))
    }

    fn has(&self, key: &str) -> bool {
        self.members.iter().any(|(name, _)| name == key)
    }

    fn is_empty(&self) -> bool {
        self.members.is_empty()
    }
}

struct Validator {
    errors: Vec<ValidationError>,
}

impl Validator {
    fn error(&mut self, code: ErrorCode, pointer: String) {
        self.errors.push(ValidationError { code, pointer });
    }

    /// Reports `UnknownKey` for each member not in `known`.
    fn known_keys(&mut self, object: &Members<'_>, known: &[&str]) {
        for (key, _) in object.members {
            if !known.contains(&key.as_str()) {
                self.error(ErrorCode::UnknownKey, object.member_pointer(key));
            }
        }
    }

    fn required<'a>(&mut self, object: &Members<'a>, key: &str) -> Option<(&'a Value, String)> {
        let member = object.get(key);
        if member.is_none() {
            self.error(ErrorCode::MissingKey, object.member_pointer(key));
        }
        member
    }

    fn object<'a>(&mut self, value: &'a Value, pointer: String) -> Option<Members<'a>> {
        match value {
            Value::Object(members) => Some(Members { members, pointer }),
            _ => {
                self.error(ErrorCode::WrongType, pointer);
                None
            }
        }
    }

    fn string<'a>(&mut self, value: &'a Value, pointer: String) -> Option<&'a str> {
        match value {
            Value::String(text) => Some(text),
            _ => {
                self.error(ErrorCode::WrongType, pointer);
                None
            }
        }
    }

    fn non_empty_string(&mut self, value: &Value, pointer: String) -> Option<String> {
        let text = self.string(value, pointer.clone())?;
        if text.is_empty() {
            self.error(ErrorCode::InvalidValue, pointer);
            return None;
        }
        Some(text.to_owned())
    }

    fn boolean(&mut self, value: &Value, pointer: String) -> Option<bool> {
        match value {
            Value::Bool(flag) => Some(*flag),
            _ => {
                self.error(ErrorCode::WrongType, pointer);
                None
            }
        }
    }

    fn timestamp(&mut self, value: &Value, pointer: String) -> Option<Timestamp> {
        let text = self.string(value, pointer.clone())?;
        let timestamp = text.parse().ok();
        if timestamp.is_none() {
            self.error(ErrorCode::InvalidValue, pointer);
        }
        timestamp
    }

    /// Reads a list, reporting `InvalidValue` when it is empty and `non_empty` is set, and returns each element with its pointer.
    fn list<'a>(&mut self, value: &'a Value, pointer: String, non_empty: bool) -> Option<Vec<(&'a Value, String)>> {
        let Value::Array(elements) = value else {
            self.error(ErrorCode::WrongType, pointer);
            return None;
        };

        if non_empty && elements.is_empty() {
            self.error(ErrorCode::InvalidValue, pointer);
            return None;
        }

        Some(
            elements
                .iter()
                .enumerate()
                .map(|(idx, element)| {
                    let mut element_pointer = pointer.clone();
                    json::push_token(&mut element_pointer, &idx.to_string());
                    (element, element_pointer)
                })
                .collect(),
        )
    }

    /// Reads a non-empty list of values of a closed set.
    fn closed_list<T>(&mut self, value: &Value, pointer: String, from_name: fn(&str) -> Option<T>) -> Option<Vec<T>> {
        let elements = self.list(value, pointer, true)?;
        let mut values = Vec::with_capacity(elements.len());
        for (element, element_pointer) in elements {
            if let Some(value) = self.closed_value(element, element_pointer, from_name) {
                values.push(value);
            }
        }
        Some(values)
    }

    fn closed_value<T>(&mut self, value: &Value, pointer: String, from_name: fn(&str) -> Option<T>) -> Option<T> {
        let name = self.string(value, pointer.clone())?;
        let value = from_name(name);
        if value.is_none() {
            self.error(ErrorCode::InvalidValue, pointer);
        }
        value
    }

    /// Reads a non-empty list of patterns ([§patterns]).
    ///
    /// [§patterns]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#patterns
    fn patterns(&mut self, value: &Value, pointer: String) -> Vec<String> {
        let mut patterns = Vec::new();
        for (element, element_pointer) in self.list(value, pointer, true).unwrap_or_default() {
            if let Some(pattern) = self.non_empty_string(element, element_pointer) {
                patterns.push(pattern);
            }
        }
        patterns
    }

    fn policy(&mut self, top: &Members<'_>) -> Policy {
        self.known_keys(
            top,
            &[
                "PolicyFormatVersion",
                "Metadata",
                "Enforcement",
                "TrustedSources",
                "Rules",
            ],
        );

        let (valid_from, valid_until) = match self.required(top, "Metadata") {
            Some((value, pointer)) => self.metadata(value, pointer),
            None => (None, None),
        };

        let audit_mode = match top.get("Enforcement") {
            Some((value, pointer)) => self.enforcement(value, pointer),
            None => false,
        };

        let mut trusted_sources = Vec::new();
        if let Some((value, pointer)) = self.required(top, "TrustedSources") {
            for (element, element_pointer) in self.list(value, pointer, false).unwrap_or_default() {
                if let Some(selector) = self.source_selector(element, element_pointer) {
                    trusted_sources.push(selector);
                }
            }
        }

        let mut rules = Vec::new();
        if let Some((value, pointer)) = self.required(top, "Rules") {
            let mut ids = HashSet::new();
            for (element, element_pointer) in self.list(value, pointer, false).unwrap_or_default() {
                if let Some(rule) = self.rule(element, element_pointer, &mut ids) {
                    rules.push(rule);
                }
            }
        }

        Policy {
            valid_from,
            valid_until,
            audit_mode,
            trusted_sources,
            rules,
        }
    }

    /// Validates the metadata and returns its validity window ([§metadata]).
    ///
    /// [§metadata]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#metadata
    fn metadata(&mut self, value: &Value, pointer: String) -> (Option<Timestamp>, Option<Timestamp>) {
        let Some(metadata) = self.object(value, pointer) else {
            return (None, None);
        };

        self.known_keys(
            &metadata,
            &[
                "Id",
                "Publisher",
                "Revision",
                "PublishedAt",
                "ValidFrom",
                "ValidUntil",
                "Description",
                "SupportUrl",
            ],
        );

        for key in ["Id", "Publisher"] {
            if let Some((value, pointer)) = self.required(&metadata, key) {
                self.non_empty_string(value, pointer);
            }
        }

        if let Some((value, pointer)) = self.required(&metadata, "Revision") {
            match value {
                Value::Number(spelling) => {
                    if !is_revision(spelling) {
                        self.error(ErrorCode::InvalidValue, pointer);
                    }
                }
                _ => self.error(ErrorCode::WrongType, pointer),
            }
        }

        if let Some((value, pointer)) = self.required(&metadata, "PublishedAt") {
            self.timestamp(value, pointer);
        }

        let valid_from = metadata
            .get("ValidFrom")
            .and_then(|(value, pointer)| self.timestamp(value, pointer));
        let valid_until = metadata.get("ValidUntil").and_then(|(value, pointer)| {
            let valid_until = self.timestamp(value, pointer.clone())?;
            if valid_from.is_some_and(|valid_from| valid_until < valid_from) {
                self.error(ErrorCode::InvalidValue, pointer);
            }
            Some(valid_until)
        });

        if let Some((value, pointer)) = metadata.get("Description") {
            self.string(value, pointer);
        }

        if let Some((value, pointer)) = metadata.get("SupportUrl") {
            self.non_empty_string(value, pointer);
        }

        (valid_from, valid_until)
    }

    /// Validates `Enforcement` and returns its audit mode ([§audit-mode]).
    ///
    /// [§audit-mode]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#audit-mode
    fn enforcement(&mut self, value: &Value, pointer: String) -> bool {
        let Some(enforcement) = self.object(value, pointer) else {
            return false;
        };

        self.known_keys(&enforcement, &["AuditMode"]);

        enforcement
            .get("AuditMode")
            .and_then(|(value, pointer)| self.boolean(value, pointer))
            .unwrap_or(false)
    }

    /// Reads a source selector ([§source-selector]).
    ///
    /// [§source-selector]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#source-selector
    fn source_selector(&mut self, value: &Value, pointer: String) -> Option<SourceSelector> {
        let selector = self.object(value, pointer)?;

        self.known_keys(&selector, &["Manager", "Source", "AnySource"]);

        let manager = self
            .required(&selector, "Manager")
            .and_then(|(value, pointer)| self.closed_value(value, pointer, Manager::from_name));

        let source = selector
            .get("Source")
            .and_then(|(value, pointer)| self.non_empty_string(value, pointer));

        if let Some((value, pointer)) = selector.get("AnySource") {
            if self.boolean(value, pointer.clone()) == Some(false) {
                self.error(ErrorCode::InvalidValue, pointer.clone());
            }
            if selector.has("Source") {
                self.error(ErrorCode::ConflictingKeys, pointer);
            }
        } else if !selector.has("Source") {
            self.error(ErrorCode::MissingKey, selector.member_pointer("Source"));
        }

        Some(SourceSelector {
            manager: manager?,
            source,
        })
    }

    /// Reads a rule ([§rules]), recording its id in `ids`.
    ///
    /// [§rules]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#rules
    fn rule(&mut self, value: &Value, pointer: String, ids: &mut HashSet<String>) -> Option<Rule> {
        let rule = self.object(value, pointer)?;

        self.known_keys(
            &rule,
            &[
                "Id", "Enabled", "Decision", "Reason", "Select", "Except", "Grants", "Forbids",
            ],
        );

        let id = self.required(&rule, "Id").and_then(|(value, pointer)| {
            let id = self.string(value, pointer.clone())?;
            if !is_rule_id(id) {
                self.error(ErrorCode::InvalidValue, pointer.clone());
            }
            if !ids.insert(id.to_owned()) {
                self.error(ErrorCode::DuplicateRuleId, pointer);
            }
            Some(id.to_owned())
        });

        let enabled = rule
            .get("Enabled")
            .and_then(|(value, pointer)| self.boolean(value, pointer))
            .unwrap_or(true);

        let decision = self
            .required(&rule, "Decision")
            .and_then(|(value, pointer)| self.closed_value(value, pointer, RuleDecision::from_name));

        let reason = rule
            .get("Reason")
            .and_then(|(value, pointer)| self.string(value, pointer))
            .map(str::to_owned);

        let select = self
            .required(&rule, "Select")
            .and_then(|(value, pointer)| self.selection(value, pointer))
            .unwrap_or_default();

        let mut except = Vec::new();
        if let Some((value, pointer)) = rule.get("Except") {
            for (element, element_pointer) in self.list(value, pointer, true).unwrap_or_default() {
                if matches!(element, Value::Object(members) if members.is_empty()) {
                    self.error(ErrorCode::InvalidValue, element_pointer);
                } else if let Some(selection) = self.selection(element, element_pointer) {
                    except.push(selection);
                }
            }
        }

        let mut opt_ins = None;
        for (key, conflicting_decision) in [("Grants", RuleDecision::Deny), ("Forbids", RuleDecision::Allow)] {
            if let Some((value, pointer)) = rule.get(key) {
                if decision == Some(conflicting_decision) {
                    self.error(ErrorCode::ConflictingKeys, pointer.clone());
                }
                opt_ins = self.opt_in_patterns(value, pointer);
            }
        }

        Some(Rule {
            id: id?,
            enabled,
            decision: decision?,
            reason,
            select,
            except,
            opt_ins,
        })
    }

    /// Reads a selection ([§selection]).
    ///
    /// [§selection]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#selection
    fn selection(&mut self, value: &Value, pointer: String) -> Option<Selection> {
        let object = self.object(value, pointer)?;

        self.known_keys(
            &object,
            &[
                "Operations",
                "Sources",
                "Packages",
                "Version",
                "Scopes",
                "Architectures",
                "Elevation",
            ],
        );

        let mut selection = Selection {
            operations: object
                .get("Operations")
                .and_then(|(value, pointer)| self.closed_list(value, pointer, Operation::from_name)),
            ..Selection::default()
        };

        if let Some((value, pointer)) = object.get("Sources") {
            let mut sources = Vec::new();
            for (element, element_pointer) in self.list(value, pointer, true).unwrap_or_default() {
                if let Some(selector) = self.source_selector(element, element_pointer) {
                    sources.push(selector);
                }
            }
            selection.sources = Some(sources);
        }

        selection.packages = object
            .get("Packages")
            .map(|(value, pointer)| self.patterns(value, pointer));

        if let Some((value, pointer)) = object.get("Version") {
            selection.version = self.version_condition(value, pointer.clone());

            if !object.has("Operations") {
                self.error(ErrorCode::MissingKey, object.member_pointer("Operations"));
            } else if selection
                .operations
                .as_ref()
                .is_some_and(|operations| operations.contains(&Operation::Uninstall))
            {
                self.error(ErrorCode::ConflictingKeys, pointer);
            }
        }

        selection.scopes = object
            .get("Scopes")
            .and_then(|(value, pointer)| self.closed_list(value, pointer, Scope::from_name));

        selection.architectures = object
            .get("Architectures")
            .and_then(|(value, pointer)| self.closed_list(value, pointer, Architecture::from_name));

        selection.elevation = object
            .get("Elevation")
            .and_then(|(value, pointer)| self.closed_value(value, pointer, Elevation::from_name));

        Some(selection)
    }

    /// Reads a version condition ([§version-condition]).
    ///
    /// [§version-condition]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#version-condition
    fn version_condition(&mut self, value: &Value, pointer: String) -> Option<VersionCondition> {
        let object = self.object(value, pointer.clone())?;

        self.known_keys(&object, &["Minimum", "Maximum", "Prerelease"]);

        if !(object.has("Minimum") || object.has("Maximum") || object.has("Prerelease")) {
            self.error(ErrorCode::InvalidValue, pointer);
        }

        let mut version = |key: &str| {
            let (value, pointer) = object.get(key)?;
            let text = self.string(value, pointer.clone())?;
            let version = Version::parse(text);
            if version.is_none() {
                self.error(ErrorCode::InvalidValue, pointer);
            }
            version
        };

        let minimum = version("Minimum");
        let maximum = version("Maximum");

        if let (Some(minimum), Some(maximum)) = (&minimum, &maximum)
            && maximum < minimum
        {
            let (_, pointer) = object.get("Maximum")?;
            self.error(ErrorCode::InvalidValue, pointer);
        }

        let exclude_prerelease = match object.get("Prerelease") {
            Some((value, pointer)) => {
                let text = self.string(value, pointer.clone());
                if text.is_some_and(|text| text != "Exclude") {
                    self.error(ErrorCode::InvalidValue, pointer);
                }
                true
            }
            None => false,
        };

        Some(VersionCondition {
            minimum,
            maximum,
            exclude_prerelease,
        })
    }

    /// Reads a `Grants` or `Forbids` object ([§grants]).
    ///
    /// [§grants]: https://github.com/Devolutions/now-libraries/blob/master/policies/CONTRACT.policy.md#grants
    fn opt_in_patterns(&mut self, value: &Value, pointer: String) -> Option<Vec<(OptIn, OptInPatterns)>> {
        let object = self.object(value, pointer.clone())?;

        if object.is_empty() {
            self.error(ErrorCode::InvalidValue, pointer);
            return None;
        }

        let mut entries = Vec::new();
        for (key, value) in object.members {
            let member_pointer = object.member_pointer(key);
            let Some(opt_in) = OptIn::from_name(key) else {
                self.error(ErrorCode::UnknownKey, member_pointer);
                continue;
            };
            let Some(grant) = self.object(value, member_pointer) else {
                continue;
            };

            if !opt_in.has_patterns() {
                self.known_keys(&grant, &[]);
                entries.push((opt_in, OptInPatterns::default()));
                continue;
            }

            self.known_keys(&grant, &["Patterns", "Except"]);

            let normalize = |patterns: Vec<String>| -> Vec<String> {
                if opt_in == OptIn::CustomInstallLocation {
                    patterns
                        .iter()
                        .map(|pattern| normalize_location_pattern(pattern))
                        .collect()
                } else {
                    patterns
                }
            };

            let patterns = self
                .required(&grant, "Patterns")
                .map(|(value, pointer)| self.patterns(value, pointer))
                .unwrap_or_default();
            let except = grant
                .get("Except")
                .map(|(value, pointer)| self.patterns(value, pointer))
                .unwrap_or_default();

            entries.push((
                opt_in,
                OptInPatterns {
                    patterns: normalize(patterns),
                    except: normalize(except),
                },
            ));
        }

        Some(entries)
    }
}

/// Returns whether a number spelled `spelling` is an integer from 1 to 2147483647 without a fraction or an exponent.
fn is_revision(spelling: &str) -> bool {
    if spelling.starts_with('-') || !spelling.bytes().all(|b| b.is_ascii_digit()) {
        return false;
    }
    // The spelling is all digits and the reader accepts no leading zero in the integer part of a number other than `0`,
    // so a longer spelling is a larger number.
    spelling.len() <= 10
        && spelling
            .parse::<u64>()
            .is_ok_and(|revision| (1..=2_147_483_647).contains(&revision))
}

/// Returns whether `id` is an ASCII letter or digit followed by ASCII letters, digits, `.`, `_`, `:` and `-`.
fn is_rule_id(id: &str) -> bool {
    let mut bytes = id.bytes();
    bytes.next().is_some_and(|first| first.is_ascii_alphanumeric())
        && bytes.all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'_' | b':' | b'-'))
}
