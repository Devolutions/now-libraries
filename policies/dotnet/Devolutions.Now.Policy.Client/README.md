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
- `ClientExecutablePath` defaults to the current process path and can be overridden through `BrokerClientOptions.ClientExecutablePath`.

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

Before sending package operation requests, the client fills missing request metadata:

- `RequestId` is generated with `BrokerClient.GenerateRequestId()` when empty. Request IDs are normalized to lowercase dashed GUIDs without braces.
- `CreatedAt` is set to `DateTimeOffset.UtcNow` when left as the default value.

Error handling and diagnostics
------------------------------

Response-oriented methods return successful DTOs or throw `BrokerClientException`. The exception includes:

- `Kind`, a `BrokerClientErrorKind` such as `BrokerUnavailable`, `Timeout`, `BrokerError`, `InvalidResponse`, `InvalidRequest`, `UnsupportedCapability`, or `RequestTooLarge`.
- `Endpoint`, when the failing broker endpoint is known.
- `StatusCode` and `BrokerError`, when the broker returned a structured `ErrorResponse`.

`IsAvailable` remains a boolean probe and reports diagnostics through `BrokerClient.Trace`. Other methods do not silently convert failures into `null`.

`GetPolicy` preserves both legacy and structured unsupported-endpoint behavior. Old Agents may return an empty or non-JSON 404, which is exposed with `StatusCode == 404` and no `BrokerError`. Rebuilt implementations may return a structured `ErrorResponse` with `Code == NotFound`. A supported Agent that cannot provide its active policy returns a structured non-404 error.

The policy management methods preserve the same ordinary 404 behavior when an older Agent does not expose a newer route. A structured `StalePolicyStoreToken` error carries the atomic current `Management` snapshot; use its exact store token for an explicitly confirmed overwrite retry. `UnsafePolicyPath` uses HTTP 409 because it represents the current storage/write-capability state rather than authentication or elevation.

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
