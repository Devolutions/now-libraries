# Policy conformance suite

Language-neutral cases for the 2.x policy format defined in `policies/CONTRACT.policy.md`.
Every implementation of the contract runs the same files, so implementations that pass agree on every case here.
Every expected value comes from the contract or a published source that the case cites, never from an implementation.

## Layout

- `documents/*.json`: validation cases; a document is either valid or has the listed errors.
- `evaluation/*.json`: decision cases; a valid policy, a request and an evaluation time give one decision.
- `COVERAGE.*.md`: which cases check each contract section, and what is deliberately left unchecked, one file per part of the suite.

A runner loads every `*.json` file of both directories, so new files need no registration.

## Common case fields

Every case is an object in the `cases` array of its file.

| Key      | Required | Value                                                                                         |
| -------- | -------- | --------------------------------------------------------------------------------------------- |
| `name`   | yes      | a kebab-case name, unique across the suite                                                     |
| `oracle` | yes      | where the expected value comes from, as `contract policies/CONTRACT.policy.md §<anchor>`, or another source kind with its reference |
| `note`   | no       | how the expected value was derived, when the cited section alone does not make it obvious     |

## Document cases

A document case has exactly one of these keys for its input:

| Key        | Value                                                                                          |
| ---------- | ---------------------------------------------------------------------------------------------- |
| `document` | any JSON value; the input is the exact bytes of that value as written in the case file         |
| `text`     | a string; the input is that string encoded in UTF-8                                            |
| `base64`   | a string; the input is the bytes it encodes, for inputs that are not valid UTF-8 or carry a byte order mark |

`document` keeps the case file's own spelling, so numbers, escapes, key order and repeated keys reach the reader unchanged.
A runner reads it as raw text, such as `serde_json::value::RawValue` in Rust or `JsonElement.GetRawText()` in C#, and never re-serializes it.

An optional `padTo` is a byte count: the runner appends ASCII spaces (U+0020) after the input until it is that long.
It builds documents near the 4 MiB limit without storing them.

`expect` is one of:

| Value                                   | Meaning                                                                                                   |
| --------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `{ "valid": true }`                     | the reader accepts the document and reports no error                                                       |
| `{ "errors": [ <error>, ... ] }`        | the reader reports exactly these errors, in any order                                                      |
| `{ "someOf": [ <error>, ... ] }`        | the reader reports a non-empty set of errors, each of them in this list, for documents with several defects |

An `<error>` is `{ "code": "<error code>", "pointer": "<JSON Pointer>" }`, with the codes of the contract's §errors and an RFC 6901 pointer, `""` for the whole document.

## Evaluation cases

An evaluation file may set `policy` and `time` at its top level, and a case may set either to replace them for that case.

| Key       | Value                                                                                               |
| --------- | --------------------------------------------------------------------------------------------------- |
| `policy`  | a valid policy document as a JSON value; the runner first checks that it is valid and fails the case otherwise |
| `time`    | the evaluation time as a contract timestamp (§timestamps)                                           |
| `request` | the request, below                                                                                  |
| `expect`  | the decision, below                                                                                 |

### Request

A request is a package request as `policies/CONTRACT.wire.md` §package-request defines it, without `RequestId`:

```json
{
  "Operation": "Install",
  "Manager": "Winget",
  "Source": { "Name": "winget" },
  "Package": { "Id": "Contoso.Tool", "Version": "1.2.3", "Architecture": "X64" },
  "Scope": "Machine",
  "Elevation": "Elevated",
  "OptIns": {
    "Interactive": {},
    "SkipHashCheck": {},
    "CustomInstallLocation": { "Path": "C:\\Apps" },
    "CustomParameters": { "Values": ["--locale", "en-US"] },
    "InstallerArguments": { "Arguments": "/quiet", "Mode": "Append" },
    "PrePostCommands": { "Pre": "cmd /c echo", "Post": "cmd /c echo" },
    "KillBeforeOperation": { "Processes": ["tool.exe"] },
    "UninstallPrevious": {},
    "NoUpgrade": {}
  }
}
```

A request uses an opt-in exactly when its key is present in `OptIns`; an absent optional member is absent from the request.

Every request in the suite is well-formed under that section and its §opt-ins.
A runner rejects a case whose request is not well-formed instead of evaluating it: a request with an unknown member, a missing required member, a value outside its domain, or `Scope` `Machine` with `Elevation` `NonElevated`.
A well-formed request can still be unreadable under the policy contract's §reading, and its expected decision then says so.

### Decision

| Key       | Value                                                                                          |
| --------- | ---------------------------------------------------------------------------------------------- |
| `outcome` | `Allow` or `Deny`                                                                              |
| `check`   | the check name from the contract's §precedence                                                 |
| `rules`   | a list of `{ "id": "<rule id>" }` or `{ "id": "<rule id>", "reason": "<Reason>" }`, in the order the decision names them |
| `optIns`  | a list of opt-in names, in the order the decision names them                                   |
| `audited` | a boolean                                                                                       |

A runner compares all five values exactly, including list order.
