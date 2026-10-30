# Coverage: evaluation semantics

Cases in `evaluation/eval-*.json` that check how a valid policy decides a request, by section of `policies/CONTRACT.policy.md`.
Requests go through `Winget` unless a case needs another manager's reading to make a property unknown or a version a pre-release, and every request is well-formed under `policies/CONTRACT.wire.md` §package-request.
Outside `eval-carriage.json`, the `OptIns` keys `Interactive`, `SkipHashCheck`, `CustomInstallLocation`, `UninstallPrevious` and `NoUpgrade` appear only in `Winget` requests; `eval-carriage.json` checks them under every manager and operation ([Opt-in carriage](#opt-in-carriage)).

## Cases by section

### §text-comparison

File `eval-text-and-patterns.json`.
Every Unicode case cites its `UnicodeData.txt` 16.0.0 line in `note`.

- ASCII case and control: `eval-text-ascii-case-ignored`, `eval-text-different-text-not-equal`.
- Simple uppercase mappings that equate: `eval-text-long-s-equals-s` (U+017F), `eval-text-s-equals-long-s-pattern`, `eval-text-dotless-i-equals-i` (U+0131), `eval-text-final-sigma-equals-sigma` (U+03C2), `eval-text-micro-sign-equals-mu` (U+00B5), `eval-text-deseret-small-equals-capital` (U+10428).
- Code points without a simple uppercase mapping, which lowercasing or case folding would equate: `eval-text-dotted-capital-i-not-i` (U+0130), `eval-text-kelvin-sign-not-k` (U+212A), `eval-text-sharp-s-not-ss` (U+00DF), `eval-text-sharp-s-not-capital-sharp-s` (U+00DF and U+1E9E).
- Code points whose full uppercase in `SpecialCasing.txt` 16.0.0 differs from their simple one, which do not equal their full uppercase, each citing both lines: `eval-text-sharp-s-not-full-uppercase-ss` (U+00DF), `eval-text-ligature-ff-not-full-uppercase-ff` (U+FB00), `eval-text-n-apostrophe-not-full-uppercase` (U+0149), `eval-text-alpha-ypogegrammeni-not-full-uppercase` (U+1F80), `eval-text-alpha-prosgegrammeni-not-full-uppercase` (U+1F88).
  Controls: `eval-text-ss-equals-uppercase-ss`, `eval-text-ff-equals-uppercase-ff`, `eval-text-apostrophe-n-equals-uppercase`, `eval-text-alpha-psili-iota-equals-uppercase`, and `eval-text-alpha-ypogegrammeni-equals-simple-uppercase`, where the simple mapping of U+1F80 applies.
- No normalization: `eval-text-no-normalization`.
- Source names: `eval-text-source-name-case-ignored`, `eval-text-source-name-dotless-i`, `eval-text-source-name-dotted-capital-i`.
- A selector with U+00DF against a source `STRASSE`: `eval-text-source-name-selector-sharp-s-not-ss`, control `eval-text-source-name-ss-equals-uppercase-ss`.
  The non-ASCII characters sit in the selector, since a `Winget` source name is a plain source name (§manager-winget).
- Install locations: `eval-text-install-location-long-s`.

### §patterns

File `eval-text-and-patterns.json`, observed through `CustomParameters` grants.

- `*` placement: `eval-pattern-star-matches-empty`, `eval-pattern-star-matches-sequence`, `eval-pattern-leading-star`, `eval-pattern-only-star`, `eval-pattern-star-ignores-case`, `eval-pattern-stars-in-order`, `eval-pattern-stars-out-of-order`, `eval-pattern-star-backtracks`, `eval-pattern-literals-do-not-overlap`.
- Whole value: `eval-pattern-whole-value-not-prefix`, `eval-pattern-whole-value-not-suffix`.
- No other wildcard and no escape: `eval-pattern-question-mark-is-literal`, `eval-pattern-question-mark-matches-itself`, `eval-pattern-brackets-are-literal`, `eval-pattern-brackets-match-themselves`, `eval-pattern-backslash-does-not-escape-star`, `eval-pattern-backslash-is-literal`.
- `*` across install location separators: `eval-grants-location-star-crosses-separators`.

### §timestamps and §validity-window

File `eval-validity-window.json`.

- Inclusive ends and their neighbours one nanosecond out: `eval-window-at-valid-from`, `eval-window-before-valid-from`, `eval-window-at-valid-until`, `eval-window-after-valid-until`, `eval-window-inside`.
- Open ends: `eval-window-open-start`, `eval-window-open-end`.
- Instants across offsets, where text order and instant order disagree: `eval-window-offset-same-instant`, `eval-window-offset-later-instant-earlier-text`, `eval-window-offset-earlier-instant-later-text`, `eval-window-negative-offset-until-inside`, `eval-window-negative-offset-until-after`.
- Nanosecond precision, which 100 ns ticks lose: `eval-window-nanosecond-until-equal`, `eval-window-nanosecond-until-after`, `eval-window-nanosecond-before-from`, `eval-window-fraction-digits-differ`.
- Offsets with non-zero minutes, on `ValidFrom`, `ValidUntil` and the evaluation time, positive and negative, including `-00:30` and `+23:59`: `eval-window-offset-minutes-*`, in pairs of an `-at` case and a neighbour one nanosecond out.
  In each pair, one case's decision changes when an implementation drops an offset's minutes or adds them with the wrong sign, as in reading `-09:30` as `-08:30` or `-00:30` as `+00:30`.
- Offsets that move an instant outside years 0001 to 9999: `eval-window-offset-before-year-one`, `eval-window-offset-after-year-9999`.

### §trusted-sources

File `eval-trusted-sources.json`.

- Trusted Update, the control of `eval-untrusted-update`: `eval-trusted-update`.
- Not trusted: `eval-untrusted-other-source`, `eval-untrusted-update`, `eval-untrusted-empty-list`, `eval-untrusted-install-from-source-only-rule-names`.
- Uninstalls ignore the list: `eval-trusted-uninstall-ignores-empty-list`, `eval-trusted-uninstall-from-source-only-rule-names`; a rule naming a source the list does not trust lists Install and Update (§selection), so it does not select the Uninstall.
- Precedence and audit mode: `eval-precedence-untrusted-source-holds`, `eval-audit-untrusted-source`.
- A `Source` selector unknown for an Update, in an `Except` entry or beside `AnySource`, in `reading-managers.json`: an Allow rule whose `Except` names the source is not applicable, `read-powershell-update-allow-except-source`, control `read-powershell-update-allow-except-other-manager`; a Deny rule whose `Except` names it applies, `read-powershell-update-deny-except-source`, control `read-powershell-install-deny-except-source`; a `Sources` list with both selectors matches, `read-powershell-update-source-and-any-source-allow`.

### §source-selector

- `AnySource`: `eval-trusted-any-source`, `eval-selection-sources-any-source`; against the unknown source of an Update, `read-vcpkg-update-any-source-allow` in `reading-managers.json` and `read-{scoop,powershell,powershell7}-update-any-source-allow`; on an Uninstall under every manager, `read-{winget,pip,npm,bun,cargo,vcpkg,chocolatey,dotnet,scoop,powershell,powershell7}-uninstall-any-source-allow`, which show that `AnySource` selects an Uninstall, not that its source is unknown; an `AnySource` Allow selects a WinGet Uninstall where a selection naming the source, with Install and Update, does not: `eval-selection-sources-any-source-uninstall`, `eval-selection-sources-named-excludes-uninstall`.
- `Source`, known and unknown: `eval-trusted-named-source`, `eval-trusted-named-source-known-npm-source`, `eval-applicability-list-unknown-entry`, and the unknown sources of Scoop, PowerShell, PowerShell7 and Vcpkg Updates, `read-{scoop,powershell,powershell7,vcpkg}-update-source-selector-{allow,deny}`, against the known Install sources of `read-{winget,pip,npm,bun,cargo,vcpkg}-install-source-selector-{allow,deny}`.
- An unknown source in the trusted sources check: a Scoop, PowerShell, PowerShell7 or Vcpkg Update is untrusted under a list that names its source, `read-{scoop,powershell,powershell7,vcpkg}-update-trusted-named-source`, and trusted under `AnySource`, `read-{scoop,powershell,powershell7,vcpkg}-update-any-source-allow`.
- A Scoop manifest source, compared with exact characters and unknown when only case differs: the `read-scoop-manifest-selector-*` and `read-scoop-manifest-trusted-*` cases of `reading-scoop.json`; spellings that let another text fetch the same manifest, a `%`, a `#`, a host part that is empty, contains `:` or ends with `.`, and a `.` or `..` segment, never reach the comparison, as the request is unreadable: `read-scoop-manifest-{percent,fragment,port,empty-port,empty-host,host-trailing-dot}*` and `read-scoop-manifest-segment-{dot,dot-dot}` there (see `COVERAGE.reading.md` §manager-scoop).
- `Source` compares ignoring case as §text-comparison defines, under `Winget` too: `eval-text-source-name-case-ignored`, `eval-text-source-name-dotless-i`, `eval-text-source-name-dotted-capital-i`.
- Another manager: `eval-untrusted-other-manager-any-source`, `eval-untrusted-other-manager-same-source-name`, `eval-selection-sources-other-manager`, `eval-applicability-other-manager-selector-unknown-source`.

### §rules

File `eval-rules.json`.

- `Enabled`: `eval-rules-disabled-allow-not-applicable`, `eval-rules-enabled-true-allow`, `eval-rules-enabled-absent-allow`, `eval-rules-disabled-allow-grants-nothing`, `eval-rules-disabled-deny-not-applicable`, `eval-rules-enabled-deny-applies`, `eval-decision-default-deny-disabled-rule`.
- Allow without `Grants`: `eval-rules-allow-without-grants-plain`, `eval-rules-allow-without-grants-opt-in`.
- Deny without `Forbids`: `eval-rules-deny-without-forbids-plain`, `eval-rules-deny-without-forbids-opt-in`.
- Deny with `Forbids`: `eval-rules-deny-forbids-plain-allowed`, `eval-rules-deny-forbids-opt-in-used`, `eval-rules-deny-forbids-other-opt-in-used`.

### §selection

File `eval-selection.json`, one matching and one non-matching case per condition.

- Empty selection: `eval-selection-empty-selects-all`.
- `Operations`: `eval-selection-operations-match`, `eval-selection-operations-no-match`, `eval-selection-operations-uninstall`.
- `Sources`: `eval-selection-sources-match`, `eval-selection-sources-no-match`, `eval-selection-sources-any-source`, `eval-selection-sources-other-manager`, `eval-selection-sources-spans-managers`.
- `Packages`: `eval-selection-packages-match`, `eval-selection-packages-no-match`.
- `Version`: `eval-selection-version-match`, `eval-selection-version-no-match`.
- `Scopes`: `eval-selection-scopes-match`, `eval-selection-scopes-no-match`, `eval-selection-scopes-any-entry`.
- `Architectures`: `eval-selection-architectures-match`, `eval-selection-architectures-no-match`.
- `Elevation`: `eval-selection-elevation-requested`, `eval-selection-elevation-machine-scope`, `eval-selection-elevation-not-elevated`, `eval-selection-non-elevated-match`, `eval-selection-non-elevated-no-scope`, `eval-selection-non-elevated-machine-scope`.
- All conditions together: `eval-selection-all-conditions-hold`, `eval-selection-one-condition-fails`.

### §version-condition

File `eval-version-condition.json`.

- Bounds, inclusive: `eval-version-minimum-inclusive`, `eval-version-below-minimum`, `eval-version-maximum-inclusive`, `eval-version-above-maximum`, `eval-version-between-bounds`.
- Comparison as §version-scheme fixes it: `eval-version-components-numeric-below`, `eval-version-components-numeric-above`, `eval-version-trailing-zero-equal`, `eval-version-extra-component-above`, `eval-version-build-metadata-ignored`, `eval-version-prerelease-below-release`.
- A `Winget` pre-release compares below its release although it is not a pre-release: `eval-version-winget-prerelease-below-minimum`, control `eval-version-minimum-inclusive`.
- `Prerelease: Exclude`: `eval-version-prerelease-exclude-prerelease`, `eval-version-prerelease-exclude-release`, `eval-version-prerelease-without-exclude`, `eval-version-prerelease-exclude-winget`.
- Unknown version and a value that is not a version: `eval-version-unknown-allow-not-applicable`, `eval-version-unknown-allow-without-version`, `eval-version-unknown-deny-applicable`, `eval-version-not-a-version-allow`, `eval-version-not-a-version-deny`.
- Deny rules and `Except`: `eval-version-deny-condition-holds`, `eval-version-deny-condition-fails`, `eval-version-except-excludes-old-install`, `eval-version-except-leaves-uninstall`.

### §grants

File `eval-grants.json`.

- Covering and a grant's own `Except`: `eval-grants-pattern-covers`, `eval-grants-except-uncovers`, `eval-grants-except-not-other-rule`, `eval-grants-except-not-other-grant`.
- `CustomParameters` per element, option values included: `eval-grants-each-element-needs-cover`, `eval-grants-each-element-covered`, `eval-grants-pattern-not-across-elements`, `eval-grants-option-value-own-element`, `eval-grants-option-value-covered`.
- `CustomInstallLocation` patterns and values normalized: `eval-grants-location-pattern-normalized`, `eval-grants-location-value-normalized`, `eval-grants-location-star-crosses-separators`, `eval-grants-location-not-covered`, `eval-grants-location-except`, `eval-grants-location-except-control`.
- Location patterns without a drive, normalized all the same: `eval-grants-location-pattern-without-drive`, `eval-grants-location-pattern-without-drive-not-covered`, `eval-grants-location-pattern-repeated-separators`, `eval-grants-location-pattern-trailing-separator`, each with its `-not-covered` control that differs only in the location.
- A forbid's own `Except`: `eval-forbid-except-uncovers`, `eval-forbid-except-other-covered`.

### §opt-ins

File `eval-opt-ins.json`.

- Each `OptIns` key makes the request use its opt-in, ungranted and granted: `eval-opt-in-{interactive,skip-hash-check,custom-install-location,custom-parameters,installer-arguments,pre-operation-command,post-operation-command,kill-before-operation,uninstall-previous,no-upgrade}-{ungranted,granted}`, where `pre-operation-command` and `post-operation-command` set one member of `PrePostCommands` each.
- `OptIns` `{}` uses nothing: `read-request-empty-opt-ins` in `reading-request.json`.
- A custom parameter that would ask for another opt-in makes the request unreadable even when the policy grants both: `eval-opt-in-{interactive,skip-hash-check,location,installer-arguments,uninstall-previous,no-upgrade}-parameter-both-granted`, control `eval-opt-in-custom-parameters-granted`.

### §applicability

File `eval-applicability.json`.

- Allow rule, `Select` unknown and known: `eval-applicability-allow-select-unknown`, `eval-applicability-allow-select-known`.
- Deny rule, `Select` unknown and not matching: `eval-applicability-deny-select-unknown`, `eval-applicability-deny-select-no-match`.
- Allow rule `Except` matching, not matching, unknown: `eval-applicability-allow-except-match`, `eval-applicability-allow-except-no-match`, `eval-applicability-allow-except-unknown`.
- Deny rule `Except` matching, not matching, unknown: `eval-applicability-deny-except-match`, `eval-applicability-deny-except-no-match`, `eval-applicability-deny-except-unknown`.
- Selection logic: `eval-applicability-selection-no-match-over-unknown`, `eval-applicability-selection-unknown-over-match`, `eval-applicability-selection-all-match`.
- `Except` list logic: `eval-applicability-except-match-over-unknown`, `eval-applicability-except-unknown-over-no-match`, `eval-applicability-except-none-match`.
- List condition logic: `eval-applicability-list-unknown-entry`, `eval-applicability-list-match-over-unknown`, `eval-applicability-deny-list-unknown`, through the unknown source of a PowerShell Update.
- An `Except` entry naming a source lists its operations, so it excepts the Install it names and not an Uninstall: `eval-applicability-except-named-source-install`, `eval-applicability-except-named-source-uninstall`.
- Unknown version under a Deny rule: `eval-version-unknown-deny-applicable`.
- Opt-ins do not affect applicability, and an exception narrows only its rule: `eval-applicability-grants-not-required`, `eval-applicability-except-narrows-own-rule`.

### §opt-in-evaluation

File `eval-opt-in-evaluation.json`, plus grants adding up in `eval-grants.json`.

- Grants add up across applicable Allow rules only: `eval-grants-add-up-across-rules`, `eval-grants-one-rule-short`, `eval-grants-custom-parameters-add-up`, `eval-grants-not-applicable-rule-grants-nothing`, `eval-grants-location-add-up`.
- Forbids: `eval-forbid-parameters-not-covered`, `eval-forbid-parameters-one-covered`, `eval-forbid-location-covered`, `eval-forbid-location-not-covered`, `eval-forbid-not-applicable-rule`, `eval-forbid-several-opt-ins`.

### §precedence

File `eval-precedence.json`.
Each check is switched on by one change to a base request that `AllowRules` allows (`eval-precedence-allow-rules-holds`), listed in each case's `note`.

- Every check alone: `eval-precedence-<check>-holds` for all nine checks.
- Every pair of checks that can hold together, adjacent pairs included: `eval-precedence-<higher>-over-<lower>`, 27 cases.
- All checks but `DefaultDeny` together: `eval-precedence-all-checks-hold`.

### §audit-mode

File `eval-audit-mode.json`, with `AuditMode: true` unless a case says otherwise.

- Every check: `eval-audit-<check>` for all nine checks.
- A higher check that audit mode does not let through over a lower one that it does: `eval-audit-{unreadable-request,validity-window,install-location,missing-version}-over-untrusted-source`, `eval-audit-missing-version-over-deny-rule`.
- Two checks audit mode lets through, the higher one named: `eval-audit-untrusted-source-over-deny-rule`, `eval-audit-deny-rule-over-ungranted-opt-in`, `eval-audit-deny-rule-over-default-deny`.
- `AuditMode` false or absent: `eval-audit-mode-false`, `eval-audit-mode-absent`.

### §decision

File `eval-decision.json`, plus the outcome, check and `audited` value of every case in the suite.

- `Reason`: `eval-decision-allow-rule-reason`, `eval-decision-deny-rule-reason`, `eval-decision-empty-reason`, `eval-decision-ungranted-rule-reason`; most other cases name rules without one.
- Rule order follows `Rules`: `eval-decision-allow-rules-in-rules-order`, `eval-decision-deny-rules-in-rules-order`, `eval-decision-ungranted-names-every-applicable-allow`, `eval-decision-deny-opt-ins-in-table-order`.
- Opt-in order follows §opt-ins: `eval-decision-ungranted-opt-in-order-install`, `eval-decision-ungranted-opt-in-order-update`, `eval-decision-default-deny-names-opt-ins`, `eval-decision-deny-opt-ins-in-table-order`.
- Rules and opt-ins per check: `eval-decision-deny-opt-in-named-once`, `eval-decision-deny-names-only-denying-rules`, `eval-decision-deny-with-and-without-forbids`, `eval-decision-allow-names-no-deny-rule`, `eval-decision-default-deny-disabled-rule`.

## Verified by review only

- §precedence: the table fixes which outcome wins, not the order in which an implementation runs its checks; no decision can show the order.
- §evaluation: a decision depends only on the policy, the request and the evaluation time; the case format gives the runner nothing else to pass.
- §text-comparison: implementations use the Unicode 16.0.0 table rather than their platform's casing; the cases sample the code points where common platform functions differ from it.
- §audit-mode: the rationale sentences (trying out a new list or rule set) state no checkable rule beyond the per-check column.
- §evaluation: the broker "accepts every valid document, refuses every invalid document whose `PolicyFormatVersion` is `"2.0.0"` and reports its errors, and runs no request while it holds no accepted policy", and "An allowed request whose command the broker cannot build runs nothing".
  These bind the broker, which the cases do not run.

## Checked by property tests

These properties quantify over every valid policy, request and evaluation time, so each implementation checks them with property tests over generated policies and requests rather than with cases here.

- §monotonicity: adding an Allow rule keeps every `Allow` outcome, and adding a Deny rule keeps every `Deny` outcome.
- §order-independence: reordering any list of a policy changes no decision except the order in which it names rules.

## Met by the suite as a whole

- §agreement: both implementations passing this suite, whose cases fix the full decision and the full error list, is the evidence that they agree.

## Gaps and unchecked boundaries

- §precedence: `UngrantedOptIn` and `DefaultDeny` never hold together, since one needs an applicable Allow rule and the other none, and `AllowRules` holds only when no other check does; those pairs have no case.
- §opt-ins: an `OptIns` key makes the request use its opt-in under every manager and operation; `eval-carriage.json` checks that ([Opt-in carriage](#opt-in-carriage)), and the other files keep those keys to `Winget` requests.
- §text-comparison: the Unicode cases sample code points that separate the simple uppercase mapping from lowercasing, case folding, full mappings, normalization and UTF-16 code-unit casing; other code points are not enumerated.
- §patterns: matching cost for patterns with many `*` is not specified and not checked.
- §selection `Packages` and §package-identity canonical forms, §reading, §managers, §install-location and §missing-version are other parts of the suite; cases here use them only to reach an evaluation clause, and say which reading they rely on in `note`.

## Opt-in carriage

File `eval-carriage.json`, which checks that an `OptIns` key makes the request use its opt-in whatever the manager and operation.
In the names below, `<manager>` is each manager the broker builds a command for other than `Winget`: `powershell`, `powershell7`, `bun`, `cargo`, `chocolatey`, `dotnet`, `npm`, `pip`, `scoop` and `vcpkg`.
`<operation>` is `install`, `update` and `uninstall`, and `<opt-in>` is `interactive`, `skip-hash-check`, `custom-install-location`, `uninstall-previous` and `no-upgrade`.
Each readable request passes every check above `DenyRule`; its `note` says why the version is not missing.

### §reading and the manager readings

- Where the manager's reading refuses the opt-in, the request is unreadable though the policy grants it: `eval-carriage-<manager>-<operation>-<opt-in>-granted` with decision `UnreadableRequest`, 107 cases, each citing its §manager section and against the plain control `eval-carriage-<manager>-<operation>-plain`.
  The readings accept `Interactive` under Chocolatey, PowerShell and PowerShell7; `SkipHashCheck` under those three and Scoop, but not on a Chocolatey or Scoop Uninstall; `CustomInstallLocation` under Cargo, Dotnet, PowerShell and PowerShell7; `UninstallPrevious` and `NoUpgrade` under PowerShell and PowerShell7.

### §opt-ins

- Where the reading accepts the opt-in, the key makes the request use it, not granted: `eval-carriage-<manager>-<operation>-<opt-in>-ungranted`, 43 cases, each against the plain control `eval-carriage-<manager>-<operation>-plain`.
- The same, forbidden by a Deny rule: `eval-carriage-<manager>-<operation>-<opt-in>-forbidden`, 43 cases, each against its `-granted` neighbour.
- Under `Winget`, the pairs the other cases leave out: `eval-carriage-winget-{install,uninstall}-uninstall-previous-{ungranted,granted,forbidden}`, `eval-carriage-winget-{update,uninstall}-no-upgrade-{ungranted,granted,forbidden}`, with the controls `eval-carriage-winget-{install,update,uninstall}-plain`.

### §opt-in-evaluation

- The granted neighbours: `eval-carriage-<manager>-<operation>-<opt-in>-granted` with decision `AllowRules`, 43 cases, and the 4 `Winget` `-granted` cases above.
- Several opt-ins all granted: `eval-carriage-combined-all-granted`.

### §rules

- Plain controls: `eval-carriage-<manager>-<operation>-plain`, 30 cases, and `eval-carriage-winget-{install,update,uninstall}-plain`.

### §install-location

- `OptIns.CustomInstallLocation.Path` with a `..` segment: `eval-carriage-<manager>-<operation>-location-not-plain`, 30 cases.
  Under Cargo, Dotnet, PowerShell and PowerShell7 a grant pattern covers the text, so only the install location check denies it, and each case's control is `eval-carriage-<manager>-<operation>-custom-install-location-granted`; under the other managers the reading refuses the key, so the request is unreadable.

### §decision

- Opt-ins named in the order of §opt-ins, not in the order the request writes them, for each check that names opt-ins: `eval-carriage-combined-ungranted-order`, `eval-carriage-combined-partly-granted-order`, `eval-carriage-combined-forbidden-order`, `eval-carriage-combined-default-deny-order`, all through PowerShell, whose reading accepts every opt-in they use.

### Gaps

- Every `PowerShell7` request here has a version that §missing-version makes concrete; how `PowerShell7` reads a range or no version is checked by the `read-missing-version-powershell7-*` cases of the request reading part.
- The install location check is exercised with one form of non-plain path, a `..` segment, under each manager; the other forms are left to the install location cases of the request reading part.
- `InstallerArguments` is accepted only by `Winget`; the other managers refuse it in `read-<manager>-installer-arguments-unreadable` of the request reading part, so it has no carriage cases here.
