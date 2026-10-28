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
- A custom parameter that starts with `-` and whose option name has a code point above U+007F: `read-winget-non-ascii-option-name`, `read-scoop-parameter-non-ascii-option-name`, `read-scoop-parameter-non-ascii-short-option`, controls `read-winget-ascii-unlisted-option`, `read-scoop-parameter-ascii-option-name`; such a code point after `=` or in an element that is not an option leaves the request readable: `read-winget-non-ascii-after-equals`, `read-scoop-parameter-non-ascii-after-equals`, `read-winget-non-ascii-not-option`.
- A pattern matches one element, and an option's value is a custom parameter: `read-request-pattern-*`, `read-request-option-value-*`.
- A property set by a field and a custom parameter, or by two custom parameters, is unknown: `read-request-scope-*`, `read-request-architecture-*`.

### §request-properties

- Elevation, read from `Elevation` and never unknown: `read-elevation-*`.
- Operation: `read-operation-*`.
- Source: `read-scoop-*` trust cases and `read-identity-npm-*-source-*`; an unknown source on an Update: `read-{scoop,powershell,powershell7,vcpkg}-update-*`; an Uninstall, which `AnySource` selects under every manager: `read-{winget,pip,npm,bun,cargo,vcpkg,chocolatey,dotnet,scoop,powershell,powershell7}-uninstall-any-source-allow`.
- Scope and architecture: see §managers.

### §package-identity

- Default reading, `name@version` split at the last `@` that is not first, canonical lowercase, pattern canonicalization: `read-identity-default-*`, under Dotnet.
- An identifier that names itself is not split at `@`: `read-identity-{winget,powershell,powershell7}-at-whole-*`, controls `read-identity-{winget,powershell,powershell7}-at-not-split-*`.
- A package identity a reading makes unknown: `read-vcpkg-{update,uninstall}-packages-*`, controls `read-vcpkg-install-packages-*` and `read-vcpkg-{update,uninstall}-no-packages-allow`.
- A match only when case is ignored is unknown, including a pattern with U+017F that matches only under simple uppercase: `read-identity-default-unicode-case-only-*`, `read-identity-npm-case-only-mismatch-*`.
- Every name versus some names (`Packages` matches, is unknown, or does not match): `read-identity-npm-alias-*`.

### §version-scheme

`reading-version.json`.

- Numeric components, leading zeros, missing trailing zeros, `v` and `V`, build metadata, components beyond 64 bits: `read-version-*` with Winget requests, and Dotnet requests where the case needs a version Npm would not read as concrete.
- Strings that are not versions give an unknown condition: `read-version-not-a-version-*`.
- Pre-release precedence per Semantic Versioning 2.0.0, including ASCII order and numeric identifiers beyond 64 bits: `read-version-pre-release-*` with Npm requests, and Dotnet requests for `v` and a fourth component: `read-version-pre-release-equal-{v-prefix,fourth-zero}`.
- Pre-release status, and none under Winget: `read-version-pre-release-excluded`, `read-version-release-not-excluded`, `read-version-build-metadata-not-pre-release`, `read-version-winget-never-pre-release`, `read-version-pre-release-satisfies-without-exclude`.
- Comparisons use the pre-release under Winget too: `read-version-winget-pre-release-below-release`, `read-version-winget-release-above-pre-release`, `read-version-winget-pre-release-order`, control `read-version-winget-pre-release-equal`.

### §managers

- Unbuilt managers: `read-request-no-command-*`.
- Defaults: no custom parameters (`read-<manager>-custom-parameters-unreadable`), no installer arguments (`read-<manager>-installer-arguments-unreadable`), scope from `Scope` and `User` when absent (`read-scope-default-*`), architecture from `Package.Architecture` and unknown when absent (`read-architecture-default-*`, `read-architecture-unknown-matches-deny`).
- Default version characters on Install and Update, not Uninstall: `read-version-characters-{leading-hyphen,slash,update,uninstall,allowed}`; characters a reading adds, each against a reading that does not add it: `read-version-characters-{tilde-npm,tilde-winget,equals-cargo,equals-npm,range-dotnet,range-powershell7,range-powershell}`.
- The only source name compared ignoring ASCII case, and not under full Unicode case: `read-{npm,bun,cargo,pip,dotnet}-source-*`, including `read-cargo-source-long-s` and `read-pip-source-dotless-i`.
- The source of an Uninstall is unknown under every manager, WinGet included; a valid selection naming a source lists operations without `Uninstall` (§selection), so only `AnySource`, which also meets a known source, can meet an Uninstall, and no case observes the unknown source (see Deliberately unchecked); an `AnySource` Allow rule allows an Uninstall under every manager: `read-{winget,pip,npm,bun,cargo,vcpkg,chocolatey,dotnet,scoop,powershell,powershell7}-uninstall-any-source-allow`; the known Install sources: `read-{winget,pip,npm,bun,cargo,vcpkg,scoop,powershell,powershell7}-install-source-selector-{allow,deny}`, `read-{chocolatey,dotnet}-install-source-selector-deny`.
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
- `--dependency-source` in any case, attached or not, on every operation: `read-winget-dependency-source*`, control `read-winget-dependency-sources-not-listed`.
- `--all`, `--recurse` and `-r` on an Update, also in a cluster and in another case for the long options: `read-winget-{all,recurse,short-r,cluster-r,all-any-case}-update`, controls `read-winget-short-r-wrong-case-update` and the same options on Install and Uninstall, `read-winget-{all,recurse,short-r}-install`, `read-winget-all-uninstall`.
- An identifier names itself, keeps its case and selects no version: `read-identity-winget-at-*`, `read-missing-version-winget-at-id-concrete`; a pattern matching it only ignoring case, with and without `*`, is unknown: `read-identity-winget-case-other-*`, `read-identity-winget-wildcard-other-case-*`, controls `read-identity-winget-case-exact-*`, `read-identity-winget-wildcard-exact-*`.

### §manager-scoop

`reading-scoop.json`.

- The custom parameter rules: `--skip-hash-check` and the `s` cluster, `--arch` and the `a` cluster, `--global`, `--all`, `g` in a cluster, `--`, `--%`, an element not starting with `-`, `White_Space`, a code point above U+007F in a short cluster, after `=` too: `read-scoop-skip-hash-check*`, `read-scoop-short-*`, `read-scoop-cluster-*`, `read-scoop-architecture-*`, `read-scoop-parameter-*`, `read-scoop-no-hyphen`, against `read-scoop-parameter-short-q` and the other readable `read-scoop-parameter-*` cases, which include each example the section gives; `read-scoop-parameter-options-end` against `read-scoop-parameter-options-end-suffixed`, and `read-scoop-parameter-short-non-ascii-after-equals` against `read-scoop-parameter-short-q-value-x`.
- Option names in any ASCII case, a long option's name ending at the first `=`, longer names with the same prefix, a cluster with a later `-`: `read-scoop-*-upper*`, `-attached`, `-longer-name`, `read-scoop-parameter-long-value-s`, `read-scoop-cluster-hyphen-inside`.
- Every character of a short cluster after its `-`, `=` and what follows included, is an option name: `read-scoop-parameter-short-q-value-{s,a,g}`, `read-scoop-cluster-s-after-equals`, `read-scoop-parameter-short-value-contains-g`, control `read-scoop-parameter-short-q-value-x`.
- The broker builds no command: `read-scoop-elevated-unreadable`, `read-scoop-{update,uninstall}-architecture-unreadable`, `read-scoop-install-architecture-neutral-unreadable`, `read-scoop-id-brackets-unreadable`, `read-scoop-id-leading-hyphen-unreadable`, and the refused opt-ins in `eval-carriage.json`.
- Bucket sources: `read-scoop-bucket-app` against an id ending with `.json` in either case, with `/`, with `@`, a bucket name with `:`, and a trailing `.` on the bucket or the id, including `..`: `read-scoop-bucket-*`, `read-scoop-id-*`, `read-scoop-source-trailing-dot`.
- Manifest sources: readable only as an `http://` or `https://` URL equal to `Package.Id` with exact characters: `read-scoop-manifest-{http-equal,scheme-upper-equal}`, `read-scoop-manifest-url-any-source`, against `read-scoop-manifest-url-id-{differs,only,differs-in-case}`, `read-scoop-manifest-{ftp-equal,ftp-other-id,leading-slash-equal,unc-source}`, `read-scoop-local-path-named`.
- A `Source` selector compares a manifest source exactly, and is unknown when only case differs, in a rule and in `TrustedSources`: `read-scoop-manifest-selector-*`, `read-scoop-manifest-trusted-*`, `read-scoop-manifest-url-named*`.
- A manifest id names itself, keeps its case and selects no version: `read-scoop-manifest-packages-*`; a bucket app name is lowercase: `read-scoop-bucket-app-case-folded`.
- A manifest id containing `@` is unreadable, after a path or in the userinfo: `read-scoop-manifest-id-at-version`, `read-scoop-manifest-id-at-userinfo`, control `read-scoop-manifest-id-without-at`.
- An Update or Uninstall whose source or id is a manifest source is unreadable: `read-scoop-manifest-{update,uninstall}-unreadable`, `read-scoop-manifest-id-only-{update,uninstall}-unreadable`.
- The source of an Update is unknown, so a `Source` selector fails an Allow rule and applies a Deny rule, while `AnySource` matches: `read-scoop-update-source-selector-{allow,deny}`, `read-scoop-update-any-source-allow`, `read-scoop-update-trusted-named-source`, controls `read-scoop-install-*`.
- A custom parameter that is exactly `-`: `read-scoop-parameter-single-hyphen`, control `read-scoop-parameter-short-q`.

### §manager-vcpkg, §manager-npm, §manager-bun, §manager-cargo, §manager-pip, §manager-dotnet, §manager-chocolatey, §manager-powershell

`reading-managers.json` and the manager cases of `reading-identity.json` and `reading-missing-version.json`.
Each bullet the section lists under "builds no command" has a case and a readable control in `reading-managers.json`; the refused opt-ins are in `eval-carriage.json`.

- Vcpkg: triplet sources, port specs with features and a triplet equal to `Source.Name` with exact characters, port and feature characters: `read-vcpkg-*`; an Install's architecture by triplet prefix, `Package.Architecture` first; version unreadable; the name is the port: `read-identity-vcpkg-*`; the package identity of an Update or Uninstall is unknown: `read-vcpkg-{install,update,uninstall}-packages-*`, `read-vcpkg-{update,uninstall}-no-packages-allow`; the architecture of an Update or Uninstall is unknown: `read-vcpkg-{update,uninstall}-architectures-{allow,deny}`, controls `read-vcpkg-install-architectures-{allow,deny}`; the source of an Update is unknown: `read-vcpkg-update-source-selector-{allow,deny}`, `read-vcpkg-update-any-source-allow`, `read-vcpkg-update-trusted-named-source`, controls `read-vcpkg-install-source-selector-{allow,deny}`, `read-vcpkg-install-trusted-named-source`.
- Npm: registry names, aliases with and without version, other identifiers unreadable, case kept, `Neutral` architecture, version ranges, `%` in an id, U+0000 and U+000A in an Uninstall version: `read-npm-*`, `read-identity-npm-*`.
  Identifiers that select the version on Install and Update are unreadable or concrete as `reading-missing-version.json` shows: `read-missing-version-npm-*`.
- Bun: the Npm reading, source `Bun`, a version that is a Semantic Versioning 2.0.0 version on every operation: `read-bun-*`, `read-identity-bun-*`, `read-missing-version-bun-*`.
- Cargo: `_` to `-`, source, `Neutral` architecture, id length 64 and 65 bytes, `+` and a leading `-`, refused opt-ins, an Uninstall version: `read-cargo-*`, `read-identity-cargo-*`.
- Pip: PEP 503 names and the Pip id and version rules, source, `Neutral` architecture, an Uninstall version, unknown uninstall scope: `read-pip-*`, `read-identity-pip-*`.
- Dotnet: `Machine` scope, an Uninstall architecture, the only Install source `nuget.org`: `read-dotnet-*`; the default identifier reading: `read-identity-default-*`, `read-missing-version-dotnet-identifier-selects*`.
- Chocolatey: `Machine` scope by default, architecture, id length and characters, `.config` and `.nupkg` suffixes, `all`, a source that is not plain on Install only: `read-chocolatey-*`.
- Chocolatey and Dotnet: an `AnySource` Allow rule allows an Uninstall, `read-{chocolatey,dotnet}-uninstall-any-source-allow`, and a `Source` selector meets the known source of an Install, `read-{chocolatey,dotnet}-install-source-selector-deny`.
- PowerShell and PowerShell7: source name characters, `[` and `]` in an id, `Neutral` architecture always, unknown uninstall scope; an identifier names itself and selects no version: `read-identity-{powershell,powershell7}-at-*`, `read-missing-version-{powershell,powershell7}-at-id-concrete`.
- Unknown Update sources under PowerShell and PowerShell7, and Uninstall sources by §managers: `read-{powershell,powershell7}-update-source-selector-{allow,deny}`, `read-{powershell,powershell7}-{update,uninstall}-any-source-allow`, `read-{powershell,powershell7}-update-trusted-named-source`, controls `read-{powershell,powershell7}-install-*`.

### §install-location

`reading-location.json`: the grammar (`read-location-plain-*`, `read-location-not-plain-*`), normalization and pattern normalization (`read-location-normalized-*`, `read-location-pattern-*`, `read-location-drive-root-*`), Unicode simple case (`read-location-unicode-*`), and the check applying whatever the rules say (`read-location-check-before-rules`).
The check reads only `OptIns.CustomInstallLocation.Path`: a Scoop custom parameter holding a relative path is not a location, `read-location-parameter-not-read` against `read-location-not-plain-relative-powershell`; a WinGet location option is unreadable, `read-location-one-from-parameter`.
Winget's own location cases are in `reading-winget.json`.

- A space is only U+0020, so other white space starts or ends a segment freely: `read-location-plain-segment-leading-no-break-space`, `read-location-plain-segment-trailing-no-break-space`, `read-location-plain-segment-trailing-ideographic-space`, against `read-location-not-plain-segment-leading-space` and `read-location-not-plain-segment-trailing-space`.
- Repeated separators right after the drive: `read-location-plain-repeated-after-drive`, `read-location-normalized-repeated-after-drive`, `read-location-drive-root-repeated-separators`.
- Characters a batch file cannot pass go through PowerShell, whose reading has no batch rule: `read-location-not-plain-{quote,less-than,greater-than,pipe}`, control `read-location-plain-powershell`.

### §missing-version

`reading-missing-version.json`: concrete and missing versions per manager, ranges under Npm, Cargo and Dotnet, versions selected by the identifier, uninstalls never missing, Scoop and Vcpkg never missing with an unknown version, and the check applying whatever the rules say.

- Npm versions are concrete only as Semantic Versioning 2.0.0 versions: `read-missing-version-npm-*`, including `read-missing-version-npm-four-components-not-semver` and `read-missing-version-npm-upper-v-prefix`; Dotnet reads `v1.2.3` and `1.2.3.4` as concrete: `read-missing-version-dotnet-{v-prefix,four-components}`.
- Pip versions are concrete only as ASCII-digit release components: `read-missing-version-pip-three-components` against `read-missing-version-pip-{post-release-hyphen,post-release,release-candidate,epoch,local,update-post-release-hyphen}` and `read-pip-version-{leading-hyphen,epoch}`.

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
- Bun under §missing-version: §manager-bun makes a request whose version is not a Semantic Versioning 2.0.0 version unreadable, so the check never sees a Bun version that is not concrete.
- The full `White_Space` and `Default_Ignorable_Code_Point` sets: the cases sample code points that tell the Unicode 16.0.0 properties from common platform whitespace tests.
- A `Source` selector against the unknown source of an Uninstall: §selection requires a selection naming a source to list operations without `Uninstall`, so no valid policy reaches that combination; `documents/selection.json` checks the rule (`doc-sel-named-source-*`).
- Each `Package.Id` character on its own: one case uses every allowed character, and several cases one character outside a reading's set.
