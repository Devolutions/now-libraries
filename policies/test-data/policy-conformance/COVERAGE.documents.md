# Coverage: document validation

The cases in `documents/*.json` check how a reader validates a 2.x policy document.
Each section below names a contract anchor and the cases that check it; a name ending in `*` stands for every case with that prefix.
Every expected value comes from the cited anchor or a published source named in the case's `oracle` or `note`.

## Cases by anchor

### §json

- Size limit: `doc-json-size-at-limit-valid`, `doc-json-size-over-limit`.
- Size counts UTF-8 bytes, not characters: `doc-json-size-counts-utf8-bytes-at-limit-valid`, `doc-json-size-counts-utf8-bytes-over-limit`.
- Byte order mark ignored once and counted in the size: `doc-json-bom-accepted`, `doc-json-bom-counted-at-limit-valid`, `doc-json-bom-counted-over-limit`, `doc-json-two-boms-malformed`, `doc-json-bom-after-whitespace-malformed`, `doc-json-bom-only-malformed`.
- Not valid UTF-8: `doc-json-invalid-utf8-*`, `doc-json-utf16-encoded-malformed`, with controls `doc-json-utf8-four-byte-valid` and `doc-json-utf8-noncharacter-valid`.
- Top-level value not an object: `doc-json-top-level-*`.
- Not well-formed RFC 8259, including comments and trailing commas: `doc-json-empty-malformed`, `doc-json-whitespace-only-malformed` and the `doc-json-*-malformed` cases built from `doc-minimal-valid`.
- Unpaired surrogates in values and keys: `doc-json-unpaired-surrogate-*`, with controls `doc-json-surrogate-pair-valid`, `doc-json-escaped-nul-valid` and `doc-json-escaped-noncharacter-valid`.
- Surrogate pairs at the ends of both halves' ranges (U+10000, U+103FF, U+10FC00, U+10FFFF), escaped with uppercase hex digits: `doc-json-surrogate-pair-{first,low-dfff,high-dbff,last}-valid` in a value.
  The same pairs decoded in a key, observed through the pointer of an unknown key, `doc-json-surrogate-pair-{first,low-dfff,high-dbff,last}-key-unknown`, and through a `DuplicateKey` with the same code point written in UTF-8, `doc-json-surrogate-pair-{first,low-dfff,high-dbff,last}-key-duplicate`.
- Nesting depth 64 allowed and 65 malformed, for arrays, objects and both: `doc-json-depth-*`.
- Number magnitude and precision never malformed: `doc-json-huge-exponent-not-malformed`, `doc-json-long-precision-not-malformed`, `doc-meta-revision-huge-exponent-invalid`.
- Keys match after escapes are decoded, with exact characters: `doc-json-escaped-key-matches`, `doc-err-duplicate-key-escaped`, `doc-top-key-differing-in-case-unknown`, `doc-meta-key-differing-in-case-unknown`, `doc-grant-unknown-opt-in-lowercase`.
- `null` is the wrong type: `doc-top-*-null-wrong-type`, `doc-meta-*-null-wrong-type`, `doc-sel-null-condition-wrong-type` and the other `*-null-wrong-type` cases.
- Key order and whitespace carry no meaning: `doc-top-reordered-keys-and-whitespace-valid`, `doc-trust-any-source-first-conflict`, `doc-window-until-before-from-in-key-order-invalid`.
- Lists may repeat entries, except rules: `doc-trust-repeated-entry-valid`, `doc-rule-except-repeated-entry-valid`, `doc-sel-repeated-entries-valid`, `doc-rule-identical-rules-duplicate-id`.

### §timestamps

- Valid forms and bounds: `doc-time-*-valid`.
- Every other string is `InvalidValue`: `doc-time-*-invalid`, each naming its control in `note`.
- A non-string is `WrongType`: `doc-time-*-wrong-type`.
- Comparison as instants at nanosecond precision: `doc-window-offsets-compare-as-instants-*`, `doc-window-nanosecond-later-invalid`, `doc-window-fraction-trailing-zeros-equal-valid`, `doc-window-year-boundary-offsets-valid`, `doc-window-year-0001-offset-order-invalid`.
- Offsets with non-zero minutes, positive and negative, on either bound, including `-00:30`: `doc-window-offset-minutes-*`, in pairs of an `-equal-valid` case and a `-later-invalid` neighbour one nanosecond out.
  In each pair, one case's result changes when an implementation drops an offset's minutes or adds them with the wrong sign, as in reading `-09:30` as `-08:30` or `-00:30` as `+00:30`.

### §document-keys

- `doc-minimal-valid`, with empty `TrustedSources` and `Rules`.
- `doc-top-missing-*`, `doc-top-*-wrong-type`, `doc-top-unknown-key`, `doc-top-unknown-schema-key`.

### §format-version

- `doc-format-version-escaped-valid`, `doc-format-version-*-unsupported` (strings, and non-strings that are not `WrongType`), `doc-top-missing-policy-format-version`.

### §metadata

- `doc-meta-*`: every key missing, of the wrong type, empty where non-empty is required, and `Revision` at 1, at 2147483647, and outside the range or written with a fraction or an exponent.

### §validity-window

- `ValidFrom` later than `ValidUntil` is `InvalidValue` at `ValidUntil`: `doc-window-from-after-until-invalid`, `doc-window-until-before-from-in-key-order-invalid`, with controls `doc-window-equal-valid`, `doc-window-from-only-valid`, `doc-window-until-only-valid`.

### §trusted-sources and §source-selector

- `doc-trust-*` for selectors in `TrustedSources`.
- Any non-empty string is a valid `Source`, including names no readable request carries: `doc-trust-source-{129-bytes,leading-space,space-only,line-feed,nul,zero-width-space,powershell-wildcard,npm-other-name}-valid` and, in a selection, `doc-sel-sources-unreadable-name-valid` and `doc-sel-sources-other-npm-name-valid`; the rejected neighbour is `doc-trust-source-empty-invalid`.
- `doc-sel-sources-*` and `doc-sel-except-sources-missing-source` for selectors in a selection.
- `doc-trust-every-manager-valid` and `doc-trust-manager-*-invalid` check the manager identifiers of §managers.

### §rules

- `doc-rule-*`: `Id` syntax, `DuplicateRuleId`, `Decision`, `Enabled`, `Reason`, `Select`, `Except`, `Grants` and `Forbids` on the wrong decision or empty, and validation of disabled rules.

### §selection

- `doc-sel-*`: every condition with valid values, empty lists, single values where a list is required, entries outside §request-properties, unknown conditions, and the same checks inside `Except` entries (`doc-sel-except-*`).

### §version-condition

- `doc-vercond-*`: empty condition, `Prerelease` values, wrong types, unknown key, `Operations` required beside `Version` and without `Uninstall`, in `Select` and in `Except` entries.
- `Minimum` higher than `Maximum`: `doc-version-order-*-invalid`, with controls `doc-version-order-*-valid`.

### §version-scheme

- Grammar: `doc-version-*-valid` and `doc-version-*-invalid` other than the `doc-version-order-*` cases.
- Comparison, through `Minimum` and `Maximum`: `doc-version-order-*`, covering numeric components, missing trailing components, leading zeros, `v` prefix, pre-release precedence from SemVer 2.0.0 item 11, ASCII order of identifiers, build metadata, and numbers beyond 64 bits.

### §grants and §opt-ins

- Every opt-in as a grant and a forbid key: `doc-grant-every-opt-in-valid`, `doc-forbid-every-opt-in-valid`.
- Keys not in the closed set: `doc-grant-unknown-opt-in-*`, `doc-forbid-unknown-opt-in`.
- Grants without fields reject keys: `doc-grant-*-with-key-unknown`, `doc-forbid-no-key-opt-in-with-key-unknown`.
- `CustomParameters` and `CustomInstallLocation` grants and forbids: `doc-grant-params-*`, `doc-grant-location-*`, `doc-forbid-params-*`, `doc-forbid-location-*`.

### §patterns

- Non-empty: `doc-sel-packages-empty-pattern-invalid`, `doc-sel-packages-second-pattern-empty-invalid`, `doc-*-empty-pattern-invalid` in grants and forbids.
- Any non-empty string is a pattern: `doc-sel-packages-unusual-patterns-valid`, `doc-grant-location-star-pattern-valid`.

### §audit-mode

- The `Enforcement` object: `doc-enforcement-*`.

### §errors

- Every code with a single-defect case: `DocumentTooLarge` (`doc-json-size-over-limit`), `MalformedDocument` (`doc-json-*-malformed`), `DuplicateKey` (`doc-err-duplicate-key`), `UnsupportedFormatVersion` (`doc-format-version-*-unsupported`), `MissingKey`, `UnknownKey`, `ConflictingKeys`, `WrongType`, `InvalidValue` (throughout), `DuplicateRuleId` (`doc-rule-duplicate-id`).
- Pointer rules: the member for `DuplicateKey`, `UnknownKey` and `ConflictingKeys` (`doc-err-duplicate-key-*`, `doc-err-pointer-*`, `doc-trust-source-and-any-source-conflict`); the absent member for `MissingKey` (`doc-trust-neither-source-key-missing`, `doc-vercond-without-operations-missing`); the value for `WrongType`, `InvalidValue` and `UnsupportedFormatVersion`; the later rule's `Id` (`doc-rule-duplicate-id-not-adjacent`); the empty pointer for the whole document.
- RFC 6901 escaping: `doc-err-pointer-slash`, `doc-err-pointer-tilde`, `doc-err-pointer-tilde-one-literal`, `doc-err-pointer-empty-key`, `doc-err-pointer-escaped-solidus-key`, `doc-err-pointer-nested-escapes`, `doc-err-duplicate-key-over-unknown-key`.
- Array indexes: `doc-trust-second-entry-pointer`, `doc-rule-second-rule-pointer`, `doc-sel-operations-second-entry-invalid`.
- A defect in one entry of a list is reported at that entry, as "the value, which is the entry when the defect is in one entry of a list" states: every `WrongType` or `InvalidValue` case whose pointer ends in an index, such as `doc-sel-operations-second-entry-invalid`, `doc-rule-except-empty-selection-invalid`, `doc-grant-params-empty-pattern-invalid` and `doc-trust-entry-string-wrong-type`, each citing that text in `note`; a list that is empty is reported at the list, as in `doc-sel-sources-empty-invalid`.
- Precedence: `doc-err-too-large-over-*`, `doc-err-malformed-*`, `doc-err-duplicate-key-over-*`, `doc-err-duplicate-format-version-*`, `doc-err-duplicate-key-inside-unknown-value`, `doc-err-unsupported-*`, `doc-err-missing-format-version-over-other-errors`, `doc-err-empty-object-reports-format-version-only`, and `doc-json-unpaired-surrogate-in-key-malformed`.
- Several defects without precedence, as `someOf`: `doc-err-several-*`, `doc-err-unknown-key-and-invalid-decision`, `doc-err-selector-conflict-and-false`, `doc-err-grants-on-deny-and-empty-except`, `doc-err-version-without-operations-and-invalid-minimum`, `doc-err-duplicate-rule-id-and-invalid-decision`, `doc-err-two-duplicate-keys`, `doc-rule-three-equal-ids`.

## Readings these cases rely on

- The 64-level boundary cannot be reached by a valid document, because no valid structure nests that deep.
  The depth-64 cases are therefore documents whose one defect is an unknown key holding the nested value.
- A timestamp's year is constrained as written, so `0001-01-01T00:00:00+01:00` is valid although its instant falls in year 0000.

## Verified by review only

- §document-keys: the broker never changes a document it accepts.
- §json: writers may choose any key order and whitespace.
- §format-version: the reason a reader refuses an unknown version rather than reading part of it.
- §errors: the list of codes is closed; every case's expected errors use only listed codes, so a runner comparing exactly also rejects other codes.

## Met by the suite as a whole

- §agreement, for documents: both implementations passing these cases, which fix validity and, for one defect, the exact error and pointer, is the evidence that they agree.

## Left unchecked

- The meaning of a valid document belongs to the evaluation cases: the validity window at an evaluation time, trusted-source matching, rule applicability, grants and forbids, audit mode, and pre-release under `Winget`.
- `Grants` or `Forbids` on a rule whose `Decision` is missing or invalid is not checked.
  One reading reports only the `Decision` error, since §rules ties the conflict to a `Decision` of `Deny` or `Allow`; the other also reports `ConflictingKeys`, since no decision permits the key.
- A key repeated three times in one object is not checked, because §errors does not say whether that is one `DuplicateKey` error or two.
- A document over the size limit is checked only with spaces as padding, because the case format builds large inputs by padding.
- A `CustomInstallLocation` pattern that is not a path, such as `*`, is checked only for validity; how it is normalized and matched belongs to the evaluation cases.
