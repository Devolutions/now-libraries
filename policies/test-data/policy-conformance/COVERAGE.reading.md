# Coverage: request reading

Cases in `evaluation/reading-*.json` for how a request is read: its input bounds, when it is unreadable, its properties, package identity, versions, each manager's reading, the install location check and the missing version check.
Every section cited below is in `policies/CONTRACT.policy.md`.

## How a property is observed

A property shows only through a decision, so most cases use an observer policy.

- Scope, architecture and elevation: one Allow rule per value plus a catch-all `allow-any`.
  The decision's rule list names the value, and only `allow-any` applies when the property is unknown.
- Package names: the same request against an Allow-only policy and a Deny-plus-catch-all policy, as the `-allow` and `-deny` pair of each `read-identity-*` case.
  Match gives (Allow, DenyRule), no match gives (DefaultDeny, Allow), and unknown gives (DefaultDeny, DenyRule).
- Version comparisons: an Allow rule with `Minimum` and one with `Maximum`, both set to the same bound.
  Equal satisfies both, higher only `Minimum`, lower only `Maximum`, and a value that is not a version neither.
- Readability: a catch-all Allow rule granting what the request uses, so an unreadable request is the only way to a Deny.

Every request is well-formed under `policies/CONTRACT.wire.md` §package-request, and is otherwise readable, in the validity window, passes the install location check, has a concrete version where its manager can pin one, and comes from a trusted source (`AnySource` for every manager unless the case tests trust).

## Coverage by section

### §request-input

`reading-input.json`: the largest and smallest well-formed values of each member, which stay readable: `read-input-source-name-*`, `read-input-package-id-*`, `read-input-package-version-*`, `read-input-install-location-2048-bytes`, `read-input-custom-parameter*`, `read-input-pre-command-*`, `read-input-post-command-*`, `read-input-kill-*`, and one value of each enumeration.
Requests that are not well-formed are not cases, since a runner rejects them (README, Request).

### §reading

- The broker builds no command for the manager: `read-request-no-command-*`, control `read-request-built-manager`.
- Custom parameters through a manager whose reading accepts none: `read-<manager>-custom-parameters-unreadable` in `reading-managers.json`, controls `read-<manager>-plain`.
- Installer arguments through every manager but WinGet: `read-<manager>-installer-arguments-unreadable`, though the policy grants them, controls `read-<manager>-plain` and `read-winget-installer-arguments-*-granted`.
- A custom parameter that would ask for another opt-in or change the source: the `read-winget-*` and `read-scoop-*` cases with a WinGet table row or the Scoop `s` row, decision `UnreadableRequest`, and `read-winget-source-*`, `read-winget-manifest-*`, controls `read-winget-*-wrong-case`, `read-winget-source-after-terminator`, `read-winget-source-as-option-value`.
- Opt-in use is key presence: `read-winget-interactive-opt-in` uses `Interactive`, and `read-request-empty-opt-ins` with `OptIns` `{}` uses none.
- A `Machine` scope with `Scope` absent or `User` and `NonElevated`, on every operation: `read-winget-scope-machine-nonelevated-{install,update,uninstall}`, `read-winget-scope-user-field-machine-parameter-nonelevated` and Chocolatey's default `read-scope-machine-default-nonelevated-{install,update,uninstall}`, each against an `Elevated` control.
- A scope a custom parameter makes unknown with `NonElevated`: `read-winget-scope-unknown-nonelevated-{install,update,uninstall}`, `read-winget-scope-missing-value-nonelevated`, `read-winget-scope-twice-nonelevated`, against `Elevated` controls; a scope unknown without a custom parameter stays readable: `read-winget-scope-not-set`, `read-winget-no-effect-parameter`, `read-scope-unknown-{pip,powershell}-uninstall-nonelevated`.
- A version through a manager that cannot pin one: `read-scoop-version-unreadable*`, `read-vcpkg-version-unreadable`.
- A WinGet or Dotnet `Package.Id` that starts with `-`: `read-request-id-leading-hyphen-{winget,dotnet,dotnet-uninstall}`, controls `read-request-id-inner-hyphen-*`; another manager is not bound by the rule: `read-request-id-leading-hyphen-powershell`.
- U+0000, U+000A, U+000D in a custom parameter, installer arguments or `Source.Name`: `read-request-custom-parameter-{nul,line-feed,carriage-return}`, `read-winget-installer-arguments-{nul,line-feed,carriage-return}`, `read-request-source-name-{nul,line-feed,carriage-return}`, controls with U+0009.
- `Source.Name` with leading or trailing `White_Space`, or any `Default_Ignorable_Code_Point`: `read-request-source-name-leading-*`, `-trailing-*`, `-ignorable-*`, controls `read-request-source-name-inner-*`.
  The sampled code points include U+180E, U+200B and U+FEFF, which are ignorable but not `White_Space` in Unicode 16.0.0.
- Elements are neither split nor joined: `read-request-custom-parameter-not-split`, `read-request-custom-parameter-not-joined`.
- A pattern matches one element, and an option's value is a custom parameter: `read-request-pattern-*`, `read-request-option-value-*`.
- A property set by a field and a custom parameter, or by two custom parameters, is unknown: `read-request-scope-*`, `read-request-architecture-*`.

### §request-properties

- Elevation, read from `Elevation` and never unknown: `read-elevation-*`.
- Operation: `read-operation-*`.
- Source: `read-scoop-*` trust cases and `read-identity-npm-*-source-*`.
- Scope and architecture: see §managers.

### §package-identity

- Default reading, `name@version` split at the last `@` that is not first, canonical lowercase, pattern canonicalization: `read-identity-default-*`.
- A match only when case is ignored is unknown, including a pattern with U+017F that matches only under simple uppercase: `read-identity-default-unicode-case-only-*`, `read-identity-npm-case-only-mismatch-*`.
- Every name versus some names (`Packages` matches, is unknown, or does not match): `read-identity-npm-alias-*`.

### §version-scheme

`reading-version.json`.

- Numeric components, leading zeros, missing trailing zeros, `v` and `V`, build metadata, components beyond 64 bits: `read-version-*` with Winget requests.
- Strings that are not versions give an unknown condition: `read-version-not-a-version-*`.
- Pre-release precedence per Semantic Versioning 2.0.0, including ASCII order and numeric identifiers beyond 64 bits: `read-version-pre-release-*` with Npm requests.
- Pre-release status, and none under Winget: `read-version-pre-release-excluded`, `read-version-release-not-excluded`, `read-version-build-metadata-not-pre-release`, `read-version-winget-never-pre-release`, `read-version-pre-release-satisfies-without-exclude`.
- Comparisons use the pre-release under Winget too: `read-version-winget-pre-release-below-release`, `read-version-winget-release-above-pre-release`, `read-version-winget-pre-release-order`, control `read-version-winget-pre-release-equal`.

### §managers

- Unbuilt managers: `read-request-no-command-*`.
- Defaults: no custom parameters (`read-<manager>-custom-parameters-unreadable`), no installer arguments (`read-<manager>-installer-arguments-unreadable`), scope from `Scope` and `User` when absent (`read-scope-default-*`), architecture from `Package.Architecture` and unknown when absent (`read-architecture-default-*`, `read-architecture-unknown-matches-deny`).
- Default version characters on Install and Update, not Uninstall: `read-version-characters-{leading-hyphen,slash,update,uninstall,allowed}`; characters a reading adds, each against a reading that does not add it: `read-version-characters-{tilde-npm,tilde-winget,equals-cargo,equals-npm,range-dotnet,range-powershell7,range-powershell}`.
- The only source name compared ignoring ASCII case, and not under full Unicode case: `read-{npm,bun,cargo,pip,dotnet}-source-*`, including `read-cargo-source-long-s` and `read-pip-source-dotless-i`.
- Plain source names: `read-winget-source-name-*` and `read-powershell*-source-*`; `read-winget-source-name-not-plain-uninstall` and `read-chocolatey-uninstall-source-not-plain` show the rule on an Uninstall.
- Batch characters, each of the eight: `read-winget-custom-parameter-batch-*`, `read-winget-location-batch-*`, `read-winget-installer-arguments-batch-*` (and `-batch-uninstall`), `read-winget-id-batch-percent`, `read-cargo-location-batch-percent`, controls `read-winget-location-plain-path` and `read-cargo-location-plain`.

### §manager-winget

`reading-winget.json`.

- Each table row: `read-winget-location*`, `read-winget-scope-*`, `read-winget-architecture-*`, `read-winget-interactive-*`, `read-winget-override*`, `read-winget-custom`, `read-winget-ignore-security-hash`, `read-winget-force*`, `read-winget-no-upgrade`, `read-winget-uninstall-previous`, `read-winget-version-*`, `read-winget-source-*`, `read-winget-manifest-*`.
- Long options ignore ASCII case and short options do not: the `-any-case` and `-wrong-case` cases.
- Option names: `read-winget-long-option-*`, `read-winget-triple-hyphen`, `read-winget-single-hyphen`, `read-winget-short-o-not-listed`.
- The `--` terminator: `read-winget-terminator-*`, `read-winget-location-after-terminator`, `read-winget-source-after-terminator`, `read-winget-scope-terminator-stops-second`.
- `--` taken as an option's value, which then ends nothing: `read-winget-override-value-terminator`.
- Short-option clusters: `read-winget-cluster-*`, `read-winget-location-in-cluster`, `read-winget-source-in-cluster`, `read-winget-architecture-in-cluster*`.
- A value taken from the next element, which is then not an option: `read-winget-override-value-not-option`, `read-winget-attached-value-frees-next`, `read-winget-source-as-option-value`.
- A missing value at the end: the `-missing-value` cases.
- Installer arguments: `Append` and `Replace`, granted and not, on Install and Uninstall: `read-winget-installer-arguments-{append,replace,uninstall}-{granted,ungranted}`; they are not custom parameters: `read-winget-installer-arguments-not-custom-parameter`.
- Scope unknown when nothing sets it: `read-winget-scope-not-set`, `read-scope-unknown-matches-deny`.

### §manager-scoop

`reading-scoop.json`.

- The custom parameter rules: `--skip-hash-check` and the `s` cluster, `--arch` and the `a` cluster, `--global`, `--all`, `g` in a cluster, `--`, an element not starting with `-`, `White_Space`: `read-scoop-skip-hash-check*`, `read-scoop-short-*`, `read-scoop-cluster-*`, `read-scoop-architecture-*`, `read-scoop-parameter-*`, `read-scoop-no-hyphen`, against `read-scoop-parameter-short-q` and the other readable `read-scoop-parameter-*` cases, which include each example the section gives.
- Option names in any ASCII case, a name ending at the first `=`, longer names with the same prefix, a cluster with a later `-`: `read-scoop-*-upper*`, `-attached`, `-longer-name`, `read-scoop-cluster-hyphen-inside`.
- The broker builds no command: `read-scoop-elevated-unreadable`, `read-scoop-{update,uninstall}-architecture-unreadable`, `read-scoop-install-architecture-neutral-unreadable`, `read-scoop-id-brackets-unreadable`, `read-scoop-id-leading-hyphen-unreadable`, and the refused opt-ins in `eval-carriage.json`.
- Bucket sources: `read-scoop-bucket-app` against an id ending with `.json` in either case, with `/`, with `@`, a bucket name with `:`, and a trailing `.` on the bucket or the id, including `..`: `read-scoop-bucket-*`, `read-scoop-id-*`, `read-scoop-source-trailing-dot`.
- Manifest sources: readable only as an `http://` or `https://` URL equal to `Package.Id` with exact characters: `read-scoop-manifest-{http-equal,scheme-upper-equal}`, `read-scoop-manifest-url-any-source`, against `read-scoop-manifest-url-id-{differs,only,differs-in-case}`, `read-scoop-manifest-{ftp-equal,ftp-other-id,leading-slash-equal,unc-source}`, `read-scoop-local-path-named`.
- A `Source` selector compares a manifest source exactly, and is unknown when only case differs, in a rule and in `TrustedSources`: `read-scoop-manifest-selector-*`, `read-scoop-manifest-trusted-*`, `read-scoop-manifest-url-named*`.
- A manifest id names itself, keeps its case and selects no version, even with `@`: `read-scoop-manifest-packages-*`; a bucket app name is lowercase: `read-scoop-bucket-app-case-folded`.

### §manager-vcpkg, §manager-npm, §manager-bun, §manager-cargo, §manager-pip, §manager-dotnet, §manager-chocolatey, §manager-powershell

`reading-managers.json` and the manager cases of `reading-identity.json` and `reading-missing-version.json`.
Each bullet the section lists under "builds no command" has a case and a readable control in `reading-managers.json`; the refused opt-ins are in `eval-carriage.json`.

- Vcpkg: triplet sources, port specs with features and a triplet equal to `Source.Name` with exact characters, port and feature characters: `read-vcpkg-*`; triplet architecture by prefix, `Package.Architecture` first; version unreadable; the name is the port: `read-identity-vcpkg-*`.
- Npm: registry names, aliases with and without version, other identifiers unreadable, case kept, `Neutral` architecture, version ranges, `%` in an id, U+0000 and U+000A in an Uninstall version: `read-npm-*`, `read-identity-npm-*`.
  Identifiers that select the version on Install and Update are unreadable or concrete as `reading-missing-version.json` shows: `read-missing-version-npm-*`.
- Bun: the Npm reading, source `Bun`, a version that is a Semantic Versioning 2.0.0 version on every operation: `read-bun-*`, `read-identity-bun-*`, `read-missing-version-bun-*`.
- Cargo: `_` to `-`, source, `Neutral` architecture, id length 64 and 65 bytes, `+` and a leading `-`, refused opt-ins, an Uninstall version: `read-cargo-*`, `read-identity-cargo-*`.
- Pip: PEP 503 names and the Pip id and version rules, source, `Neutral` architecture, an Uninstall version, unknown uninstall scope: `read-pip-*`, `read-identity-pip-*`.
- Dotnet: `Machine` scope, an Uninstall architecture, the only Install source `nuget.org`, an Uninstall source that is unknown: `read-dotnet-*`.
- Chocolatey: `Machine` scope by default, architecture, id length and characters, `.config` and `.nupkg` suffixes, `all`, a source that is not plain on Install only: `read-chocolatey-*`.
- Unknown Uninstall sources under Chocolatey and Dotnet: a `Source` selector is unknown, so an Allow rule fails closed and a Deny rule applies, while `AnySource` matches: `read-{chocolatey,dotnet}-uninstall-source-selector-{allow,deny}`, `read-{chocolatey,dotnet}-uninstall-any-source-allow`, controls `read-{chocolatey,dotnet}-install-source-selector-deny`.
- PowerShell and PowerShell7: source name characters, `[` and `]` in an id, `Neutral` architecture always, unknown uninstall scope.

### §install-location

`reading-location.json`: the grammar (`read-location-plain-*`, `read-location-not-plain-*`), normalization and pattern normalization (`read-location-normalized-*`, `read-location-pattern-*`, `read-location-drive-root-*`), Unicode simple case (`read-location-unicode-*`), and the check applying whatever the rules say (`read-location-check-before-rules`).
The check reads only `OptIns.CustomInstallLocation.Path`: a Scoop custom parameter holding a relative path is not a location, `read-location-parameter-not-read` against `read-location-not-plain-relative-powershell`; a WinGet location option is unreadable, `read-location-one-from-parameter`.
Winget's own location cases are in `reading-winget.json`.

- A space is only U+0020, so other white space starts or ends a segment freely: `read-location-plain-segment-leading-no-break-space`, `read-location-plain-segment-trailing-no-break-space`, `read-location-plain-segment-trailing-ideographic-space`, against `read-location-not-plain-segment-leading-space` and `read-location-not-plain-segment-trailing-space`.
- Repeated separators right after the drive: `read-location-plain-repeated-after-drive`, `read-location-normalized-repeated-after-drive`, `read-location-drive-root-repeated-separators`.
- Characters a batch file cannot pass go through PowerShell, whose reading has no batch rule: `read-location-not-plain-{quote,less-than,greater-than,pipe}`, control `read-location-plain-powershell`.

### §missing-version

`reading-missing-version.json`: concrete and missing versions per manager, ranges under Npm, Cargo and Dotnet, versions selected by the identifier, uninstalls never missing, Scoop and Vcpkg never missing with an unknown version, and the check applying whatever the rules say.

- An Npm Update with a range and with three components: `read-missing-version-npm-update-range`, `read-missing-version-npm-update-three-components`.
- PowerShell7 resolves anything but a version as a range: versions `read-missing-version-powershell7-{two-components,three-components,one-component,pre-release,update-concrete}`; a range, a wildcard or no version on Install and Update `read-missing-version-powershell7-{range,wildcard,absent,update-range,update-absent}`; an Uninstall never missing `read-missing-version-powershell7-uninstall-{absent,range}`.

## Verified by review only

- §reading: the broker runs the command whose reading it evaluated, and a change to how it builds commands changes the contract.
  These bind the broker, not the evaluator's decision.
- §manager-winget: the order in which the broker passes installer arguments before the custom parameters.
  It binds the command line, not the decision.
- §install-location: the check never asks the operating system about a drive.
  `read-location-plain-other-drive` uses `Z:`, but a runner cannot show that no lookup happened.

## Gaps

None: the contract decides every clause of this part that a case could reach.

## Deliberately unchecked

- That an `OptIns` key makes the request use its opt-in whatever the manager and operation is checked by `evaluation/eval-carriage.json` (see `COVERAGE.evaluation.md`), so the cases here do not repeat it.
- Requests that are not well-formed: a runner rejects them, so the reading never sees them.
- U+0000, U+000A and U+000D in `Package.Id`, and the batch characters other than `%` in it: the wire character set of `Package.Id` excludes them, so no well-formed request has them.
- A Scoop local path as `Package.Id`: the wire character set of `Package.Id` excludes `\`, so only the source can be one.
- Bun's range reading under §missing-version: a Bun version is always a Semantic Versioning 2.0.0 version with three release components, so a present Bun version is never a range.
- The full `White_Space` and `Default_Ignorable_Code_Point` sets: the cases sample code points that tell the Unicode 16.0.0 properties from common platform whitespace tests.
- Each `Package.Id` character on its own: one case uses every allowed character, and several cases one character outside a reading's set.
