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
- Opt-ins a custom parameter carries: a catch-all Allow rule granting only `CustomParameters`, so the `UngrantedOptIn` decision names each other opt-in used.

Every request is otherwise readable, in the validity window, passes the install location check, carries a concrete version where its manager can pin one, and comes from a trusted source (`AnySource` for every manager unless the case tests trust).

## Coverage by section

### §request-input

`reading-input.json`, every case.

- Closed sets, a value inside and one outside or with other case: `read-input-operation-*`, `read-input-manager-*`, `read-input-architecture-*`, `read-input-scope-*`, `read-input-requested-elevation-*`.
- Byte bounds at the limit and one past it, including multi-byte values that are short in code points but long in bytes: `read-input-source-name-*`, `read-input-source-url-*`, `read-input-package-version-*`, `read-input-channel-*`, `read-input-install-location-*`, `read-input-custom-parameter-*`, `read-input-pre-command-*`, `read-input-post-command-*`, `read-input-kill-element-*`.
- List lengths 64 and 65: `read-input-custom-parameters-*-elements`, `read-input-kill-list-*-elements`.
- `Package.Id` length and character set: `read-input-package-id-*`.
- Empty strings where the minimum is 1: the `-empty` cases.

### §reading

- The broker builds no command for the manager: `read-request-no-command-*`, control `read-request-built-manager`.
- Custom parameters through a manager whose reading accepts none: `read-<manager>-custom-parameters-unreadable` in `reading-managers.json`, controls `read-<manager>-plain`.
- A `Source.Url` that the manager's reading does not accept: `read-request-source-url-*` and the `read-<manager>-url-*` cases under §managers, §manager-cargo and §manager-dotnet; Bun's URL is under §manager-bun.
- A version through a manager that cannot pin one: `read-scoop-version-unreadable*`, `read-vcpkg-version-unreadable`.
- An empty custom parameter: `read-input-custom-parameter-empty`, `read-scoop-empty-custom-parameter`.
- U+0000, U+000A, U+000D in a custom parameter or `Source.Name`: `read-request-custom-parameter-{nul,line-feed,carriage-return}`, `read-request-source-name-{nul,line-feed,carriage-return}`, controls with U+0009; in `Package.Id`: `read-input-package-id-line-feed`.
- `Source.Name` with leading or trailing `White_Space`, or any `Default_Ignorable_Code_Point`: `read-request-source-name-leading-*`, `-trailing-*`, `-ignorable-*`, controls `read-request-source-name-inner-*`.
  The sampled code points include U+180E, U+200B and U+FEFF, which are ignorable but not `White_Space` in Unicode 16.0.0.
- Elements are neither split nor joined: `read-request-custom-parameter-not-split`, `read-request-custom-parameter-not-joined`.
- A pattern matches one element, and an option's value is a custom parameter: `read-request-pattern-*`, `read-request-option-value-*`.
- A custom parameter with the effect of another opt-in needs that opt-in's grant: the `read-winget-*` and `read-scoop-*` cases whose decision is `UngrantedOptIn`, control `read-winget-interactive-granted`.
- A property set by a field and a custom parameter, or by two custom parameters, is unknown: `read-request-scope-*`, `read-request-architecture-*`.
- `Options.PreRelease` has no effect: `read-version-pre-release-flag-ignored-*`.

### §request-properties

- Elevation, including that it reads the `Options.Scope` part rather than the scope property and is never unknown: `read-elevation-*`.
- Operation: `read-operation-*`.
- Source: `read-scoop-*` trust cases and `read-identity-npm-*-source-*`.
- Scope and architecture: see §managers.

### §package-identity

- Default reading, `name@version` split at the last `@` that is not first, canonical lowercase, pattern canonicalization: `read-identity-default-*`.
- A match only when case is ignored is unknown, as stated by "a name they would match ignoring case makes it unknown", including a pattern with U+017F that matches only under simple uppercase: `read-identity-default-unicode-case-only-*`, `read-identity-npm-case-only-mismatch-*`, `read-identity-bun-case-kept-*`.
- Every name versus some names (`Packages` matches, is unknown, or does not match): `read-identity-npm-alias-*`.
- Unknown identity, which even `*` does not match: `read-identity-npm-name-at-version-unknown-*`, `read-identity-npm-not-registry-*`, `read-identity-bun-alias-unknown-*`.

### §version-scheme

`reading-version.json`.

- Numeric components, leading zeros, missing trailing zeros, `v` and `V`, build metadata, components beyond 64 bits: `read-version-*` with Winget requests.
- Strings that are not versions give an unknown condition: `read-version-not-a-version-*`.
- Pre-release precedence per Semantic Versioning 2.0.0, including ASCII order and numeric identifiers beyond 64 bits: `read-version-pre-release-*` with Npm requests.
- Pre-release status, and none under Winget: `read-version-pre-release-excluded`, `read-version-release-not-excluded`, `read-version-build-metadata-not-pre-release`, `read-version-winget-never-pre-release`, `read-version-pre-release-satisfies-without-exclude`.
- Comparisons use the pre-release under Winget too: `read-version-winget-pre-release-below-release`, `read-version-winget-release-above-pre-release`, `read-version-winget-pre-release-order`, control `read-version-winget-pre-release-equal`.

### §managers

- Unbuilt managers: `read-request-no-command-*`.
- Defaults: no custom parameters (`read-<manager>-custom-parameters-unreadable`), scope from `Options.Scope` and `User` when absent (`read-scope-default-*`), architecture from `Package.Architecture` and unknown when absent (`read-architecture-default-*`, `read-architecture-unknown-matches-deny`), a channel makes no request unreadable (`read-input-channel-1-byte`, `read-npm-channel-readable`, `read-pip-channel-readable`).
- The only source name compared ignoring ASCII case, and not under full Unicode case: `read-{npm,bun,cargo,pip}-source-*`, including `read-cargo-source-long-s` and `read-pip-source-dotless-i`.
- `Winget` and `Scoop` ignore `Source.Url`, and the source stays `Source.Name`: `read-winget-url-readable`, `read-winget-url-empty-readable`, `read-scoop-url-readable`, and the trust pairs `read-winget-url-not-source` and `read-winget-url-source-from-name`, `read-scoop-url-not-source` and `read-scoop-url-source-from-name`.
- Every other manager without its own URL rule refuses any `Source.Url`: `read-{powershell,powershell7,chocolatey,pip,vcpkg}-url-unreadable`, `read-npm-url-cargo-index-unreadable`, `read-dotnet-url-nuget-under-chocolatey`, `read-request-source-url-*`, each against its manager's `read-<manager>-plain`; on other operations, `read-chocolatey-url-uninstall-unreadable` against `read-chocolatey-uninstall-plain` and `read-pip-url-update-unreadable` against `read-pip-update-plain`.

### §manager-winget

`reading-winget.json`.

- Each table row: `read-winget-location*`, `read-winget-scope-*`, `read-winget-architecture-*`, `read-winget-interactive-*`, `read-winget-override*`, `read-winget-custom`, `read-winget-ignore-security-hash`, `read-winget-force*`, `read-winget-no-upgrade`, `read-winget-uninstall-previous`, `read-winget-version-*`.
- Long options ignore ASCII case and short options do not: the `-any-case` and `-wrong-case` cases.
- Option names: `read-winget-long-option-*`, `read-winget-triple-hyphen`, `read-winget-single-hyphen`, `read-winget-short-o-not-listed`.
- The `--` terminator: `read-winget-terminator-*`, `read-winget-location-after-terminator`, `read-winget-scope-terminator-stops-second`.
- `--` taken as an option's value, which then ends nothing: `read-winget-override-value-terminator`, `read-winget-location-value-terminator` with its control `read-winget-location-value-plain-path`.
- Short-option clusters: `read-winget-cluster-*`, `read-winget-location-in-cluster`, `read-winget-architecture-in-cluster*`.
- Attached values with and without `"`, the first `=`, one quote, doubled quotes, an empty value: the `-attached*`, `-quoted`, `-first-equals`, `-one-quote*`, `-doubled-quotes`, `-empty*` cases.
- A value taken from the next element, which is then not an option: `read-winget-override-value-not-option`, `read-winget-attached-value-frees-next`; quotes kept there: `-next-element-quoted`.
- A missing value at the end: the `-missing-value` cases.
- Scope unknown when nothing sets it: `read-winget-scope-not-set`, `read-scope-unknown-matches-deny`.

### §manager-scoop

`reading-scoop.json`: the `--skip-hash-check` and `s` cluster row, the `--arch` and `a` cluster row, cluster shape, version and channel unreadable, and bucket, manifest URL and local path sources as known sources.

- Option names in any ASCII case: `read-scoop-skip-hash-check-upper`, `read-scoop-short-upper-s`, `read-scoop-architecture-short-upper-a`, `read-scoop-architecture-arch-upper-attached`.
- A name ends at the first `=`: `read-scoop-skip-hash-check-attached`, `read-scoop-architecture-arch-attached`, `read-scoop-cluster-s-after-equals`; longer names with the same prefix are not the option: `read-scoop-skip-hash-check-longer-name`, `read-scoop-architecture-longer-name`.
- A cluster with a later `-` is still one cluster: `read-scoop-cluster-hyphen-inside`.
- A version on an Uninstall: `read-scoop-version-unreadable-uninstall`, control `read-scoop-uninstall-plain`.

### §manager-vcpkg, §manager-npm, §manager-bun, §manager-cargo, §manager-pip, §manager-dotnet, §manager-chocolatey, §manager-powershell

`reading-managers.json` and the manager cases of `reading-identity.json` and `reading-missing-version.json`.

- Vcpkg: triplet architecture by prefix, ignoring ASCII case, `Package.Architecture` first; version unreadable; the name is the text before the first `[` or `:`, with no `@` split: `read-identity-vcpkg-port-features-triplet-*`, `read-identity-vcpkg-triplet-only-*`, `read-identity-vcpkg-features-only-*`, `read-identity-vcpkg-at-not-split-*`, `read-identity-vcpkg-at-whole-name-*`.
- Npm: registry names, aliases with and without version, other identifiers with unknown identity and source, case kept, `Neutral` architecture, version ranges.
  Identifiers that select the version on Install and Update (`name@version`, `@scope/name@version`, alias with version, `#`): `read-missing-version-npm-{name-at-version,name-at-version-update,scoped-name-at-version,alias-selects,alias-selects-update,alias-scoped-target-selects,hash-selects,hash-selects-update}`, with controls `read-missing-version-npm-{three-components,update-three-components,scoped-name,alias,alias-update,alias-scoped-target,git-specifier,git-specifier-update}`.
- Bun: aliases unread, source `Bun`, the `https://www.npmjs.com` URL rule, channel unreadable; the URL rule ignores only ASCII case, `read-bun-url-long-s` against `read-bun-url-registry`, and covers an Uninstall, `read-bun-url-uninstall-other-host` against `read-bun-url-uninstall-registry`; case kept in canonical form: `read-identity-bun-case-kept-*`, control `read-identity-bun-case-exact-*`; version selected as under Npm: `read-missing-version-bun-{name-at-version,name-at-version-update,alias-selects,hash-selects}`, controls `read-missing-version-bun-{three-components,update-three-components,alias,git-specifier}`.
- Cargo: `_` to `-`, source, channel unreadable, `Neutral` architecture; `Source.Url` accepted only as `https://index.crates.io` after trailing `/` are dropped, ignoring ASCII case: `read-cargo-url-{index,trailing-slashes,any-case}`, near misses `read-cargo-url-{other-host,sparse-prefix,path,empty,dotless-i}`, where `dotless-i` matches only under Unicode case; the rule covers an Uninstall: `read-cargo-url-uninstall-other-host` against `read-cargo-url-uninstall-index`.
- Pip: PEP 503 names, source, `Neutral` architecture, unknown uninstall scope.
- Dotnet: `Neutral` makes the architecture unknown; on Install and Update `Source.Url` is accepted only as `https://api.nuget.org/v3/index.json` ignoring ASCII case, or empty or `White_Space` only: `read-dotnet-url-{nuget,any-case,empty,white-space-only,update-nuget}`, near misses `read-dotnet-url-{trailing-slash,other,surrounding-space,zero-width-space,update-other,dotless-i}`, where `dotless-i` matches only under Unicode case; on Uninstall it has no effect: `read-dotnet-url-uninstall-{other,zero-width-space}`.
- Chocolatey: `Machine` scope by default, `X64` makes the architecture unknown.
- PowerShell and PowerShell7: source name characters, `Neutral` architecture always, unknown uninstall scope.

### §install-location

`reading-location.json`: the grammar (`read-location-plain-*`, `read-location-not-plain-*`), normalization and pattern normalization (`read-location-normalized-*`, `read-location-pattern-*`, `read-location-drive-root-*`), Unicode simple case (`read-location-unicode-*`), counting locations from the field and custom parameters (`read-location-one-from-parameter`, `read-location-field-and-*`, `read-location-two-parameters`), and the check applying whatever the rules say (`read-location-check-before-rules`).
Winget's own location cases are in `reading-winget.json`.

- A space is only U+0020, so other white space starts or ends a segment freely: `read-location-plain-segment-leading-no-break-space`, `read-location-plain-segment-trailing-no-break-space`, `read-location-plain-segment-trailing-ideographic-space`, against `read-location-not-plain-segment-leading-space` and `read-location-not-plain-segment-trailing-space`.
- Repeated separators right after the drive: `read-location-plain-repeated-after-drive`, `read-location-normalized-repeated-after-drive`, `read-location-drive-root-repeated-separators`.

### §missing-version

`reading-missing-version.json`: concrete and missing versions per manager, ranges under Npm, Bun, Cargo and Dotnet, versions selected by the identifier or a custom parameter, uninstalls never missing, Scoop and Vcpkg never missing with an unknown version, and the check applying whatever the rules say.

- An Npm Update with a range and with three components: `read-missing-version-npm-update-range`, `read-missing-version-npm-update-three-components`.
- PowerShell7 resolves anything but a version as a range: versions `read-missing-version-powershell7-{two-components,three-components,one-component,pre-release,update-concrete}`; a range, a wildcard or no version on Install and Update `read-missing-version-powershell7-{range,wildcard,absent,update-range,update-absent}`; an Uninstall never missing `read-missing-version-powershell7-uninstall-{absent,range}`.

## Verified by review only

- §manager-scoop: an element not starting with `-` that contains `@` makes the version unknown.
  Scoop's version is always unknown, so no decision differs.
- §manager-scoop: which source names are manifest URLs or local paths.
  Both kinds are known sources like buckets, so only that they are known is observable, and the cases check that.
- §reading: the broker runs the command whose reading it evaluated, and a change to how it builds commands changes the contract.
  These bind the broker, not the evaluator's decision.
- §install-location: the check never asks the operating system about a drive.
  `read-location-plain-other-drive` uses `Z:`, but a runner cannot show that no lookup happened.

## Gaps

None: the contract decides every clause of this part that a case could reach.

## Deliberately unchecked

- That an `Options` field carries its opt-in whatever the manager and operation is checked by `evaluation/eval-carriage.json` (see `COVERAGE.evaluation.md`), so the cases here do not repeat it.
- A boolean part with a value of another JSON type: the case format's request is typed JSON, and a runner may reject such a request before evaluation.
- The full `White_Space` and `Default_Ignorable_Code_Point` sets: the cases sample code points that tell the Unicode 16.0.0 properties from common platform whitespace tests.
- Each `Package.Id` character on its own: one case uses every allowed character, and several cases one character outside the set.
- Opt-ins carried by fields, such as an empty `Options.PreOperationCommand`, belong to §opt-ins and are not repeated here.
