Devolutions NOW package broker client for .NET
==============================================

`Devolutions.Now.Policy.Client` contains .NET client logic for communicating with a Devolutions NOW package broker. It builds on `Devolutions.Now.Policy.Api` DTOs and implements the client-side HTTP-over-named-pipe transport.

Purpose
-------

This package is the .NET transport/client layer for the package broker API. It does not define the protocol schema itself and does not execute package-manager operations locally. Instead, it serializes API DTOs, sends them to a broker, and deserializes broker responses.

The client is used to:

- discover whether a local broker is reachable;
- query broker health and capabilities;
- evaluate package operations without executing them;
- submit package operations for elevated execution;
- poll asynchronous operation status until completion or failure.

Execution responses additionally return a per-operation event channel descriptor (`OperationSubmission.EventChannel`) whenever the broker supports event channels. The channel carries the `NOW_BROKER` frame protocol: status change notifications pushed by the broker and, when the operation was submitted with `CaptureOutput`, stdout/stderr data. The frame codec (`EventFrame`, `EventFrameDecoder`) lives in `Devolutions.Now.Policy.Api`; see `policies/docs/event-channel-protocol.md` for the wire specification.

To consume the channel, pass the execution response to `BrokerClient.OpenEventChannel`; it connects to the advertised local pipe and returns an `OperationEventChannel`:

```csharp
var execution = await client.Execute(request);
await using var channel = await client.OpenEventChannel(execution);

await foreach (var frame in channel.ReadEvents())
{
    switch (frame)
    {
        case EventFrame.Stdout stdout: Console.Out.Write(stdout.Data); break;
        case EventFrame.Stderr stderr: Console.Error.Write(stderr.Data); break;
        case EventFrame.StatusUpdated: /* issue QueryStatus */ break;
        case EventFrame.Finish: /* operation finished; enumeration completes */ break;
    }
}
```

`ReadEvents` skips unknown frame kinds and completes after `Finish` or when the broker closes the channel; `ReadFrame` exposes the raw frame stream, including `EventFrame.Unknown`.

Architecture
------------

The main surface is `BrokerClient`:

- `IsAvailable` probes the health endpoint.
- `GetHealth` and `GetCapabilities` query broker metadata.
- `GetPolicy` sends `GET /v1/policy` and returns a `PolicyResponse` containing the active parsed `PolicyDocument` after strict validation of the successful response.
- `GetPolicyManagement` gets the atomic active/missing/invalid management snapshot and advisory write capability.
- `ValidatePolicy` preserves raw `JsonElement` draft content for authoritative validation and returns a canonical draft, exact findings, and receipt.
- `ReplacePolicy` performs a token- and receipt-bound optimistic replacement; confirmed overwrite still targets an exact newly observed token and is never unconditional.
- `Evaluate` sends `POST /v1/package-operations/evaluate`.
- `Execute` sends `POST /v1/package-operations/execute`.
- `ExecuteAndWait` submits an operation and polls status until a terminal state.
- `QueryStatus` sends `POST /v1/package-operations/get-status`.
- `Cancel` sends `POST /v1/package-operations/cancel` to request cancelation of an in-flight operation.
- `OpenEventChannel` connects to the per-operation event channel advertised in an `ExecutionResponse` and returns an `OperationEventChannel` frame reader.

Transport is abstracted behind `IBrokerTransport`, which exchanges HTTP-style `BrokerTransportRequest` and `BrokerTransportResponse` values. `NamedPipeBrokerTransport` is the default implementation and sends HTTP/1.1 over a Windows named pipe. Tests and future transports can inject their own transport through `BrokerClientOptions.Transport`.

Named pipe transport
--------------------

`NamedPipeBrokerTransport` opens a new connection for each request and is configured through `NamedPipeBrokerTransportOptions`.
`BrokerClient` uses it with `BrokerClientOptions.NamedPipeTransport` when no `Transport` is supplied.

By default, on Windows, the transport:

- opens the pipe with generic read and write-data access only (`PipeAccessRights.Read | Synchronize | WriteData`), which is a subset of what every broker version grants to standard users;
- grants the server identification-level impersonation (`ImpersonationLevel`, default `TokenImpersonationLevel.Identification`);
- verifies the pipe server before writing any request byte (`VerifyServer`, default `true`): the server process reported by the kernel must be the running process of a `ServerServiceNames` service (default `DevolutionsAgent`, then `devolutions-agent`), configured to run as LocalSystem;
- writes the complete request header and body in a single write right after connecting;
- retries `503 Service Unavailable` replies that carry `Retry-After`, over a new connection, up to `MaxBusyRetries` times (default 3) with the delay capped by `MaxBusyRetryDelay` (default 5 seconds);
- retries, within the same `MaxBusyRetries` budget and with a backoff starting at 100 ms, connections the broker closes without sending any response byte, but only when the request did not fully reach the broker or has no side effects (`GET`, `HEAD`, evaluation, status, and policy validation requests); other requests, such as executions and policy replacements, fail with `BrokerUnavailable`;
- applies `ConnectTimeout` (default 5 seconds), during which a busy or temporarily missing pipe is retried, and `ResponseTimeout` (default 30 seconds);
- rejects responses whose headers exceed `MaxResponseHeaderBytes` (default 64 KiB) or whose `Content-Length` exceeds `MaxResponseBodyBytes` (default 64 MiB), before allocating the body buffer.

Server verification works for standard users because it relies on service control manager queries.
When the caller is allowed to open the server process (typically elevated callers), the transport also requires the process token to be readable with the user LocalSystem, and holds the process handle until the exchange completes.
A failed verification throws `BrokerClientException` with `Kind == BrokerClientErrorKind.ServerVerificationFailed`.
Verification is only supported on Windows; set `VerifyServer = false` to reach a development or test broker.

Responses must use strict HTTP/1.1 framing: an `HTTP/1.1` status line, a single valid `Content-Length`, no `Transfer-Encoding`, and no data past the declared body.
Framing errors are reported as `InvalidResponse` with the response `StatusCode`.
A connection closed after the response started but before it completed is reported with `IncompleteResponseErrorKind` (default `InvalidResponse`) and no `StatusCode`.
A connection closed before any response byte is reported as `BrokerUnavailable` once retries are exhausted or not allowed.

Callers that need additional server checks, such as signature or install location checks, can set `ServerAuthenticator`.
It runs after the built-in verification and before any request byte is written, receives the connected pipe, the server process id and, when available, the held server process handle, and may return an `IDisposable` kept alive until the response is read.
It is bounded by `ServerAuthenticatorTimeout` (default 4 seconds), because the broker closes connections that do not send a request header within 5 seconds.
When the transport stops waiting, the pipe and the late authentication result are disposed once the authenticator completes.

```csharp
var client = new BrokerClient(new BrokerClientOptions
{
    RequestedElevation = Elevation.Standard,
    NamedPipeTransport = new NamedPipeBrokerTransportOptions
    {
        MaxResponseBodyBytes = 1024 * 1024,
        ServerAuthenticator = (context, cancellationToken) =>
        {
            // Inspect context.ServerProcessId and context.ServerProcess; throw to reject the server.
            return ValueTask.FromResult<IDisposable?>(null);
        },
    },
});
```

Client context
--------------

`BrokerClient` owns the client-controlled context fields sent in `PackageRequest.Client` and `StatusRequest.Client`.

Callers provide the fields that cannot be derived reliably:

```csharp
var client = new BrokerClient(new BrokerClientOptions
{
    RequestedElevation = Elevation.Elevated,
});
```

The client fills the remaining context implicitly:

- `Transport` is taken from the configured `IBrokerTransport`.
- `EffectiveUser` defaults to the current user and can be overridden through `BrokerClientOptions.EffectiveUser`.
- `ClientVersion` defaults to the `Devolutions.Now.Policy.Client` assembly version.
- `ClientExecutablePath` defaults to the current process image path (`Environment.ProcessPath`), which the broker checks against the calling process, and can be overridden through `BrokerClientOptions.ClientExecutablePath`.

The public client methods accept client-facing wrapper types instead of raw wire DTOs:

- `PackageOperationRequest` omits `ClientContext` and lets the client fill it.
- `OperationStatusQuery` omits `ClientContext` and only requires the operation id.
- `OperationCancelQuery` omits `ClientContext` and only requires the operation id.

For transport-independent message identification, request DTOs serialize fixed `RequestKind`
discriminators automatically while the client fills `RequestVersion` at the top level of
`PackageRequest` and `StatusRequest`. Responses carry fixed top-level `ResponseKind` discriminators
and `ResponseVersion`; this is required for further protocol evolution and allows the client to
switch transport from HTTP to other mechanisms without changing the wire schema.

Before sending package operation and status requests, the client implicitly queries `GetCapabilities` once and caches the result. The cached capabilities are used as a local preflight gate: unsupported transports, package managers, operations, scopes, architectures, request body sizes, custom parameters, custom install locations, or captured output requests fail before the client sends the operation/status request. Use `CapabilitiesResponse.SupportsManager(ManagerName)` or `GetManagerCapability(ManagerName)` to check package manager support ahead of time.

Policy validation and replacement use the separate fixed `BrokerApi.MaxPolicyManagementBodyBytes`
limit (16 MiB / 16,777,216 bytes). `BrokerClient` measures the serialized UTF-8 request body,
including the complete validation or replacement envelope, before sending it. Transport helpers
must apply the same full-body limit to `POST /v1/policy/validate` and `PUT /v1/policy` only; package
operation requests retain their advertised 256 KiB default. The 16 MiB value is an operational cap
for realistic policies within the 1,024-rule editor model, not the schema's pathological theoretical
maximum.

Before sending package operation requests, the client fills missing request metadata:

- `RequestId` is generated with `BrokerClient.GenerateRequestId()` when empty. Request IDs are normalized to lowercase dashed GUIDs without braces.
- `CreatedAt` is set to `DateTimeOffset.UtcNow` when left as the default value.

Grant execution
---------------

Grant execution relies on these limits:

- Server authentication assumes that the Service Control Manager reports a running service's PID only while that process lives, so another process cannot reuse it. This assumption is untested.
- A grant binds the target user and the exact request, not one process; another process running as the same user can redeem a grant it obtains.
- An outcome that depends on captured output is unknown, because the event stream cannot prove that output is complete or unaltered.

`BrokerClient.ExecuteGrant(request, grant)` runs a package operation that a Gateway grant authorizes.
It posts to `/v1/package-operations/execute-grant` only, sends the grant in the single `X-Devolutions-Package-Grant` header, and never retries or falls back to `/execute`.
The grant must be 1 to 2,048 ASCII characters matching `[A-Za-z0-9][A-Za-z0-9._~:-]*`.
`request.RequestId` must already be a lowercase hyphenated UUID and is sent unchanged, `CaptureOutput` must be true, and `IncludeCommandPreview` must be false.
Status and cancellation use the existing methods with the returned operation ID and no grant.

The client authenticates the pipe server before writing any byte.
Set `BrokerClientOptions.ServerAuthenticator` and use `NamedPipeBrokerTransport`, or another `IAuthenticatedBrokerTransport`; otherwise `ExecuteGrant` throws `UnsupportedCapability` without sending anything.
`ServiceBrokerServerAuthenticator` compares the pipe server PID with the running `DevolutionsAgent` service PID and requires session 0.
It relies on the PID-reuse assumption above; implement `IBrokerServerAuthenticator` to use a different check.
An authentication failure after the request was sent leaves the outcome unknown.

`BrokerClientException.NoWorkStarted` is true only when the request was never sent or the broker returned a documented rejection that starts no work.
It applies to that request alone: an earlier, concurrent or later request may redeem the same grant, so it never proves that the operation failed.
When it is false, including after a timeout, disconnect, malformed response or unexpected status, the outcome may be unknown.
A 404 from an older broker throws `UnsupportedEndpoint`. The grant and response text are never copied into exceptions or `Trace`.

`OpenGrantEventChannel(response)` checks the event pipe server PID and session against the submission pipe and calls the authenticator again; a mismatch throws `ServerAuthenticationFailed`.
`OperationEventChannel.IsCaptureComplete` is true only after a valid `Hello`, a `Finish`, and no overflow or malformed frame.
It is necessary but not sufficient: it cannot detect frames consumed by another reader, or output written or held open by other processes that inherited the command's output handles.
A grant channel throws on end of stream before `Finish`.
It rejects data observed after `Finish`, but cannot detect data arriving after the caller stops reading.
A caller must treat `!IsCaptureComplete` or any exception as an unknown outcome, and must not treat `IsCaptureComplete` alone as proof of an outcome that depends on output.

Error handling and diagnostics
------------------------------

Response-oriented methods return successful DTOs or throw `BrokerClientException`. The exception includes:

- `Kind`, a `BrokerClientErrorKind` such as `BrokerUnavailable`, `Timeout`, `BrokerError`, `InvalidResponse`, `InvalidRequest`, `UnsupportedCapability`, `RequestTooLarge`, `ServerVerificationFailed`, `ServerAuthenticationFailed`, or `UnsupportedEndpoint`.
- `Endpoint`, when the failing broker endpoint is known.
- `StatusCode` and `BrokerError`, when the broker returned a structured `ErrorResponse`.
- `NoWorkStarted` and `BrokerErrorCode`, set by grant execution.

`IsAvailable` remains a boolean probe and reports diagnostics through `BrokerClient.Trace`. Other methods do not silently convert failures into `null`.

`GetPolicy` preserves both legacy and structured unsupported-endpoint behavior. Old Agents may return an empty or non-JSON 404, which is exposed with `StatusCode == 404` and no `BrokerError`. Rebuilt implementations may return a structured `ErrorResponse` with `Code == NotFound`. A supported Agent that cannot provide its active policy returns a structured non-404 error.

The policy management methods preserve the same ordinary 404 behavior when an older Agent does not expose a newer route. A structured `StalePolicyStoreToken` error carries the atomic current `Management` snapshot; use its exact store token for an explicitly confirmed overwrite retry. `UnsafePolicyPath` uses HTTP 409 because it represents the current storage/write-capability state rather than authentication or elevation. Configured `.yaml`, `.yml`, extensionless, and other non-JSON policy paths use `PolicyReadOnlyReason.UnsupportedFormat` and `ErrorCode.UnsupportedPolicyFormat` (HTTP 422).

Schema relationship
-------------------

The client depends on `Devolutions.Now.Policy.Api`, whose DTOs are validated against the OpenAPI document generated from Rust:

```text
policies\rust\now-policy-api\openapi\now-policy-api.yaml
```

Sample request and response documents are shared with the Rust server-template tests so the .NET client layer stays aligned with the same contract.

Validation
----------

Useful targeted checks:

```powershell
dotnet test policies\dotnet\Devolutions.Now.Policy.Client.Tests\Devolutions.Now.Policy.Client.Tests.csproj
dotnet format policies\dotnet\Devolutions.Now.Policy.slnx --verify-no-changes
```

Run the Rust OpenAPI generator before these checks when API model or route metadata changes.
