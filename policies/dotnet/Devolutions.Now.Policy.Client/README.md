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

Architecture
------------

The main surface is `BrokerClient`:

- `IsAvailable` probes the health endpoint.
- `GetHealth` and `GetCapabilities` query broker metadata.
- `Evaluate` sends `POST /v1/package-operations/evaluate`.
- `Execute` sends `POST /v1/package-operations/execute`.
- `ExecuteAndWait` submits an operation and polls status until a terminal state.
- `QueryStatus` sends `POST /v1/package-operations/get-status`.

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

For transport-independent message identification, request DTOs serialize fixed `RequestKind`
discriminators automatically while the client fills `RequestVersion` at the top level of
`PackageRequest` and `StatusRequest`. Responses carry fixed top-level `ResponseKind` discriminators
and `ResponseVersion`; this is required for further protocol evolution and allows the client to
switch transport from HTTP to other mechanisms without changing the wire schema.

Before sending package operation and status requests, the client implicitly queries `GetCapabilities` once and caches the result. The cached capabilities are used as a local preflight gate: unsupported transports, package managers, operations, scopes, architectures, request body sizes, custom parameters, custom install locations, or captured output requests fail before the client sends the operation/status request.

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
