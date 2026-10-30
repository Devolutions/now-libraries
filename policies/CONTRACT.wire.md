# Package broker wire contract

This contract defines the 2.0 encoding of the requests and responses between the package broker and its clients on the broker's named pipe, and the event channel of an operation.
The parties are the package broker inside Devolutions Agent and its clients, such as UniGetUI and RDM.
`policies/INTENT.wire.md` explains the design choices; this contract states the rules.
`CONTRACT.policy.md` defines the policy and how it decides a package request; this contract defines the shape of that request and how it travels.

<a id="definitions"></a>
## Definitions

<a id="def-broker"></a>
### Broker

The broker is the package broker inside Devolutions Agent, which serves requests on its pipe ([§transport](#transport)).

<a id="def-client"></a>
### Client

A client is a program that sends requests to the broker, such as UniGetUI or RDM.

<a id="def-owner"></a>
### Owner

The owner of a request is the client process that sent it, identified by the security identifier (SID) of the Windows user of its token and the full path of the executable file it runs ([§authentication](#authentication)).
Two owners are the same when their SIDs are equal and their paths are equal ignoring case as [CONTRACT.policy.md §text-comparison](CONTRACT.policy.md#text-comparison) defines.

<a id="def-package-request"></a>
### Package request

A package request asks the broker to install, update or uninstall one package through one manager ([§package-request](#package-request)).

<a id="def-opt-in"></a>
### Opt-in

An opt-in is something a package request asks for beyond a plain request, as [CONTRACT.policy.md §def-opt-in](CONTRACT.policy.md#def-opt-in) defines.

<a id="def-decision"></a>
### Decision

A decision is the result of evaluating a package request against the broker's policy ([CONTRACT.policy.md §decision](CONTRACT.policy.md#decision)), written as [§decision](#decision) states.
A decision's `Outcome` is `Allow` or `Deny`, and a `Deny` decision, an unreadable request's included, comes in an evaluation or execution response ([§evaluate](#evaluate), [§execute](#execute)), never in an error response.
In this contract, the outcome of a request is the response it gets, which is not a decision's `Outcome`.

<a id="def-error"></a>
### Error

An error is a result that is not a decision, as [CONTRACT.policy.md §def-error](CONTRACT.policy.md#def-error) defines; in this contract, an error response ([§errors](#errors)) or a policy error ([§policy-errors](#policy-errors)).
Only an error response whose `Code` is `NotRunnable` carries a decision ([§not-runnable](#not-runnable)), and [§errors](#errors) states which codes leave the outcome of the request unknown.

<a id="def-accepted-policy"></a>
### Accepted policy

The accepted policy is the one policy the broker holds and evaluates package requests with ([CONTRACT.policy.md §evaluation](CONTRACT.policy.md#evaluation)).

<a id="def-operation"></a>
### Operation

An operation is one run of a package request that the broker started for an execution request, identified by its `OperationId` ([§operations](#operations)).

<a id="def-event-channel"></a>
### Event channel

An event channel is a one-way pipe from the broker to the owner of one operation, carrying frames about that operation ([§event-channel](#event-channel)).

<a id="general"></a>
## General rules

These rules apply throughout this contract and are not repeated.

<a id="values"></a>
### Values

Lengths of strings count bytes in UTF-8.
An identifier is a string of 1 to 128 ASCII letters, digits, `.`, `_`, `:` and `-` whose first character is an ASCII letter or digit.
A timestamp is a timestamp of [CONTRACT.policy.md §timestamps](CONTRACT.policy.md#timestamps), and the broker writes every timestamp with the offset `Z`.
A value from a closed set this contract defines or cites, such as `Install` or `Elevated`, matches only with its exact characters.

<a id="json"></a>
### JSON bodies

A request body is one JSON value encoded in UTF-8.
It is malformed when it is a malformed document under [CONTRACT.policy.md §json](CONTRACT.policy.md#json), when it starts with a byte order mark, which [RFC 8259][rfc8259] forbids a sender to add, or when it has an object that repeats a key after JSON escapes are decoded.
Keys, `null`, key order and whitespace are read as [CONTRACT.policy.md §json](CONTRACT.policy.md#json) reads them in a document, so an optional member is absent rather than `null`; the order of entries in a list, which that section gives no meaning in a policy, is part of a request's value ([§equal-requests](#equal-requests)).
In every request object this contract defines, a member its table does not list, an absent required member, a value of the wrong JSON type, and a value outside the domain its table gives each make the request `BadRequest` ([§errors](#errors)), so that no part of a request goes unread.

A response body is one JSON object, encoded in UTF-8 without a byte order mark.
The broker writes exactly the members that the response's table lists and whose presence condition holds.
A client accepts any key order and whitespace, and ignores a response member this contract does not define.

<a id="transport"></a>
## Transport

The broker listens on the local named pipe `\\.\pipe\Devolutions.Now.PackageBroker.v1`.
Each connection carries one HTTP/1.1 request ([RFC 9112][rfc9112]) and the broker's response to it, and the broker closes the connection after sending the response.
A client sends one request per connection.
A request body is framed by `Content-Length` or by the chunked transfer coding.
This contract defines the broker's response to every request whose path starts with `/v2/`; a request with another path is outside this contract.
The request target is the path, optionally followed by a query, which the broker ignores.
A client sends a header section of at most 8192 bytes.
A larger header section, a message that RFC 9112 does not allow, a request with both `Content-Length` and `Transfer-Encoding`, and a request with an `Expect` header are outside this contract.
A request to an endpoint with a request body ([§endpoints](#endpoints)) is `UnsupportedMediaType` when its `Content-Type` is absent or does not have the media type `application/json`, compared ignoring ASCII case; media type parameters such as `charset` are ignored, and the body is UTF-8 whatever they say.
The broker ignores `Content-Type` on a request to an endpoint without a request body.
Every response defined here has the media type `application/json` and the HTTP status that [§errors](#errors) gives its error code, or 200 when it is not an error response.
The broker may close a connection without a response when the request header is not complete 5 seconds after the connection opened, or when the exchange is not complete 30 seconds after it opened.
While the broker serves other connections, it may have no pipe instance listening, so that opening the pipe fails with `ERROR_PIPE_BUSY`; the client may then wait for an instance with `WaitNamedPipe`.

<a id="endpoints"></a>
### Endpoints

This table is the closed set of endpoints.
A method and path that match no row are `UnknownEndpoint`.
A body larger than the endpoint's limit, counting every byte after decoding the transfer coding, is `RequestTooLarge`, so any non-empty body sent to an endpoint without a request body is `RequestTooLarge`.

| Method | Path                                  | Request body                             | Response                                  | Body limit (bytes) | Client must be                           |
| ------ | ------------------------------------- | ---------------------------------------- | ----------------------------------------- | ------------------ | ---------------------------------------- |
| `GET`  | `/v2/health`                          | none                                     | [§health](#health)                        | 0                  | any                                      |
| `GET`  | `/v2/capabilities`                    | none                                     | [§capabilities](#capabilities)            | 0                  | authenticated                            |
| `POST` | `/v2/package-requests/evaluate`       | [§evaluate](#evaluate)                   | [§evaluate](#evaluate)                    | 262144             | authenticated                            |
| `POST` | `/v2/package-requests/execute`        | [§execute](#execute)                     | [§execute](#execute)                      | 262144             | authenticated                            |
| `GET`  | `/v2/operations/{OperationId}`        | none                                     | [§status](#status)                        | 0                  | authenticated                            |
| `POST` | `/v2/operations/{OperationId}/cancel` | none                                     | [§cancel](#cancel)                        | 0                  | authenticated                            |
| `GET`  | `/v2/policy/management`               | none                                     | [§management](#management)                | 0                  | authenticated                            |
| `POST` | `/v2/policy/validate`                 | [§validation](#validation)               | [§validation](#validation)                | 33554432           | authenticated                            |
| `POST` | `/v2/policy`                          | [§replacement](#replacement)             | [§replacement](#replacement)              | 33554432           | authenticated, an elevated administrator |

A path matches a row only when it has the same characters, with no decoding or normalization, and `{OperationId}` matches one or more characters other than `/`.
Those characters are the operation id the request names, so a segment that is not an identifier, such as one holding a percent-encoding, names no operation.
A request whose target is in absolute form, and a request with the method `HEAD`, are outside this contract.

<a id="authentication"></a>
### Authentication

The broker identifies the client by the process connected to the pipe, as its owner ([§def-owner](#def-owner)) states.
No request member names the client, because the broker reads its identity from the pipe.
A request to an endpoint whose client must be authenticated is `Unauthenticated` when the broker does not authenticate the client process.
Which processes the broker authenticates is set by its deployment, such as requiring a Devolutions Authenticode signature on the executable file, and is outside this contract.
A request to `POST /v2/policy` is `AdministratorRequired` when the broker authenticates the client process but its token is not elevated or does not have the Administrators group SID enabled.

<a id="errors"></a>
## Errors

An error response has these members.

| Member       | Present                                                                                         | Value                                                                    |
| ------------ | ----------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| `Code`       | always                                                                                          | an error code from the table below                                       |
| `Message`    | always                                                                                          | a string of at most 2048 bytes, which carries no meaning a client may depend on |
| `Decision`   | when `Code` is `NotRunnable`                                                                    | a decision ([§decision](#decision))                                      |
| `Management` | when `Code` is `StaleStoreToken`, `PolicyNotWritable`, `RevisionNotHigher` or `ActivationFailed` | the policy management state ([§management-state](#management-state)) after the request |
| `Errors`     | when `Code` is `InvalidPolicy`                                                                  | a non-empty list of policy errors ([§policy-errors](#policy-errors))     |

This table is the closed set of error codes.

| Code                    | HTTP status | The request                                                                                        |
| ----------------------- | ----------- | -------------------------------------------------------------------------------------------------- |
| `UnknownEndpoint`       | 404         | has a method and path that match no endpoint ([§endpoints](#endpoints))                            |
| `UnsupportedMediaType`  | 415         | has no `application/json` content type ([§transport](#transport))                                  |
| `Unauthenticated`       | 401         | comes from a client process the broker does not authenticate ([§authentication](#authentication)) |
| `AdministratorRequired` | 403         | replaces the policy from a process that is not an elevated administrator                           |
| `RequestTooLarge`       | 413         | has a body larger than its endpoint's limit ([§endpoints](#endpoints))                             |
| `BadRequest`            | 400         | has a malformed body ([§json](#json)), or a request object this contract does not allow            |
| `PolicyUnavailable`     | 503         | is a package request while the broker holds no accepted policy                                     |
| `RequestIdConflict`     | 409         | reuses a `RequestId` for a different package request ([§execute](#execute))                        |
| `NotRunnable`           | 422         | is allowed, but the broker will not run it ([§not-runnable](#not-runnable))                        |
| `OperationNotFound`     | 404         | names an operation the broker does not hold for its owner ([§operations](#operations))             |
| `StaleStoreToken`       | 409         | replaces the policy with a store token that is not current ([§replacement](#replacement))          |
| `PolicyNotWritable`     | 409         | replaces the policy while the broker cannot write it ([§replacement](#replacement))                |
| `InvalidPolicy`         | 422         | replaces the policy with a document that is not valid ([§replacement](#replacement))               |
| `RevisionNotHigher`     | 409         | replaces the policy with one of the same `Id` and no higher `Revision` ([§replacement](#replacement)) |
| `PersistenceFailed`     | 500         | replaces the policy, and the broker failed to write it                                             |
| `ActivationFailed`      | 500         | replaces the policy, and the broker wrote it but failed to load it                                 |
| `InternalError`         | 500         | met a broker failure that no other code names                                                      |

When several outcomes apply to a request, the earliest in this list is the response; this fixes which outcome wins, not the order in which the broker runs its checks:

1. `UnknownEndpoint`;
2. `UnsupportedMediaType`;
3. `Unauthenticated`;
4. `AdministratorRequired`;
5. `RequestTooLarge`;
6. `BadRequest`;
7. the outcomes of the endpoint's own section, in the order that section gives.

For outcomes 1 to 4 the broker sends the response without waiting for any byte of the body, so an unauthenticated client cannot make it read one.
A client that fails to send its body still reads the response.
`InternalError` may replace an outcome of item 7.
A response is one this contract does not allow when its body is not a JSON object, lacks a member its table makes present, has a member whose value is outside its domain, or has an HTTP status other than the one its `Code` gives, or other than 200 for a response that is not an error response.
A client that receives `InternalError`, no response, or a response this contract does not allow treats the outcome of its request as unknown; [§execute](#execute) and [§replacement](#replacement) state how a client recovers from an unknown outcome.

<a id="health"></a>
## Health

The response has one member, `Status`: `Ready` when the broker holds an accepted policy, and `Paused` otherwise.
It names no policy, because any client may send it; an authenticated client learns the accepted policy from [§management](#management).

<a id="capabilities"></a>
## Capabilities

The response has these members.

| Member                    | Value                                                           |
| ------------------------- | --------------------------------------------------------------- |
| `ServerVersion`           | a string of 1 to 128 bytes naming the broker's version          |
| `OperationTimeoutSeconds` | an integer from 1 to 2147483647 ([§operations](#operations))   |
| `Managers`                | a list of manager capabilities, each naming a different manager |

A manager capability has these members.

| Member          | Value                                                                                         |
| --------------- | --------------------------------------------------------------------------------------------- |
| `Manager`       | a manager identifier ([CONTRACT.policy.md §managers](CONTRACT.policy.md#managers))            |
| `Operations`    | a non-empty list of operations ([CONTRACT.policy.md §request-properties](CONTRACT.policy.md#request-properties)) |
| `Scopes`        | a list of scopes                                                                              |
| `Architectures` | a list of architectures                                                                       |
| `OptIns`        | a list of `OptIns` keys ([§opt-ins](#opt-ins))                                               |

Which managers the list holds and what each entry contains are implementation-defined and may depend on the client's user.
A capability is advisory: whether a package request runs is decided only by [§evaluate](#evaluate) and [§execute](#execute).

<a id="package-request"></a>
## Package request

A package request is an object with these members.
It is the request that [CONTRACT.policy.md](CONTRACT.policy.md) evaluates, and an execution request also has `RequestId` ([§execute](#execute)).

| Member      | Required | Value                                                                                                 |
| ----------- | -------- | ----------------------------------------------------------------------------------------------------- |
| `Operation` | yes      | an operation ([CONTRACT.policy.md §request-properties](CONTRACT.policy.md#request-properties))      |
| `Manager`   | yes      | a manager identifier ([CONTRACT.policy.md §managers](CONTRACT.policy.md#managers))                  |
| `Source`    | yes      | an object with one member, `Name`, a string of 1 to 128 bytes                                         |
| `Package`   | yes      | a package object                                                                                      |
| `Scope`     | no       | a scope ([CONTRACT.policy.md §request-properties](CONTRACT.policy.md#request-properties))           |
| `Elevation` | yes      | an elevation ([CONTRACT.policy.md §request-properties](CONTRACT.policy.md#request-properties))      |
| `OptIns`    | no       | an opt-ins object ([§opt-ins](#opt-ins))                                                              |

A package object has these members.

| Member         | Required | Value                                                                                                   |
| -------------- | -------- | ------------------------------------------------------------------------------------------------------- |
| `Id`           | yes      | a string of 1 to 256 ASCII letters, digits and `.`, `_`, `+`, `@`, `/`, `:`, `[`, `]`, `,`, `#`, `$`, `%`, `{`, `}` and `-` |
| `Version`      | no       | a string of 1 to 128 bytes                                                                              |
| `Architecture` | no       | an architecture ([CONTRACT.policy.md §request-properties](CONTRACT.policy.md#request-properties))     |

A package request whose `Scope` is `Machine` and whose `Elevation` is `NonElevated` is `BadRequest`, because it contradicts itself; the broker does not elevate it silently.
A package request that is not `BadRequest` is well-formed.
This contract defines only the shape of a package request: a well-formed request may still be unreadable under [CONTRACT.policy.md §reading](CONTRACT.policy.md#reading), and evaluating it then gives a decision, `Deny` with the check `UnreadableRequest`, not an error.

<a id="opt-ins"></a>
### Opt-ins

`OptIns` lists the opt-ins the request asks for, keyed by the opt-in names of [CONTRACT.policy.md §opt-ins](CONTRACT.policy.md#opt-ins), as a policy's `Grants` and `Forbids` are.
A key present asks for its opt-in, and a request without `OptIns`, or with `OptIns` set to `{}`, asks for none.
This table gives the value of each key.

| Key                     | Value                                                                                   |
| ----------------------- | --------------------------------------------------------------------------------------- |
| `Interactive`           | `{}`                                                                                    |
| `SkipHashCheck`         | `{}`                                                                                    |
| `CustomInstallLocation` | an object with one member, `Path`, a string of 1 to 2048 bytes                          |
| `CustomParameters`      | an object with one member, `Values`, a list of 1 to 64 strings of 1 to 512 bytes        |
| `InstallerArguments`    | an object with the members `Arguments`, a string of 1 to 2048 bytes, and `Mode`, `Append` or `Replace` |
| `PrePostCommands`       | an object with the members `Pre` and `Post`, each optional and a string of 1 to 2048 bytes, at least one of them present |
| `KillBeforeOperation`   | an object with one member, `Processes`, a list of 1 to 64 image names, each a string of 1 to 128 bytes without `*` or `?` |
| `UninstallPrevious`     | `{}`                                                                                    |
| `NoUpgrade`             | `{}`                                                                                    |

An image name, such as `notepad.exe`, is the file name of a process's executable, and the operation stops the processes with that image name as [`taskkill /F /IM`][taskkill] does.
`*` is excluded because `taskkill` reads it as a wildcard, and `?` because no Windows file name contains it.

Every key is well-formed with every manager; which managers accept a key is defined by [CONTRACT.policy.md §reading](CONTRACT.policy.md#reading), and what a value does by that contract's [§reading](CONTRACT.policy.md#reading), [§opt-ins](CONTRACT.policy.md#opt-ins), [§install-location](CONTRACT.policy.md#install-location) and [§managers](CONTRACT.policy.md#managers), and by [§operations](#operations) here.

<a id="equal-requests"></a>
### Equal package requests

Two package requests are equal when they have the same members with equal values, ignoring `RequestId`.
Two strings are equal when they have the same code points, two lists when they have equal entries in the same order, and two objects when they have the same keys with equal values, whatever the order of their keys.

<a id="policy-identity"></a>
## Policy identity

A policy identity is an object with these members, copied from the policy's `Metadata` ([CONTRACT.policy.md §metadata](CONTRACT.policy.md#metadata)).

| Member     | Value                               |
| ---------- | ----------------------------------- |
| `Id`       | the policy's `Id`, a string         |
| `Revision` | the policy's `Revision`, an integer |

<a id="decision"></a>
## Decision

A decision is an object with these members, whose values are those of [CONTRACT.policy.md §decision](CONTRACT.policy.md#decision) for the request, the accepted policy and the evaluation time.

| Member    | Value                                                                                                |
| --------- | ---------------------------------------------------------------------------------------------------- |
| `Outcome` | `Allow` or `Deny`                                                                                    |
| `Check`   | a check from [CONTRACT.policy.md §precedence](CONTRACT.policy.md#precedence)                         |
| `Rules`   | a list, possibly empty, of rule objects in the order that contract gives                             |
| `OptIns`  | a list, possibly empty, of opt-in names from [CONTRACT.policy.md §opt-ins](CONTRACT.policy.md#opt-ins), in the order of its table |
| `Audited` | a boolean                                                                                            |

A rule object has the member `Id`, the rule's id, and the member `Reason`, the rule's `Reason`, present only when the rule has one.

<a id="evaluate"></a>
## Evaluate

An evaluation request is a package request ([§package-request](#package-request)), and a `RequestId` in it is `BadRequest`.
The broker evaluates it against its accepted policy at its current time, starts nothing and changes nothing.
Its outcomes, in the order of [§errors](#errors) item 7, are:

1. `PolicyUnavailable`, when the broker holds no accepted policy;
2. `NotRunnable`, when the decision's `Outcome` is `Allow` and [§not-runnable](#not-runnable) applies;
3. the evaluation response.

The evaluation response has these members.

| Member        | Value                                                                          |
| ------------- | ------------------------------------------------------------------------------ |
| `Decision`    | the decision ([§decision](#decision))                                          |
| `Policy`      | the identity of the policy that decided ([§policy-identity](#policy-identity)) |
| `EvaluatedAt` | the evaluation time, a timestamp                                               |

An evaluation response means that an execution request at that time would start an operation exactly when its `Outcome` is `Allow`; it reserves nothing, and a later execution request is evaluated again.

<a id="not-runnable"></a>
### Not runnable

An allowed request whose `OptIns` has `PrePostCommands` and whose `Elevation` is `Elevated` is `NotRunnable`, because the broker never runs such a command with administrator rights, whatever the decision's `Audited`.
The error response carries the decision, whose `Outcome` is `Allow`, and the broker runs nothing for the request.
For a request, an accepted policy and an evaluation time, an evaluation request and an execution request get the same `NotRunnable` outcome.
[§operations](#operations) states how an operation ends when the broker would run its commands with an elevated token although its request's `Elevation` is `NonElevated`.

<a id="execute"></a>
## Execute

An execution request is a package request ([§package-request](#package-request)) with this member besides.

| Member      | Required | Value                                                                    |
| ----------- | -------- | ------------------------------------------------------------------------ |
| `RequestId` | yes      | an identifier the owner chooses, new for each operation it wants started |

Its outcomes, in the order of [§errors](#errors) item 7, are:

1. `RequestIdConflict`, when the broker holds an operation started for an execution request of the same owner with the same `RequestId`, and the two package requests are not equal ([§equal-requests](#equal-requests));
2. the replayed execution response, when the broker holds such an operation and the package requests are equal;
3. `PolicyUnavailable`, when the broker holds no accepted policy;
4. `NotRunnable`, when the decision's `Outcome` is `Allow` and [§not-runnable](#not-runnable) applies;
5. the execution response.

The execution response has these members.

| Member        | Present                                                    | Value                                                                          |
| ------------- | ---------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `Decision`    | always                                                     | the decision ([§decision](#decision))                                          |
| `Policy`      | always                                                     | the identity of the policy that decided ([§policy-identity](#policy-identity)) |
| `EvaluatedAt` | always                                                     | the evaluation time, a timestamp                                               |
| `OperationId` | when `Decision.Outcome` is `Allow`                         | the operation's id, an identifier                                              |
| `Status`      | when `Decision.Outcome` is `Allow`                         | the operation's status when the response is written ([§operations](#operations)) |
| `EventPipe`   | when the broker created an event channel for the operation | the name of the operation's event channel pipe, a string of 1 to 256 ASCII letters, digits, `.`, `_` and `-` ([§event-channel](#event-channel)) |

When `Decision.Outcome` is `Allow`, the broker starts one operation for the request before writing the response, and the operation's owner is the request's owner.
An execution request that starts no operation, whatever its outcome, leaves its `RequestId` unused.
A replayed execution response has the `Decision`, `Policy`, `EvaluatedAt`, `OperationId` and `EventPipe` of the response to the request that started the operation, and the operation's current `Status`; the broker evaluates nothing and starts nothing for it.
When several execution requests of one owner with one `RequestId` arrive together, the broker starts at most one operation for them, and every response to them that carries an `OperationId` carries that operation's.
A client that does not know the outcome of an execution request sends the same request again with the same `RequestId`, and gets the operation that request started, if any, without starting a second one, for as long as the broker holds the operation ([§operations](#operations)).
Once the broker no longer holds it, including after the broker restarts, the same request is evaluated again and, when allowed, starts a second operation.

<a id="operations"></a>
## Operations

An operation runs these steps in order, skipping a step whose `OptIns` member its request lacks:

1. it stops the processes of `KillBeforeOperation`;
2. it runs the `Pre` command of `PrePostCommands`;
3. it runs the manager process, which carries out the package request;
4. it runs the `Post` command of `PrePostCommands`.

This table is the closed set of operation statuses.

| Status      | Terminal | The operation                                                                 |
| ----------- | -------- | ----------------------------------------------------------------------------- |
| `Starting`  | no       | was started, and its manager process has not been launched                    |
| `Running`   | no       | has launched its manager process                                              |
| `Canceling` | no       | was asked to cancel and has not ended                                         |
| `Completed` | yes      | ended after its manager process exited on its own with code 0                 |
| `Failed`    | yes      | ended otherwise, as the rules below state                                     |
| `Canceled`  | yes      | was ended by the broker on a cancel request ([§cancel](#cancel))              |

A status changes only from `Starting` to `Running`, from `Starting` or `Running` to `Canceling`, and from a status that is not terminal to a terminal one, so a terminal status never changes.
A process of the operation runs too long when it runs longer than `OperationTimeoutSeconds` ([§capabilities](#capabilities)), and the broker then stops it.
These rules decide how an operation that is not canceled ends:

- when its request has `PrePostCommands` and the token the broker would run its processes with is elevated, such as when User Account Control is disabled, it runs no step and ends `Failed`, because the broker never runs a `Pre` or `Post` command with an elevated token;
- when its request is an Update through a manager that updates from the source recorded when the package was installed, and that record names another source or cannot be read ([CONTRACT.policy.md §managers](CONTRACT.policy.md#managers)), it runs no step and ends `Failed`;
- a failure to stop a process of `KillBeforeOperation` is ignored;
- when the `Pre` command cannot be run, exits with a code other than 0 or runs too long, it runs no later step and ends `Failed`;
- when the manager process cannot be launched or runs too long, it runs no later step and ends `Failed`;
- when the manager process exits on its own, it runs the `Post` command and then ends `Completed` when the manager process's exit code is 0, and `Failed` otherwise, however the `Post` command ends.

The broker never gives two operations the same `OperationId`.
An operation started by a request outside this contract is not an operation of this contract: no request of this contract names it, and its request's id neither conflicts with nor replays an execution request of this contract.
While the broker keeps running, it holds every operation whose status is not terminal, and holds a terminal operation for at least 300 seconds after it ended; a restart of the broker forgets every operation.
Once the broker no longer holds an operation, a request naming its `OperationId` is `OperationNotFound`, and its `RequestId` is unused again.
A status or cancel request names an operation the broker holds for its owner only when the operation's owner is the request's owner, so that the response for another owner's operation is the same as for an operation the broker does not hold.

<a id="status"></a>
## Status

The request is `OperationNotFound` when the broker holds no operation with the path's `OperationId` for the request's owner.
Otherwise the response has these members.

| Member        | Present                                                                                     | Value                                                     |
| ------------- | ------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| `Status`      | always                                                                                      | the operation's current status                            |
| `StartedAt`   | once the operation's manager process was launched                                           | when it was launched, a timestamp                         |
| `CompletedAt` | when the status is terminal                                                                 | when the operation ended, a timestamp                     |
| `ExitCode`    | when the status is terminal and the manager process exited on its own                       | the manager process's exit code, an integer from -2147483648 to 2147483647 |
| `Message`     | optional                                                                                    | a string of at most 2048 bytes, which carries no meaning a client may depend on |

<a id="cancel"></a>
## Cancel

The request is `OperationNotFound` when the broker holds no operation with the path's `OperationId` for the request's owner.
Otherwise, when the operation's status is not terminal, the broker sets it to `Canceling` and runs no later step.
Before the manager process has exited on its own, the broker stops the operation's running process and the operation ends `Canceled`, unless the rules of [§operations](#operations) ended it first.
Once the manager process has exited on its own, the broker stops or skips the `Post` command, and the operation ends as that exit decides ([§operations](#operations)).
The response has one member, `Status`, the operation's status after the request: `Canceling` when it was not terminal, and its terminal status otherwise.
Sending a cancel request again has no other effect.

<a id="event-channel"></a>
## Event channel

For every operation it starts, the broker creates the pipe `\\.\pipe\` followed by the response's `EventPipe` before writing the execution response, unless creating the pipe fails, in which case the response has no `EventPipe`.
The broker creates the pipe as the first instance of its name and allows no other instance, so creating it fails when another process created that name first.
Any process of the operation owner's Windows user, the local system and members of the Administrators group can open the pipe, for reading only; another process of the same user can therefore read the operation's frames.
The broker serves at most one connection on the pipe.
The broker closes the channel only:

- after sending `FINISH`;
- when no client has connected 60 seconds after the operation's status became terminal;
- when writing a frame has made no progress for 30 seconds;
- when the client closes its end;
- when the broker stops.

Closing the channel never affects the operation.
A client whose channel closes before the `FINISH` frame learns the operation's status with status requests.

<a id="frames"></a>
### Frames

The channel carries a sequence of frames, and every integer in a frame is unsigned and little-endian.
A frame is a 4-byte body size, a 2-byte frame kind, and a body of that size.
A fatal frame defect ends the channel for the client: it closes the channel and learns the operation's status with status requests.
A body size larger than 65536 is a fatal frame defect.
This table is the closed set of frame kinds.

| Kind     | Name              | Body                                                                 |
| -------- | ----------------- | -------------------------------------------------------------------- |
| `0x0000` | `HELLO`           | 4 bytes: a 2-byte major version `2`, then a 2-byte minor version `0` |
| `0x0001` | `STATUS_UPDATED`  | empty                                                                |
| `0x0002` | `FINISH`          | empty                                                                |
| `0x0003` | `STDOUT`          | 1 to 65536 bytes of UTF-8 text                                       |
| `0x0004` | `STDERR`          | 1 to 65536 bytes of UTF-8 text                                       |
| `0x0005` | `STDOUT_OVERFLOW` | 4 bytes: a number of standard output bytes dropped                   |
| `0x0006` | `STDERR_OVERFLOW` | 4 bytes: a number of standard error bytes dropped                    |

A frame of a kind in the table whose body has another size, or a `STDOUT` or `STDERR` body that is not valid UTF-8, is a fatal frame defect.
A client skips a frame whose kind is not in the table, and a `HELLO` major version other than `2` is a fatal frame defect.
The end of the stream inside a frame is a fatal frame defect, not a close.

<a id="frame-order"></a>
### Frame order

The broker sends only the frame kinds in the table.
`HELLO` is the first frame and is sent once.
The broker sends `STATUS_UPDATED` after each change of the operation's status, which the client reads with a status request.
Once the operation reaches a terminal status, the broker sends `STATUS_UPDATED`, may then send data and overflow frames for output it read earlier, and sends `FINISH` last.
An operation has one standard output stream and one standard error stream, each the concatenation of that output of the `Pre` command and then of the manager process, in the order the broker read it; `STDOUT` and `STDERR` carry them.
The broker decodes each stream as one UTF-8 sequence, replacing each maximal subpart of an ill-formed sequence, including an incomplete sequence at the end of the stream, with U+FFFD, as the [Unicode Standard 16.0.0][unicode16] chapter 3.9 describes for U+FFFD substitution of maximal subparts.
A code point is never split across frames.
When the client reads too slowly, the broker may drop the oldest decoded output of a stream that it has not sent; it then sends that stream's overflow frame before the next data frame of that stream or before `FINISH`.
The numbers of a stream's overflow frames count the bytes of decoded output dropped, and add up to all the bytes dropped since the previous data frame of that stream, using several frames when one number cannot hold them.
The broker drops no `HELLO`, `STATUS_UPDATED` or `FINISH` frame while the channel is open.

<a id="management"></a>
## Policy management

The response has one member, `Management`, the broker's policy management state.

<a id="management-state"></a>
### Policy management state

| Member           | Present                                    | Value                                                                    |
| ---------------- | ------------------------------------------ | ------------------------------------------------------------------------ |
| `State`          | always                                     | `Active`, `Missing` or `Invalid`                                         |
| `Path`           | always                                     | the path of the policy file the broker reads, a non-empty string of at most 32767 UTF-16 code units |
| `StoreToken`     | always                                     | a store token                                                            |
| `ReadOnlyReason` | when a replacement cannot write the policy | a reason from the table below                                            |
| `Document`       | when `State` is `Active`                   | the content of the policy file, as a string                              |
| `InvalidReason`  | when `State` is `Invalid`                  | a reason from the table below                                            |
| `Errors`         | when `InvalidReason` is `InvalidDocument`  | a non-empty list of policy errors ([§policy-errors](#policy-errors))     |

`State` is `Active` when the broker holds an accepted policy read from the policy file, `Missing` when no file exists at `Path`, and `Invalid` otherwise; the broker holds no accepted policy unless `State` is `Active`.
A store token is a string of 1 to 512 ASCII letters, digits, `.`, `_`, `~`, `:` and `-` whose first character is an ASCII letter or digit, which a client only compares and sends back.
The broker gives each content of the policy file, and its absence, its own store token, so two states carry the same store token only when the broker observed no change of the policy file between them.
`Document` is the file's bytes decoded as UTF-8, so a leading byte order mark becomes a leading U+FEFF, and a document the broker accepted from a replacement comes back exactly as it was sent.
When the accepted policy was converted from an earlier policy format, `Document` holds the file's text in that format.
That sentence and the `InvalidReason` `Unconvertible` apply only to a policy file in an earlier format ([INTENT.legacy.md §format-1](INTENT.legacy.md#format-1)), and leave this contract when the broker stops reading that format.
This table is the closed set of `InvalidReason` values.

| InvalidReason           | The policy file                                                                          |
| ----------------------- | ---------------------------------------------------------------------------------------- |
| `ReadFailed`            | cannot be opened or read                                                                 |
| `UnsupportedPath`       | has a path whose form or extension the broker does not read                              |
| `InsecureStorage`       | or its location fails the broker's storage security checks                               |
| `InvalidDocument`       | is not a valid policy document ([CONTRACT.policy.md §errors](CONTRACT.policy.md#errors)) |
| `Unconvertible`         | holds a policy in an earlier format that the broker cannot convert exactly               |
| `MonitoringUnavailable` | cannot be watched for changes, so the broker does not rely on it                         |

This table is the closed set of `ReadOnlyReason` values.

| ReadOnlyReason            | The broker cannot write the policy file because                       |
| ------------------------- | --------------------------------------------------------------------- |
| `MonitoringUnavailable`   | it cannot watch the file for changes                                  |
| `UnsupportedPath`         | the path has a form or extension it does not write                    |
| `UnsafePath`              | the path or its location fails its storage security checks            |
| `InsufficientPermissions` | it lacks the permissions to write it                                  |
| `UnsupportedFileSystem`   | the file system cannot replace the file atomically                    |

A state whose `InvalidReason` is `MonitoringUnavailable` has the `ReadOnlyReason` `MonitoringUnavailable`.
Which reason applies is implementation-defined when several do.

<a id="policy-errors"></a>
### Policy errors

A policy error is an object with these members, reporting one error of [CONTRACT.policy.md §errors](CONTRACT.policy.md#errors).

| Member    | Value                                                                  |
| --------- | ---------------------------------------------------------------------- |
| `Code`    | an error code from that section                                        |
| `Pointer` | the error's location, a [JSON Pointer][rfc6901] string, possibly empty |

<a id="validation"></a>
## Policy validation

A policy validation request is an object with this member.

| Member     | Required | Value                                   |
| ---------- | -------- | --------------------------------------- |
| `Document` | yes      | a string, the text of a policy document |

The document is the UTF-8 encoding of the string, so the rules of [CONTRACT.policy.md §json](CONTRACT.policy.md#json) apply to it, including its size limit and its byte order mark.
The body limit of [§endpoints](#endpoints) holds any document within that size limit, whatever escapes the client writes in the string.
The response has one member, `Errors`, the list of policy errors that the policy contract has a reader report for the document, which is empty exactly when the document is valid.
Validation reads and changes nothing the broker stores.

<a id="replacement"></a>
## Policy replacement

A policy replacement request is an object with these members.

| Member               | Required | Value                                                                     |
| -------------------- | -------- | ------------------------------------------------------------------------- |
| `ExpectedStoreToken` | yes      | a store token ([§management-state](#management-state))                    |
| `Document`           | yes      | a string, the text of a policy document, as in [§validation](#validation) |

The current store token is the store token of the policy file as it is when the broker decides the request, or when it writes the file for a request it writes.
Its outcomes, in the order of [§errors](#errors) item 7, are:

1. `StaleStoreToken`, when `ExpectedStoreToken` is not the current store token;
2. `PolicyNotWritable`, when the current state has a `ReadOnlyReason`;
3. `InvalidPolicy`, when the document is not valid, with its errors as [§validation](#validation) gives them;
4. `RevisionNotHigher`, when the broker holds an accepted policy whose `Id` equals the document's and whose `Revision` is not lower than the document's;
5. `PersistenceFailed`, when the broker failed to write the policy file;
6. `ActivationFailed`, when the broker wrote the policy file and does not hold the document as its accepted policy;
7. the replacement response.

A policy file that changes after the broker checked `ExpectedStoreToken` and before it writes makes the request `StaleStoreToken`, and the broker never writes over that change.
A document whose `Id` differs from the accepted policy's may have any `Revision`, and so may any document while the broker holds no accepted policy.
On success, the broker writes the UTF-8 encoding of `Document` as the whole content of the policy file, without changing it, and holds that document as its accepted policy before writing the response.
The replacement response has one member, `Management`, whose `State` is `Active`, whose `Document` equals the request's, and whose `StoreToken` differs from `ExpectedStoreToken`.
A package request evaluated after the response is evaluated against the new policy.
When several replacement requests with the same `ExpectedStoreToken` arrive together, at most one of them succeeds, and no other writes the policy file.
A client that does not know the outcome of a replacement reads the policy management state: a `Document` equal to the one it sent shows that its replacement, or one with the same document, succeeded.

[rfc6901]: https://www.rfc-editor.org/rfc/rfc6901
[rfc8259]: https://www.rfc-editor.org/rfc/rfc8259
[rfc9112]: https://www.rfc-editor.org/rfc/rfc9112
[taskkill]: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/taskkill
[unicode16]: https://www.unicode.org/versions/Unicode16.0.0/
