# Policy contract

This contract defines the 2.x policy format and how a policy decides a request.
A policy decides which package installs, updates and uninstalls the broker runs for users, often with administrator rights.
The parties are the package broker inside Devolutions Agent, editors that author policies, such as RDM and UniGetUI, and a central authority that pushes policies to agents.
The broker evaluates requests with a Rust implementation and editors use a C# implementation, and every rule here binds both.
`policies/INTENT.md` explains the design choices; this contract states the rules.

<a id="definitions"></a>
## Definitions

<a id="def-broker"></a>
### Broker

The broker is the package broker inside Devolutions Agent.
It evaluates each request against its policy and runs only the requests whose decision allows them.

<a id="def-editor"></a>
### Editor

An editor is a program that authors policies, such as RDM or UniGetUI.

<a id="def-manager"></a>
### Manager

A manager is a package manager, named by an identifier from [§managers](#managers).

<a id="def-source"></a>
### Source

A source is where a manager gets packages from, identified by the manager and the source's name, such as WinGet `winget`, PowerShell `PSGallery` or a Scoop bucket.
Which sources a policy accepts is set by its [trusted sources](#trusted-sources).
*Avoid:* repository, feed, registry, bucket, except when quoting a specific manager.

<a id="def-request"></a>
### Request

A request asks the broker to install, update or uninstall one package through one manager ([§request-input](#request-input)).
The broker carries it out by running a command: the manager invocation with every argument, and whatever the broker runs or stops before or after it.
The evaluator reads a request the way the broker will execute it ([§reading](#reading)), and the result is the request's [properties](#request-properties) and opt-ins.

<a id="def-opt-in"></a>
### Opt-in

An opt-in is something a request asks for beyond a plain request; [§opt-ins](#opt-ins) lists every opt-in.
Example: WinGet `--override` and `--custom` pass installer arguments, which a request asks for with `InstallerArguments` rather than as custom parameters ([§manager-winget](#manager-winget)).
*Avoid:* option, modifier, characteristic, flag, extra.

<a id="def-plain-request"></a>
### Plain request

A plain request uses no opt-ins.

<a id="def-rule"></a>
### Rule

A rule is an entry of a policy's `Rules` whose `Decision` is `Allow` or `Deny` ([§rules](#rules)).
An Allow rule permits plain requests and grants opt-ins; a Deny rule denies requests or forbids opt-ins.

<a id="def-selection"></a>
### Selection

A selection is an object of conditions on request properties ([§selection](#selection)).
A rule picks the requests it applies to with `Select`, narrowed by `Except`.

<a id="def-applicable"></a>
### Applicable rule

A rule is applicable to a request when it is enabled, its `Select` matches the request and its `Except` does not, with unknown properties resolved as [§applicability](#applicability) states.

<a id="def-grants"></a>
### Grants and Forbids

An Allow rule `Grants` opt-ins, and a Deny rule `Forbids` them ([§grants](#grants)).
Every grant is an object, even one with no fields, and so is every forbid.

<a id="def-elevation"></a>
### Elevation

Elevation is whether a request asks the broker to run it with administrator rights ([§request-properties](#request-properties)).
Elevation is a property a selection can test, not an opt-in, and neither of its values needs a grant.

<a id="def-unknown"></a>
### Unknown

A request property is unknown when reading the request does not fix its value ([§reading](#reading)).
Evaluation resolves every unknown toward denial ([§applicability](#applicability)).

<a id="def-pre-release"></a>
### Pre-release

A version is a pre-release only when its manager's version scheme marks it as one ([§version-scheme](#version-scheme)).

<a id="def-audit-mode"></a>
### Audit mode

Audit mode is a policy setting under which a request that the policy content denies runs anyway, and its decision records the denial ([§audit-mode](#audit-mode)).

<a id="def-decision"></a>
### Decision

A decision is the result of evaluating one request: whether the broker runs it, and the check, rules and opt-ins that produced that outcome ([§decision](#decision)).

<a id="general"></a>
## General rules

These rules apply throughout this contract and are not repeated.

<a id="text-comparison"></a>
### Text comparison

Two strings are equal ignoring case when they have the same code points after each code point is replaced by its simple uppercase mapping in `UnicodeData.txt` of [Unicode 16.0.0][unicode16]; no other normalization applies.
Implementations use that table rather than their platform's casing, so they agree.
Custom parameters, install locations, source names and the patterns that match them compare ignoring case.
Package ids compare in their manager's canonical form ([§package-identity](#package-identity)).
Rule ids, and values from a closed set this contract defines, such as `Allow` or `Winget`, match only with their exact characters.

<a id="patterns"></a>
### Patterns

A pattern is a non-empty string matched against a whole value.
In a pattern, `*` matches any sequence of zero or more characters, and every other character matches one character equal to it under the comparison that applies to the value.
A pattern has no escape character.

<a id="json"></a>
### Documents

A policy document is one JSON value as [RFC 8259][rfc8259] defines it, encoded in UTF-8, and its top-level value is an object.
A document is at most 4 MiB (4,194,304 bytes), counting every byte, including a byte order mark.
A reader ignores one leading UTF-8 byte order mark.
A document is malformed when it is not valid UTF-8, is not well-formed RFC 8259 JSON, has a top-level value that is not an object, contains a string with an unpaired surrogate, or nests arrays and objects more than 64 levels deep, counting the top-level object as level 1.
Comments, trailing commas and other extensions to RFC 8259 are not well-formed.
The magnitude or precision of a number never makes a document malformed.
Keys match only with their exact characters, after JSON escapes are decoded.
`null` is a value of the wrong type wherever it appears.
Key order and whitespace carry no meaning, and writers may choose any.
The order of entries in a list carries no meaning, except the order in which a decision names rules.
Except for rules, whose ids are unique, a list may repeat an entry with no effect.

<a id="timestamps"></a>
### Timestamps

A timestamp is a string of the form `YYYY-MM-DDTHH:MM:SS`, optionally followed by `.` and 1 to 9 digits, then by `Z`, `+HH:MM` or `-HH:MM`, which is [RFC 3339][rfc3339] `date-time`.
The date exists in the Gregorian calendar, the year is 0001 to 9999, hours are 00 to 23, minutes and seconds are 00 to 59, `T` and `Z` are uppercase, and an offset is at most 23:59; any other string is `InvalidValue`.
Timestamps compare as instants, after applying each one's offset, at nanosecond precision.

<a id="document"></a>
## Policy document

<a id="document-keys"></a>
### Top-level keys

| Key                   | Required | Value                                                                              |
| --------------------- | -------- | ---------------------------------------------------------------------------------- |
| `PolicyFormatVersion` | yes      | the string `"2.0.0"` ([§format-version](#format-version))                          |
| `Metadata`            | yes      | a metadata object ([§metadata](#metadata))                                         |
| `Enforcement`         | no       | an enforcement object ([§audit-mode](#audit-mode))                                 |
| `TrustedSources`      | yes      | a list of source selectors, possibly empty ([§trusted-sources](#trusted-sources)) |
| `Rules`               | yes      | a list of rules, possibly empty ([§rules](#rules))                                 |

The author of a document sets every value in it, and the broker never changes a document it accepts.

<a id="format-version"></a>
### Format version

`PolicyFormatVersion` identifies the format of a document.
A document whose `PolicyFormatVersion` is `"2.0.0"` is in format 2.x, and this contract defines no other value.
Any other `PolicyFormatVersion` value, including a value that is not a string, is `UnsupportedFormatVersion`.
A reader refuses a format version it does not implement rather than reading the document partially, because a newer version may change what an existing key means.

<a id="metadata"></a>
### Metadata

| Key           | Required | Value                                                                                   |
| ------------- | -------- | --------------------------------------------------------------------------------------- |
| `Id`          | yes      | a non-empty string                                                                      |
| `Publisher`   | yes      | a non-empty string                                                                      |
| `Revision`    | yes      | an integer from 1 to 2147483647, written without a fraction or an exponent              |
| `PublishedAt` | yes      | a timestamp ([§timestamps](#timestamps))                                                |
| `ValidFrom`   | no       | a timestamp ([§validity-window](#validity-window))                                      |
| `ValidUntil`  | no       | a timestamp ([§validity-window](#validity-window))                                      |
| `Description` | no       | a string                                                                                |
| `SupportUrl`  | no       | a non-empty string                                                                      |

A `Revision` number outside that range or written with a fraction or an exponent is `InvalidValue`.
Evaluation reads only `ValidFrom` and `ValidUntil`; the other metadata identifies the document for its readers.

<a id="validity-window"></a>
### Validity window

When `ValidFrom` and `ValidUntil` are both present and `ValidFrom` is later than `ValidUntil`, the document is `InvalidValue` at `ValidUntil`.
The policy is in force at an evaluation time that is not earlier than `ValidFrom` and not later than `ValidUntil`, and an absent key leaves that side open.
Outside that period every request is denied ([§precedence](#precedence)), and the broker does not fall back to another policy.

<a id="trusted-sources"></a>
### Trusted sources

`TrustedSources` lists, as source selectors, the sources that installs and updates may get packages from.
The list governs installs and updates only, because uninstalls download nothing; rules alone govern uninstalls.
The source of an Install or Update request is trusted when a selector in the list matches it ([§source-selector](#source-selector)); a selector that is unknown for the request does not trust it.
An Install or Update request whose source is not trusted is denied whatever the rules say ([§precedence](#precedence)).
A manager with no selector in the list therefore cannot install or update anything.
A rule may name a source the list does not trust, which governs uninstalls of packages from that source; for installs and updates, a rule only narrows which trusted sources it applies to.

<a id="source-selector"></a>
### Source selector

A source selector names one manager and either one of its sources or all of them.

| Key         | Required                    | Value                                          |
| ----------- | --------------------------- | ---------------------------------------------- |
| `Manager`   | yes                         | a manager identifier ([§managers](#managers)) |
| `Source`    | exactly one of the two keys | a non-empty string, which names a source       |
| `AnySource` | exactly one of the two keys | `true`                                         |

A selector with both `Source` and `AnySource` is `ConflictingKeys` at `AnySource`, and one with neither is `MissingKey` at `Source`.
`AnySource` with the value `false` is `InvalidValue`, because such a selector would name no source.
A selector does not match a request through another manager.
For a request through its manager, a selector with `AnySource` matches whatever the source, including an unknown one.
For a request through its manager, a selector with `Source` matches a known source equal to it ignoring case ([§text-comparison](#text-comparison)), does not match a different known source, and is unknown for an unknown source.

<a id="rules"></a>
### Rules

| Key        | Required | Value                                                                               |
| ---------- | -------- | ----------------------------------------------------------------------------------- |
| `Id`       | yes      | a string of an ASCII letter or digit followed by ASCII letters, digits, `.`, `_`, `:` and `-` |
| `Enabled`  | no       | a boolean; absent means `true`                                                      |
| `Decision` | yes      | `Allow` or `Deny`                                                                   |
| `Reason`   | no       | a string, which a decision naming the rule carries                                  |
| `Select`   | yes      | a selection ([§selection](#selection)); `{}` selects every request                 |
| `Except`   | no       | a non-empty list of selections, none of them `{}`                                   |
| `Grants`   | no       | on an Allow rule, a non-empty object of grants ([§grants](#grants))                 |
| `Forbids`  | no       | on a Deny rule, a non-empty object of forbids ([§grants](#grants))                  |

A rule whose `Id` equals the `Id` of an earlier rule is `DuplicateRuleId` at its `Id`.
`Grants` on a rule whose `Decision` is `Deny` is `ConflictingKeys` at `Grants`, and `Forbids` on a rule whose `Decision` is `Allow` is `ConflictingKeys` at `Forbids`.
An `Except` entry that is `{}` is `InvalidValue`, because it would except every request and silently disable its rule.
A rule whose `Enabled` is `false` is never applicable.
`Select` is required so that a rule selecting every request says so with `{}`.
An Allow rule without `Grants` permits plain requests only.
A Deny rule without `Forbids` denies every request it applies to; a Deny rule with `Forbids` denies only requests that use an opt-in it forbids ([§opt-in-evaluation](#opt-in-evaluation)).
Because the default decision is always Deny, a deny-list policy is written as a catch-all Allow rule with the grants it permits, plus Deny rules.

<a id="selection"></a>
### Selection

A selection is an object whose keys are conditions, and an absent condition places no constraint.
The values of operations, scopes, architectures and elevations are those of [§request-properties](#request-properties).

| Condition       | Value                                                                       | Holds when the request's            |
| --------------- | --------------------------------------------------------------------------- | ----------------------------------- |
| `Operations`    | a non-empty list of operations                                              | operation is in the list            |
| `Sources`       | a non-empty list of source selectors ([§source-selector](#source-selector)) | source matches a selector           |
| `Packages`      | a non-empty list of patterns ([§patterns](#patterns))                       | package names match ([§package-identity](#package-identity)) |
| `Version`       | a version condition ([§version-condition](#version-condition))              | version satisfies the condition     |
| `Scopes`        | a non-empty list of scopes                                                  | scope is in the list                |
| `Architectures` | a non-empty list of architectures                                           | architecture is in the list         |
| `Elevation`     | one elevation                                                               | elevation is that elevation         |

`Packages` tests package names whatever the manager, so a rule meant for one manager's package should also name that manager in `Sources`.

<a id="version-condition"></a>
### Version condition

| Key          | Required | Value                                                      |
| ------------ | -------- | ---------------------------------------------------------- |
| `Minimum`    | no       | a version ([§version-scheme](#version-scheme))             |
| `Maximum`    | no       | a version ([§version-scheme](#version-scheme))             |
| `Prerelease` | no       | `Exclude`                                                  |

A version condition with none of these keys is `InvalidValue`, because it would constrain nothing.
A `Minimum` or `Maximum` that is not a version is `InvalidValue`, and a `Minimum` higher than the `Maximum` is `InvalidValue` at `Maximum`.
A version satisfies the condition when it is not lower than `Minimum`, is not higher than `Maximum`, and, when `Prerelease` is present, is not a pre-release; an absent bound places no constraint.
Without `Prerelease`, a pre-release satisfies the condition like any other version.
The condition is unknown when the request's version is unknown or is not a version.
A selection with `Version`, in `Select` or in an `Except` entry, also has `Operations` without `Uninstall`, because an uninstall carries no version a policy can check: the manager removes whichever version is installed.
Such a selection without `Operations` is `MissingKey` at `Operations`, and one whose `Operations` lists `Uninstall` is `ConflictingKeys` at `Version`.
So no version condition applies to an uninstall, and a rule that tests the version says so by listing its operations rather than silently never matching uninstalls.
`Prerelease` is checked against the version the command installs.

<a id="grants"></a>
### Grants and Forbids

`Grants` and `Forbids` are objects whose keys are opt-ins from [§opt-ins](#opt-ins) and whose values are grant or forbid objects.
A grant or forbid for `CustomParameters` or `CustomInstallLocation` has these keys, and a grant or forbid for any other opt-in is an object with no keys, so a key in it is `UnknownKey`.

| Key        | Required | Value                                                  |
| ---------- | -------- | ------------------------------------------------------ |
| `Patterns` | yes      | a non-empty list of patterns ([§patterns](#patterns)) |
| `Except`   | no       | a non-empty list of patterns                           |

A value is covered by a grant or forbid when it matches one of its `Patterns` and none of its `Except` patterns.
For `CustomParameters`, the values are the request's custom parameters, each element on its own, option values included; for `CustomInstallLocation`, the value is the request's normalized install location ([§install-location](#install-location)).
Before a `CustomInstallLocation` pattern is matched, each `/` in it becomes `\`, repeated `\` are collapsed, and a trailing `\` is dropped unless it follows a `:`, as an install location is normalized, so `C:/Apps/*` matches `C:\Apps\Tool`.
A grant's or forbid's `Except` narrows only that grant or forbid, never another one and never another rule.

<a id="opt-ins"></a>
### Opt-ins

This table is the closed set of opt-ins, and its order is the order in which a decision names them.
A request uses an opt-in exactly when the opt-in's key is present in its `OptIns` ([CONTRACT.wire.md §opt-ins](CONTRACT.wire.md#opt-ins)), and the opt-ins a request uses are never unknown.
No custom parameter makes a request use an opt-in; one that would ask for another opt-in makes the request unreadable ([§reading](#reading)).

| Opt-in                  | Meaning                                                                                  |
| ----------------------- | ---------------------------------------------------------------------------------------- |
| `Interactive`           | the installer shows its user interface                                                   |
| `SkipHashCheck`         | the manager skips its check of the installer's hash                                      |
| `CustomInstallLocation` | the request sets where the package is installed                                          |
| `CustomParameters`      | the manager receives custom parameters ([§reading](#reading))                            |
| `InstallerArguments`    | the installer itself receives arguments                                                  |
| `PrePostCommands`       | the broker runs the `Pre` or `Post` command around the manager process ([CONTRACT.wire.md §operations](CONTRACT.wire.md#operations)) |
| `KillBeforeOperation`   | the broker stops the processes of `Processes` before the manager process and any `Pre` command ([CONTRACT.wire.md §operations](CONTRACT.wire.md#operations)) |
| `UninstallPrevious`     | the manager uninstalls the installed version before installing another                   |
| `NoUpgrade`             | the manager does not upgrade a package that is already installed                         |

<a id="errors"></a>
### Validation errors

A document is valid when it has none of these errors, and this table is the closed set of errors a reader reports for a document.

| Error                      | The document                                                                     |
| -------------------------- | -------------------------------------------------------------------------------- |
| `DocumentTooLarge`         | exceeds the size limit of [§json](#json)                                         |
| `MalformedDocument`        | is malformed ([§json](#json))                                                    |
| `DuplicateKey`             | has an object that repeats a key                                                 |
| `UnsupportedFormatVersion` | has a `PolicyFormatVersion` this contract does not define ([§format-version](#format-version)) |
| `MissingKey`               | lacks a key that is required at that place                                       |
| `UnknownKey`               | has a key this contract does not define at that place                            |
| `ConflictingKeys`          | has a key that another key or value in the same object excludes                  |
| `WrongType`                | has a value of another JSON type than its key requires                           |
| `InvalidValue`             | has a value of the right JSON type outside the domain its key allows             |
| `DuplicateRuleId`          | has two rules with equal ids                                                     |

In every object this contract defines, an absent required key is `MissingKey`, a key the object's table does not list is `UnknownKey`, and a value of the wrong JSON type is `WrongType`, except at `PolicyFormatVersion`, where every present value other than `"2.0.0"` is `UnsupportedFormatVersion`.
Every error carries its code and a [JSON Pointer][rfc6901] to where it is:

- for `DuplicateKey`, `UnknownKey` and `ConflictingKeys`, the member;
- for `MissingKey`, the absent member;
- for `WrongType`, `InvalidValue` and `UnsupportedFormatVersion`, the value, which is the entry when the defect is in one entry of a list;
- for `DuplicateRuleId`, the later rule's `Id`;
- for `DocumentTooLarge` and `MalformedDocument`, the whole document, which is the empty pointer.

A document with exactly one defect reports exactly that error.
A document with several defects reports a non-empty subset of the errors that apply to it, and which subset is implementation-defined, except as follows:

- when `DocumentTooLarge` applies, it is the only error;
- otherwise, when `MalformedDocument` applies, it is the only error;
- otherwise, when `DuplicateKey` applies, only `DuplicateKey` errors are reported;
- otherwise, when `PolicyFormatVersion` is absent or `UnsupportedFormatVersion` applies, that is the only error.

<a id="requests"></a>
## Requests

<a id="request-input"></a>
### Request input

The evaluator's input is a well-formed package request ([CONTRACT.wire.md §package-request](CONTRACT.wire.md#package-request)) without its `RequestId`.

<a id="reading"></a>
### Reading a request

Each property is the value that the manager's reading ([§managers](#managers)) assigns to the request, which is the value the manager acts on when the broker runs the command, so a change to how the broker builds commands is a change to this contract.
A value the client sends that the command does not carry has no effect on any property.
An `OptIns` key makes the request use its opt-in even when the command does not carry what the key asks for, so a decision depends only on the policy and the request, never on what a broker version does with the key.
A request is unreadable when:

- the broker builds no command for its manager ([§managers](#managers));
- it carries custom parameters through a manager whose reading accepts none;
- it has `OptIns.InstallerArguments` through a manager whose reading accepts none;
- a custom parameter would ask for an opt-in other than `CustomParameters`, as its manager's reading states, so that every opt-in the command asks for is a key of its `OptIns`;
- its scope property is `Machine`, its `Scope` member is absent or `User`, and its `Elevation` is `NonElevated`, whatever its operation, because it would install, update or uninstall machine-wide without saying so in `Scope`;
- a custom parameter makes its scope unknown, such as a scope option without a valid value or a scope set twice, and its `Elevation` is `NonElevated`, because it may then act machine-wide without saying so in `Scope`; a scope that its manager's reading leaves unknown without a custom parameter, such as WinGet's without `Scope` and `--scope`, keeps the request readable;
- its `Package.Version` is present and its manager cannot pin a version ([§managers](#managers));
- it is through `Winget` or `Dotnet` and its `Package.Id` starts with `-`, so that the manager never reads the id as an option;
- a custom parameter, `OptIns.InstallerArguments.Arguments`, `Package.Id` or `Source.Name` contains U+0000, U+000A or U+000D, which the broker cannot pass to a manager;
- `Source.Name` starts or ends with a code point that has the `White_Space` property in `PropList.txt` of [Unicode 16.0.0][unicode16-ucd], or contains a code point that has the `Default_Ignorable_Code_Point` property in `DerivedCoreProperties.txt` of [Unicode 16.0.0][unicode16-ucd], because such a name looks like another;
- its manager's reading states another case.

Custom parameters are the elements of `OptIns.CustomParameters.Values`, which the broker passes to the manager as they are and in their order, repeated elements included, neither splitting nor joining them.
Every element is a custom parameter, including an option's value, and a pattern is matched against one element.
A custom parameter that sets or affects a property affects it as its manager's reading states.
When a property is set both by a field and by a custom parameter, or by two custom parameters, it is unknown.
The broker runs the command whose reading it evaluated, so the version a decision checked is the version the manager installs.

<a id="request-properties"></a>
### Request properties

This table is the only list of the values of request properties.

| Property     | Values                                 | Read from                                                                   |
| ------------ | -------------------------------------- | --------------------------------------------------------------------------- |
| Operation    | `Install`, `Update`, `Uninstall`       | `Operation`; never unknown                                                  |
| Manager      | an identifier from [§managers](#managers) | `Manager`; never unknown                                                 |
| Source       | a source name                          | `Source.Name`, unless the manager's reading makes it unknown                |
| Package names | one or more package ids               | [§package-identity](#package-identity)                                      |
| Version      | a version string                       | [§missing-version](#missing-version)                                        |
| Scope        | `User`, `Machine`                      | the manager's reading                                                       |
| Architecture | `X86`, `X64`, `Arm64`, `Neutral`       | the manager's reading                                                       |
| Elevation    | `NonElevated`, `Elevated`              | `Elevation`; never unknown                                                  |

<a id="package-identity"></a>
### Package identity

A request's package names are the packages its `Package.Id` names, as its manager's reading parses the identifier.
Unless the manager's reading states otherwise, an identifier `name@version`, where that `@` is its last `@` and is not its first character, names `name` and selects the version, and any other identifier names one package, itself.
A package name compares in its manager's canonical form, and a pattern's characters between `*` are put in that canonical form before matching.
Unless the manager's reading states otherwise, the canonical form maps ASCII letters to lowercase.
For each package name, the `Packages` patterns match when one matches it in canonical form, are unknown when none does but one would when case is ignored ([§text-comparison](#text-comparison)), and do not match otherwise.
`Packages` matches when the patterns match every package name, does not match when, for every package name, the patterns do not match it, and is unknown otherwise, so a name they would match only ignoring case makes it unknown.
`Packages` is unknown when the manager's reading makes the package identity unknown.
So an Allow rule's selection matches only when it covers every package the identifier names, and a Deny rule's selection matches when it covers any of them, whatever their case.

<a id="version-scheme"></a>
### Version scheme

A version is an optional `v` or `V`, then one or more release components of ASCII digits separated by `.`, then optionally `-` and a pre-release of `.`-separated identifiers of ASCII letters, digits and `-`, then optionally `+` and build metadata of zero or more ASCII letters, digits, `-` and `.`.
Each pre-release identifier is non-empty, and one made only of digits has no leading zero unless it is `0`, as [Semantic Versioning 2.0.0][semver] requires.
Release components compare numerically, whatever their length or leading zeros, and missing trailing components count as zero, so `1.2` equals `1.2.0`.
A version with a pre-release is lower than the same release without one, and pre-releases compare by [Semantic Versioning 2.0.0][semver] precedence.
Build metadata never affects a comparison.
A version is a pre-release when it has a pre-release, except under `Winget`, where no version is a pre-release; comparisons use the pre-release under every manager.

<a id="managers"></a>
### Managers

The manager identifiers are `Winget`, `PowerShell`, `PowerShell7`, `Apt`, `Bun`, `Cargo`, `Chocolatey`, `Dnf`, `Dotnet`, `Flatpak`, `Homebrew`, `Npm`, `Pacman`, `Pip`, `Scoop`, `Snap` and `Vcpkg`.
The broker builds commands only for `Winget`, `PowerShell`, `PowerShell7`, `Bun`, `Cargo`, `Chocolatey`, `Dotnet`, `Npm`, `Pip`, `Scoop` and `Vcpkg`; a request through any other manager is unreadable, and a policy may still name every manager.
Unless a manager's reading below states otherwise:

- the reading accepts no custom parameters;
- the reading accepts no installer arguments;
- the broker can pin a version;
- an Install or Update whose `Package.Version` starts with `-`, or contains a character other than ASCII letters, digits, `.`, `-`, `+`, `_` and those the reading adds, is unreadable;
- the scope is `Scope`, and `User` when it is absent;
- the architecture is `Package.Architecture`, and unknown when it is absent.

A reading below that names the only source name of its manager compares `Source.Name` with it ignoring ASCII case.
A plain source name starts with an ASCII letter or digit and contains only ASCII letters, digits, `.`, `-`, `_` and spaces.
The batch characters are `"`, `%`, `!`, `^`, `&`, `|`, `<` and `>`, which the broker cannot pass to a manager that it runs through a batch file.

<a id="manager-winget"></a>
#### Winget

The broker builds no WinGet command, so a request is unreadable, when:

- its `Package.Id`, `OptIns.CustomInstallLocation.Path` or a custom parameter contains a batch character;
- its `Source.Name` is not a plain source name.

The reading accepts custom parameters and installer arguments.
On an Install or Update, the broker passes `--custom` when the `Mode` of `OptIns.InstallerArguments` is `Append`, or `--override` when it is `Replace`, then its `Arguments`, as two elements before the custom parameters, so that no custom parameter takes them as its value or moves them after a `--`.
On an Uninstall the broker passes neither, and the request still uses `InstallerArguments` ([§reading](#reading)).
A request whose `OptIns.InstallerArguments.Arguments` contains a batch character is unreadable whatever its operation.
An element that is exactly `--` ends the options: no later element names an option.
Before it, an element that starts with `--` names a long option by its text after `--` and before its first `=`, and a long option matches a name in the table ignoring ASCII case.
Any other element that starts with `-` and has another character names one short option for each character after `-` and before its first `=`, and a short option matches a name in the table with its exact characters, so `-ei` names `-e` and `-i`.
An option the table lists with a value takes one only when it is a long option or the only short option its element names.
It then takes the text after the element's first `=` when the element has one, and otherwise the next element, which is then only that value, even when it is `--` or starts with `-`.
An option that takes no value, because its element names other short options or no element follows, still has its effect: `--scope` or `--architecture` makes its property unknown.

| Custom parameter                  | Effect                                                                     |
| --------------------------------- | -------------------------------------------------------------------------- |
| `--location`, `-l` and its value  | would ask for `CustomInstallLocation`, so the request is unreadable        |
| `--scope` and its value           | sets the scope                                                             |
| `--architecture`, `-a` and its value | sets the architecture                                                   |
| `--interactive`, `-i`             | would ask for `Interactive`, so the request is unreadable                  |
| `--override`, `--custom` and its value | would ask for `InstallerArguments`, so the request is unreadable      |
| `--ignore-security-hash`, `--force` | would ask for `SkipHashCheck`, so the request is unreadable              |
| `--no-upgrade`                    | would ask for `NoUpgrade`, so the request is unreadable                    |
| `--uninstall-previous`            | would ask for `UninstallPrevious`, so the request is unreadable            |
| `--version`, `-v` and its value   | makes the version unknown and not concrete                                 |
| `--source`, `-s`, `--manifest`, `-m` and its value | changes the source, so the request is unreadable          |

`--scope` with `user` or `machine`, and `--architecture` with `x86`, `x64`, `arm64` or `neutral`, compared ignoring ASCII case, set the matching value; any other value makes the property unknown.
The scope is unknown when neither `Scope` nor `--scope` sets it.

<a id="manager-scoop"></a>
#### Scoop

The broker builds no Scoop command, so a request is unreadable, when:

- its `Elevation` is `Elevated`, as it is for every request whose `Scope` is `Machine` ([CONTRACT.wire.md §package-request](CONTRACT.wire.md#package-request));
- it has `OptIns.Interactive`, `OptIns.CustomInstallLocation`, `OptIns.NoUpgrade` or `OptIns.UninstallPrevious`;
- it is an Uninstall with `OptIns.SkipHashCheck`;
- it is an Update or an Uninstall with `Package.Architecture`, or an Install whose `Package.Architecture` is `Neutral`;
- its `Package.Id` contains `[` or `]`, or starts with `-`.

The reading accepts custom parameters.
A short cluster is an element whose first character is `-` and whose second character exists and is not `-`.
An option name ends at the element's first `=` and matches ignoring ASCII case, so `--ARCH=64bit` is `--arch` and `-S` is a short cluster containing `s`.
A custom parameter makes the request unreadable when:

- it is exactly `--`;
- it does not start with `-`, so an option takes a value only after `=` in its own element;
- it contains a code point that has the `White_Space` property in `PropList.txt` of [Unicode 16.0.0][unicode16-ucd];
- it names the option `--global`, `--all` or `--arch`;
- it starts with `-` but not `--`, and its text after that `-`, including any text after `=`, contains `g` or `a` ignoring ASCII case;
- it names the option `--skip-hash-check`, or is a short cluster whose option names before its first `=` include `s`, because it would ask for `SkipHashCheck`.

So `--globalx`, `-`, `-q` and `-q=s` make no request unreadable, while `-qa`, `-G`, `-q=abcg` and `--ARCH=x` do.

A Scoop source is a bucket, named by the bucket's name, or a manifest URL or local path.
A source name or identifier is a manifest URL or local path when it contains `...`, `:\` or `\\`, or when, after its leading `/` characters are removed, it starts with `http://`, `https://`, `ftp://` or `ftps://` ignoring ASCII case.
A manifest URL or local path is a known source like a bucket, so a `Source` selector can name one and `AnySource` covers them all.
A request whose `Source.Name` or `Package.Id` is a manifest URL or local path is unreadable unless the two are equal with exact characters, because with a manifest source the broker passes `Package.Id` alone, and an identifier that is a manifest would fetch from outside the named source.
A local or UNC path, with `:\` or `\\`, is never readable, because `Package.Id` cannot contain `\` and so cannot equal it.
Any other source name is a bucket name, and a request through it is unreadable unless the bucket name and `Package.Id` are each one or more ASCII letters, digits, `-`, `_` and `.`, and `Package.Id` does not end with `.json` ignoring ASCII case, so that `<bucket>/<id>` names exactly one app in that bucket: no version after `@`, no path inside the bucket and no local manifest file.
Such a request is also unreadable when its bucket name or `Package.Id` is made only of `.` characters, because that would name a directory rather than a bucket or an app.
The broker cannot pin a version through Scoop.

<a id="manager-vcpkg"></a>
#### Vcpkg

The source name is a triplet.
A port spec is a port, optionally followed by `[`, a `,`-separated list of one or more features and `]`, optionally followed by `:` and a triplet.
A port and each feature are one or more ASCII lowercase letters, digits and `-`, not starting with `-`, and a triplet is one or more ASCII letters, digits, `-`, `_` and `.`.
The broker builds no Vcpkg command, so a request is unreadable, when:

- its `Elevation` is `Elevated`;
- it has `OptIns.Interactive`, `OptIns.SkipHashCheck`, `OptIns.CustomInstallLocation`, `OptIns.NoUpgrade` or `OptIns.UninstallPrevious`;
- it has `Package.Architecture`;
- its `Source.Name` is not a triplet;
- its `Package.Id` is not a port spec, or has a triplet that differs from `Source.Name` in any character.

An identifier names its port, so `port[features]:triplet` names `port`, and no identifier selects the version.
The architecture is `X86`, `X64` or `Arm64` when the source triplet starts with `x86-`, `x64-` or `arm64-` ignoring ASCII case, and unknown otherwise.
The broker cannot pin a version through Vcpkg.

<a id="manager-npm"></a>
#### Npm

The only source name is `npm`, and a request with another source name is unreadable.
Package names keep their case in canonical form.
A registry name is `name` or `@scope/name`, where `scope` and `name` are one or more ASCII letters, digits, `-`, `.`, `_` and `~` and do not start with `-`, `.` or `_`; a registry name is at most 214 bytes and does not end with `.tgz`, `.tar` or `.tar.gz` ignoring ASCII case.
An identifier that is a registry name names itself.
An alias `alias:target`, split at its first `:`, names `alias` and `target` when both are registry names, and so does `alias:target@suffix`, where that `@` is the last `@` after the `:` and is not the first character after it; the broker drops the suffix, so it selects no version.
The broker builds no Npm command, so a request is unreadable, when:

- its `Elevation` is `Elevated`;
- it has `OptIns.Interactive`, `OptIns.SkipHashCheck`, `OptIns.CustomInstallLocation`, `OptIns.NoUpgrade` or `OptIns.UninstallPrevious`;
- its `Package.Architecture` is `X86`, `X64` or `Arm64`;
- its `Package.Id` is neither a registry name nor an alias, so that no identifier fetches from outside the named source, or it contains `%`;
- it is an Uninstall whose `Package.Version` contains U+0000, U+000A or U+000D.

A `Package.Version` may also contain `~` and `*`.
The architecture is `Neutral` when `Package.Architecture` is absent.

<a id="manager-bun"></a>
#### Bun

The reading is that of [§manager-npm](#manager-npm), except for identifiers, sources and versions.
An identifier that is a registry name names itself, keeping its case in canonical form, and any other identifier makes the request unreadable.
The only source name is `Bun`, and a request with another source name is unreadable.
A `Package.Version` that is not a version as [Semantic Versioning 2.0.0][semver] defines it makes the request unreadable, whatever its operation.

<a id="manager-cargo"></a>
#### Cargo

The only source name is `crates.io`, and a request with another source name is unreadable.
The broker builds no Cargo command, so a request is unreadable, when:

- its `Elevation` is `Elevated`;
- it has `OptIns.Interactive`, `OptIns.SkipHashCheck`, `OptIns.NoUpgrade`, `OptIns.UninstallPrevious`, `OptIns.PrePostCommands` or `OptIns.KillBeforeOperation`;
- its `Package.Architecture` is `X86`, `X64` or `Arm64`;
- its `Package.Id` is longer than 64 bytes, starts with `-`, or contains a character other than ASCII letters, digits, `-` and `_`;
- its `OptIns.CustomInstallLocation.Path` contains a batch character;
- it is an Uninstall with `Package.Version`.

A `Package.Version` may also contain `=`, `~` and `*`.
The canonical form maps ASCII letters to lowercase and `_` to `-`.
The architecture is `Neutral` when `Package.Architecture` is absent.

<a id="manager-pip"></a>
#### Pip

The only source name is `pip`, and a request with another source name is unreadable.
The broker builds no Pip command, so a request is unreadable, when:

- its `Elevation` is `Elevated`;
- it has `OptIns.Interactive`, `OptIns.SkipHashCheck`, `OptIns.CustomInstallLocation`, `OptIns.NoUpgrade` or `OptIns.UninstallPrevious`;
- its `Package.Architecture` is `X86`, `X64` or `Arm64`;
- its `Package.Id` starts or ends with a character other than an ASCII letter or digit, or contains a character other than ASCII letters, digits, `.`, `_` and `-`;
- its `Package.Version` contains a character other than ASCII letters, digits, `.`, `!`, `+`, `-` and `_`;
- it is an Uninstall with `Package.Version`.

That version rule replaces the default one of [§managers](#managers), so a Pip version may start with `-`.

The canonical form maps ASCII letters to lowercase and each run of `-`, `_` and `.` to one `-`, as [PEP 503][pep503] normalizes names.
The architecture is `Neutral` when `Package.Architecture` is absent, and the scope of an Uninstall is unknown.

<a id="manager-dotnet"></a>
#### Dotnet

The only source name of an Install or Update is `nuget.org`, compared ignoring ASCII case, and an Install or Update with another source name is unreadable.
The source of an Uninstall is unknown, because the broker does not pass it, so a `Source` selector is unknown for it and an `AnySource` selector matches it ([§source-selector](#source-selector)).
The broker builds no Dotnet command, so a request is unreadable, when:

- its `Scope` is `Machine`;
- it has `OptIns.Interactive`, `OptIns.SkipHashCheck`, `OptIns.NoUpgrade` or `OptIns.UninstallPrevious`;
- it is an Uninstall with `Package.Architecture`.

A `Package.Version` may also contain `[`, `]`, `(`, `)`, `,` and `*`.
A requested architecture of `Neutral` makes the architecture unknown.

<a id="manager-chocolatey"></a>
#### Chocolatey

The broker builds no Chocolatey command, so a request is unreadable, when:

- its `Scope` is `User`;
- its `Package.Architecture` is `Arm64` or `Neutral`, or it is an Uninstall with `Package.Architecture`;
- it has `OptIns.CustomInstallLocation`, `OptIns.NoUpgrade` or `OptIns.UninstallPrevious`;
- it is an Uninstall with `OptIns.SkipHashCheck`;
- its `Package.Id` is longer than 100 bytes, starts with `-` or `.`, contains a character other than ASCII letters, digits, `.`, `-` and `_`, ends with `.config` or `.nupkg` ignoring ASCII case, or is `all` ignoring ASCII case;
- it is an Install or Update whose `Source.Name` is not a plain source name.

The source of an Uninstall is unknown, because the broker does not pass it, so a `Source` selector is unknown for it and an `AnySource` selector matches it ([§source-selector](#source-selector)).
The scope is `Machine` when `Scope` is absent, and a requested architecture of `X64` makes the architecture unknown.

<a id="manager-powershell"></a>
#### PowerShell and PowerShell7

A source name that is not a plain source name, or a `Package.Id` that contains `[` or `]`, makes the request unreadable, because PowerShell reads other characters as wildcards or matches them by its own rules.
Under `PowerShell7`, a `Package.Version` may also contain `[`, `]`, `(`, `)`, `,`, `*` and spaces.
The architecture is always `Neutral`, and the scope of an Uninstall is unknown.

<a id="install-location"></a>
### Install location check

A request fails the install location check when it uses `CustomInstallLocation` and its install location, `OptIns.CustomInstallLocation.Path`, is not a plain local path; no other request fails it.
A plain local path is an ASCII letter followed by `:` and a separator, then any sequence of separators and segments, where a separator is `\` or `/` and a segment is a maximal run of other characters.
A segment is not `.` or `..`, does not start with a space (U+0020), does not end with `.` or a space, and contains no control character (Unicode general category Cc, U+0000 to U+001F and U+007F to U+009F), and none of `: * ? " < > |`.
The normalized location has an uppercase drive letter and uses `\` as its only separator, with repeated separators collapsed and a trailing separator dropped unless it follows the drive's `:`, so the normalized drive root is `C:\`; grant and forbid patterns match it.
The check reads only the path's text and does not ask the operating system what a drive letter refers to, so both implementations give the same result on any machine.
A network path, a device path or a relative path would give an elevated installer a target that no rule can describe, so the check applies whatever the policy says.

<a id="missing-version"></a>
### Missing version

The version is `Package.Version` when it is concrete, and unknown otherwise.
A version is concrete when it is present, neither a custom parameter nor the identifier ([§package-identity](#package-identity)) selects the version, and the manager does not resolve it as a range.
`Npm`, `Bun`, `Cargo` and `Dotnet` resolve a version as a range unless it is a version ([§version-scheme](#version-scheme)) with at least three release components, and `PowerShell7` resolves one as a range unless it is a version, because its `-Version` accepts NuGet version ranges.
A manager whose reading cannot pin a version has an unknown version.
An Install or Update request through a manager that can pin a version is missing its version when its version is not concrete, and is denied whatever the policy says, so that rule matching always has the version.
An Uninstall request is never missing its version, because no version condition applies to it ([§version-condition](#version-condition)).

<a id="evaluation"></a>
## Evaluation

The broker evaluates each request once, before running it, against the one policy it holds, at its current time, which is the evaluation time.
A decision depends only on the policy, the request and the evaluation time.
The broker accepts every valid document, refuses every invalid document whose `PolicyFormatVersion` is `"2.0.0"` and reports its errors, and runs no request while it holds no accepted policy.
An allowed request may still run nothing, such as when the broker will not run its `Pre` or `Post` command ([CONTRACT.wire.md §not-runnable](CONTRACT.wire.md#not-runnable)) or cannot launch the manager process ([CONTRACT.wire.md §operations](CONTRACT.wire.md#operations)); that refusal comes after the decision and is not one, so `Allow` does not promise that a request runs.

<a id="applicability"></a>
### Matching and applicability

A condition, a selection and an `Except` list each match, do not match, or are unknown.
A condition is unknown when the property it tests is unknown, as [§source-selector](#source-selector), [§version-condition](#version-condition) and [§package-identity](#package-identity) refine.
A list condition matches when one of its entries matches, otherwise is unknown when one of its entries is unknown, and otherwise does not match.
A selection does not match when one of its conditions does not match, otherwise is unknown when one of its conditions is unknown, and otherwise matches.
An `Except` list matches when one of its entries matches, otherwise is unknown when one of its entries is unknown, and otherwise does not match; an absent `Except` does not match.
Unknown resolves toward denial, which keeps every rule fail-closed:

- an enabled Allow rule is applicable when its `Select` matches and its `Except` neither matches nor is unknown;
- an enabled Deny rule is applicable when its `Select` matches or is unknown and its `Except` does not match, an unknown `Except` counting as not matching.

Whether a rule is applicable never depends on the opt-ins a request uses, so an Allow rule cannot require an opt-in.
An exception narrows only the rule, grant or forbid it is written in.

<a id="opt-in-evaluation"></a>
### Granted and forbidden opt-ins

An opt-in the request uses is granted when an applicable Allow rule grants it.
`CustomParameters` is granted when each custom parameter is covered by a `CustomParameters` grant of an applicable Allow rule, so grants from several rules add up; `CustomInstallLocation` is granted when its location is covered in the same way.
A Deny rule denies a request for an opt-in the request uses when the rule is applicable and forbids that opt-in; for `CustomParameters`, when also a custom parameter is covered by the forbid, and for `CustomInstallLocation`, when the location is covered by the forbid.
A Deny rule denies a request when it is applicable and has no `Forbids`, or when it denies the request for an opt-in.

<a id="precedence"></a>
### Outcomes and precedence

Each check below denies a request when it holds, except `AllowRules`, which allows it.
When several checks hold, the one higher in the table produces the decision; this fixes which outcome wins, not the order in which an implementation runs its checks.

| Check               | Holds when                                                                  | Audit mode lets it through |
| ------------------- | --------------------------------------------------------------------------- | -------------------------- |
| `UnreadableRequest` | the request is unreadable ([§reading](#reading))                            | no                         |
| `ValidityWindow`    | the evaluation time is outside the validity window                          | no                         |
| `InstallLocation`   | the request fails the install location check ([§install-location](#install-location)) | no              |
| `MissingVersion`    | the request is missing its version ([§missing-version](#missing-version))   | no                         |
| `UntrustedSource`   | the request is an Install or Update whose source is not trusted             | yes                        |
| `DenyRule`          | a Deny rule denies the request                                              | yes                        |
| `UngrantedOptIn`    | an Allow rule is applicable and an opt-in the request uses is not granted   | yes                        |
| `DefaultDeny`       | no Allow rule is applicable                                                 | yes                        |
| `AllowRules`        | no check above holds                                                        |                            |

<a id="audit-mode"></a>
### Audit mode

`Enforcement` is an object with one optional key, `AuditMode`, a boolean; an absent `Enforcement` or `AuditMode` means `false`.
While `AuditMode` is `true`, a request whose decision comes from a check that audit mode lets through runs, and its decision is audited.
An audited decision names the same check, rules and opt-ins as it would without audit mode.
The other checks deny in audit mode too: outside the validity window the policy, including its `AuditMode`, is not in force, and the unreadable request, install location and missing version checks are not policy content.
Audit mode lets a new list of trusted sources or a new set of rules be tried out without blocking users.

<a id="decision"></a>
### Decision

A decision has these values, which each implementation may represent in its language's style.

| Value   | Content                                                              |
| ------- | -------------------------------------------------------------------- |
| Outcome | `Allow` when the broker runs the request, `Deny` otherwise           |
| Check   | the check from [§precedence](#precedence) that produced the decision |
| Rules   | rule ids, in the order of `Rules`, each with its `Reason` when it has one |
| Opt-ins | opt-ins, in the order of [§opt-ins](#opt-ins)                        |
| Audited | `true` when audit mode let a denied request run, `false` otherwise   |

The outcome is `Allow` when the check is `AllowRules` or the decision is audited, and `Deny` otherwise.
A decision that no rule produced names its check and no rule.
The rules and opt-ins a decision names depend on its check:

| Check            | Rules                                     | Opt-ins                                                         |
| ---------------- | ----------------------------------------- | --------------------------------------------------------------- |
| `DenyRule`       | every Deny rule that denies the request   | every opt-in for which one of those rules denies the request    |
| `UngrantedOptIn` | every applicable Allow rule               | every opt-in the request uses that is not granted               |
| `DefaultDeny`    | none                                      | every opt-in the request uses                                   |
| `AllowRules`     | every applicable Allow rule               | none                                                            |
| any other check  | none                                      | none                                                            |

<a id="properties"></a>
## Properties

These properties follow from the rules above, so an implementation that breaks one breaks a rule.

<a id="monotonicity"></a>
### Monotonicity

When a valid policy gains one Allow rule and stays valid, every request whose outcome was `Allow` at an evaluation time is still `Allow` at that time.
When a valid policy gains one Deny rule and stays valid, every request whose outcome was `Deny` at an evaluation time is still `Deny` at that time.
Both hold because a Deny rule wins over every Allow rule, and grants from applicable Allow rules add up rather than override one another.

<a id="order-independence"></a>
### Order independence

Reordering the entries of any list in a policy changes no decision, except the order in which a decision names rules.

<a id="agreement"></a>
### Agreement

For every valid policy, request and evaluation time, the Rust and C# implementations produce equal decisions: the same outcome, check, rule ids, reasons, opt-ins and audited value.
For every document, both find it valid or both find it invalid, and for a document with exactly one defect both report the same error at the same pointer.
Because validity agrees, an editor or a central authority can check a policy before it reaches a broker that would refuse it.

<a id="risk-guidance"></a>
## Risk guidance

This section is guidance, not a rule: the shared C# implementation uses it so that editors warn alike, and a policy that ignores it is still valid.
Each row that a construct matches gives one warning at the row's level.
For a row about a grant or an Allow rule, the level applies when the rule can select `Elevated` requests, and is one level lower, never below Low, when its `Select` has `Elevation: NonElevated`.
A pattern `*` means a pattern made only of `*` characters.

| Construct                                                   | Level  | Why                                                                                                   |
| ----------------------------------------------------------- | ------ | ----------------------------------------------------------------------------------------------------- |
| a grant of `PrePostCommands`                                | High   | the user chooses commands that run with the request's rights                                          |
| a grant of `InstallerArguments`                             | High   | installer arguments can change what an installer runs, where it reads files from and where it writes them |
| a grant of `SkipHashCheck`                                  | High   | the installer runs without the manager's integrity check                                              |
| a grant of `Interactive`                                    | High   | the dialogs of an elevated installer can start other programs with administrator rights              |
| a `CustomParameters` grant with the pattern `*`             | High   | every custom parameter passes                                                                         |
| a `CustomParameters` grant with other patterns              | Medium | a pattern can match more arguments than its author meant                                              |
| a grant of `CustomInstallLocation`                          | Medium | an elevated install can place files where other programs load code from                              |
| a grant of `KillBeforeOperation`                            | Medium | the user can stop any process, including security software                                            |
| a grant of `UninstallPrevious` or `NoUpgrade`               | Low    | the effect is limited to the package the request names                                                |
| an `AnySource` trusted source for `Scoop`                   | High   | a manifest URL lets a user install from any address                                                   |
| an `AnySource` trusted source for another manager           | Medium | every source configured on the machine is trusted, including ones added later                         |
| an Allow rule whose `Select` is `{}`                        | High   | it allows every package from every trusted source, plus every opt-in it grants                        |
| an Allow rule without `Packages`                            | Medium | it allows packages nobody reviewed                                                                    |
| `Enforcement.AuditMode` set to `true`                       | High   | only the checks that audit mode does not let through deny requests                                   |
| no `Metadata.ValidUntil`                                    | Low    | the policy never expires                                                                              |

[pep503]: https://peps.python.org/pep-0503/#normalized-names
[rfc3339]: https://www.rfc-editor.org/rfc/rfc3339
[rfc6901]: https://www.rfc-editor.org/rfc/rfc6901
[rfc8259]: https://www.rfc-editor.org/rfc/rfc8259
[semver]: https://semver.org/spec/v2.0.0.html
[unicode16]: https://www.unicode.org/Public/16.0.0/ucd/UnicodeData.txt
[unicode16-ucd]: https://www.unicode.org/Public/16.0.0/ucd/
