using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>
/// Client for communicating with the Devolutions Agent NOW package broker through
/// an HTTP-style request/response transport.
/// </summary>
public sealed class BrokerClient : IDisposable
{
    public const string DefaultPipeName = BrokerApi.DefaultPipeName;

    private const string JsonMediaType = "application/json";

    private readonly IBrokerTransport _transport;
    private readonly string _effectiveUser;
    private readonly Elevation _requestedElevation;
    private readonly string _clientExecutablePath;
    private readonly string _clientVersion;
    private CapabilitiesResponse? _capabilities;

    /// <summary>Optional diagnostic sink; receives human-readable trace lines.</summary>
    public Action<string>? Trace { get; init; }

    public BrokerClient(BrokerClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _transport = options.Transport ?? new NamedPipeBrokerTransport(options.PipeName);
        _effectiveUser = string.IsNullOrWhiteSpace(options.EffectiveUser)
            ? ResolveEffectiveUser()
            : options.EffectiveUser;
        _requestedElevation = options.RequestedElevation;
        _clientExecutablePath = string.IsNullOrWhiteSpace(options.ClientExecutablePath)
            ? ResolveClientExecutablePath()
            : options.ClientExecutablePath;
        _clientVersion = string.IsNullOrWhiteSpace(options.ClientVersion)
            ? ResolveClientVersion()
            : options.ClientVersion;
    }

    /// <summary>Check whether the broker is reachable (pipe exists and answers the health check).</summary>
    public async Task<bool> IsAvailable(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendRequest("GET", "/v1/health", null, null, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == 200;
        }
        catch (BrokerClientException ex)
        {
            Trace?.Invoke($"Broker not available: {ex.Message}");
            return false;
        }
    }

    /// <summary>Query the broker's health endpoint.</summary>
    public async Task<HealthResponse> GetHealth(CancellationToken cancellationToken = default)
    {
        var response = await SendRequest("GET", "/v1/health", null, null, cancellationToken).ConfigureAwait(false);
        return DeserializeResponse<HealthResponse>(response, "health", "/v1/health");
    }

    /// <summary>Query the broker's advertised capabilities and cache them for subsequent requests.</summary>
    public async Task<CapabilitiesResponse> GetCapabilities(CancellationToken cancellationToken = default)
    {
        _capabilities = await FetchCapabilities(cancellationToken).ConfigureAwait(false);
        return _capabilities;
    }

    /// <summary>Get the broker's active parsed policy document.</summary>
    public async Task<PolicyResponse> GetPolicy(CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string> { ["Accept"] = JsonMediaType };
        var response = await SendRequest("GET", "/v1/policy", null, headers, cancellationToken).ConfigureAwait(false);
        return DeserializeResponse<PolicyResponse>(
            response,
            "policy",
            "/v1/policy",
            strictSuccessBody: true);
    }

    /// <summary>Evaluate a package operation against policy without executing it (dry-run).</summary>
    public async Task<EvaluationResponse> Evaluate(PackageOperationRequest request, CancellationToken cancellationToken = default)
    {
        var packageRequest = CreatePackageRequest(request);
        return await SendPackageOperation<EvaluationResponse>(
            packageRequest,
            "/v1/package-operations/evaluate",
            JsonMediaType,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Submit a package operation for evaluation and (if allowed) elevated execution.</summary>
    public async Task<ExecutionResponse> Execute(PackageOperationRequest request, CancellationToken cancellationToken = default)
    {
        var packageRequest = CreatePackageRequest(request);
        return await SendPackageOperation<ExecutionResponse>(
            packageRequest,
            "/v1/package-operations/execute",
            JsonMediaType,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<EvaluationResponse> Evaluate(PackageRequest request, CancellationToken cancellationToken = default)
        => SendPackageOperation<EvaluationResponse>(
            request,
            "/v1/package-operations/evaluate",
            JsonMediaType,
            cancellationToken);

    private Task<ExecutionResponse> Execute(PackageRequest request, CancellationToken cancellationToken = default)
        => SendPackageOperation<ExecutionResponse>(
            request,
            "/v1/package-operations/execute",
            JsonMediaType,
            cancellationToken);

    /// <summary>
    /// Submit a package operation and poll until it reaches a terminal status
    /// (<see cref="OperationStatus.Completed"/>, <see cref="OperationStatus.Failed"/>,
    /// or <see cref="OperationStatus.Canceled"/>).
    /// When <paramref name="cancellationToken"/> is canceled after the operation was submitted,
    /// a best-effort broker-side cancelation is issued before the
    /// <see cref="OperationCanceledException"/> is propagated.
    /// </summary>
    public async Task<StatusResponse> ExecuteAndWait(
        PackageOperationRequest request,
        CancellationToken cancellationToken = default,
        int pollIntervalMs = 500)
    {
        var packageRequest = CreatePackageRequest(request);
        var executeResponse = await Execute(packageRequest, cancellationToken).ConfigureAwait(false);

        if (executeResponse.Decision.Decision != Decision.Allow)
        {
            Trace?.Invoke($"Operation denied by policy: {executeResponse.Decision.Reason}");
            throw new BrokerClientException(
                BrokerClientErrorKind.PolicyDenied,
                $"Operation denied by policy: {executeResponse.Decision.Reason}",
                "/v1/package-operations/execute");
        }

        if (executeResponse.Operation is null)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.InvalidResponse,
                "The broker allowed the execute request but did not include an operation submission.",
                "/v1/package-operations/execute");
        }

        var operationId = executeResponse.Operation.OperationId;

        try
        {
            while (true)
            {
                await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);

                var status = await QueryStatus(new OperationStatusQuery { OperationId = operationId }, cancellationToken)
                    .ConfigureAwait(false);

                if (status.Status is OperationStatus.Completed or OperationStatus.Failed or OperationStatus.Canceled)
                {
                    return status;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryCancelOperation(operationId).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Request cancelation of a previously submitted package operation.</summary>
    /// <remarks>
    /// Cancelation is asynchronous: a successful response with
    /// <see cref="OperationStatus.Canceling"/> means the broker accepted the request and is
    /// terminating the operation. Poll <see cref="QueryStatus"/> until a terminal status is reached.
    /// </remarks>
    public async Task<CancelResponse> Cancel(
        OperationCancelQuery request,
        CancellationToken cancellationToken = default)
    {
        var cancelRequest = CreateCancelRequest(request);

        var capabilities = await GetCachedCapabilities(cancellationToken).ConfigureAwait(false);
        EnsureTransportSupported(capabilities, _transport.Kind, "cancel operation", "/v1/package-operations/cancel");

        var body = BrokerJson.Serialize(cancelRequest);
        EnsureRequestBodySize(body, capabilities, "/v1/package-operations/cancel");

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = JsonMediaType,
            ["Accept"] = JsonMediaType,
        };

        var response = await SendRequest(
            "POST",
            "/v1/package-operations/cancel",
            body,
            headers,
            cancellationToken).ConfigureAwait(false);

        return DeserializeResponse<CancelResponse>(response, "cancel", "/v1/package-operations/cancel");
    }

    private async Task TryCancelOperation(string operationId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await Cancel(new OperationCancelQuery { OperationId = operationId }, timeout.Token)
                .ConfigureAwait(false);
            Trace?.Invoke($"Requested broker-side cancelation of operation {operationId}: {response.Status}");
        }
        catch (Exception ex)
        {
            // Best-effort: the caller is already canceling; never mask the original cancelation,
            // regardless of what the (possibly user-provided) transport throws.
            Trace?.Invoke($"Failed to cancel broker operation {operationId}: {ex.Message}");
        }
    }

    /// <summary>Query the status of a previously submitted package operation.</summary>
    public async Task<StatusResponse> QueryStatus(
        OperationStatusQuery request,
        CancellationToken cancellationToken = default)
    {
        var statusRequest = CreateStatusRequest(request);

        var capabilities = await GetCachedCapabilities(cancellationToken).ConfigureAwait(false);
        EnsureTransportSupported(capabilities, _transport.Kind, "query operation status", "/v1/package-operations/get-status");

        var body = BrokerJson.Serialize(statusRequest);
        EnsureRequestBodySize(body, capabilities, "/v1/package-operations/get-status");

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = JsonMediaType,
            ["Accept"] = JsonMediaType,
        };

        var response = await SendRequest(
            "POST",
            "/v1/package-operations/get-status",
            body,
            headers,
            cancellationToken).ConfigureAwait(false);

        return DeserializeResponse<StatusResponse>(response, "status", "/v1/package-operations/get-status");
    }

    /// <summary>
    /// Connect to the per-operation event channel advertised in an execution response and
    /// return a reader for <c>NOW_BROKER</c> event frames (stdout/stderr data and status
    /// change notifications).
    /// </summary>
    /// <exception cref="BrokerClientException">
    /// The response carries no event channel descriptor, the channel kind is not
    /// supported, or the connection failed.
    /// </exception>
    public async Task<OperationEventChannel> OpenEventChannel(
        ExecutionResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var descriptor = response.Operation?.EventChannel
            ?? throw new BrokerClientException(
                BrokerClientErrorKind.InvalidResponse,
                "The execution response does not carry an event channel descriptor; "
                + "the broker likely does not support event channels.");

        if (descriptor.Kind != EventChannelKind.LocalPipe)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Unsupported event channel kind '{descriptor.Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Path))
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.InvalidResponse,
                "The event channel descriptor carries an empty path.");
        }

        Trace?.Invoke($"Connecting to operation event channel pipe '{descriptor.Path}'.");
        return await OperationEventChannel.ConnectLocalPipe(descriptor.Path, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _transport.Dispose();
    }

    public static string GenerateRequestId() => Guid.NewGuid().ToString("D").ToLowerInvariant();

    private PackageRequest CreatePackageRequest(PackageOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PackageRequest
        {
            RequestId = NormalizeRequestId(request.RequestId),
            RequestVersion = BrokerApi.Version,
            CreatedAt = request.CreatedAt ?? DateTimeOffset.UtcNow,
            Operation = request.Operation,
            Manager = request.Manager,
            Source = request.Source,
            Package = request.Package,
            Options = request.Options,
            Client = CreateClientContext(),
            IncludeCommandPreview = request.IncludeCommandPreview,
            CaptureOutput = request.CaptureOutput,
        };
    }

    private static string NormalizeRequestId(string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return GenerateRequestId();
        }

        if (Guid.TryParse(requestId, out var parsed))
        {
            return parsed.ToString("D").ToLowerInvariant();
        }

        throw new BrokerClientException(
            BrokerClientErrorKind.InvalidRequest,
            "RequestId must be a valid GUID.");
    }

    private StatusRequest CreateStatusRequest(OperationStatusQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);

        return new StatusRequest
        {
            RequestVersion = BrokerApi.Version,
            OperationId = request.OperationId,
            Client = CreateClientContext(),
        };
    }

    private CancelRequest CreateCancelRequest(OperationCancelQuery request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);

        return new CancelRequest
        {
            RequestVersion = BrokerApi.Version,
            OperationId = request.OperationId,
            Client = CreateClientContext(),
        };
    }

    private async Task<TResponse> SendPackageOperation<TResponse>(
        PackageRequest request,
        string endpoint,
        string acceptMediaType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var capabilities = await GetCachedCapabilities(cancellationToken).ConfigureAwait(false);
        EnsurePackageRequestSupported(request, capabilities, endpoint);

        var body = BrokerJson.Serialize(request);
        EnsureRequestBodySize(body, capabilities, endpoint);

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = JsonMediaType,
            ["Accept"] = acceptMediaType,
        };

        var response = await SendRequest("POST", endpoint, body, headers, cancellationToken).ConfigureAwait(false);

        return DeserializeResponse<TResponse>(response, endpoint, endpoint);
    }

    private Task<BrokerTransportResponse> SendRequest(
        string method,
        string path,
        string? body,
        Dictionary<string, string>? extraHeaders,
        CancellationToken cancellationToken)
        => _transport.Send(
            new BrokerTransportRequest
            {
                Method = method,
                Path = path,
                Body = body,
                Headers = extraHeaders ?? new Dictionary<string, string>(),
            },
            cancellationToken);

    private CapabilitiesResponse? CachedCapabilities => _capabilities;

    private async Task<CapabilitiesResponse> FetchCapabilities(CancellationToken cancellationToken)
    {
        var response = await SendRequest("GET", "/v1/capabilities", null, null, cancellationToken).ConfigureAwait(false);
        return DeserializeResponse<CapabilitiesResponse>(response, "capabilities", "/v1/capabilities");
    }

    private async Task<CapabilitiesResponse> GetCachedCapabilities(CancellationToken cancellationToken)
    {
        if (CachedCapabilities is not null)
        {
            return CachedCapabilities;
        }

        _capabilities = await FetchCapabilities(cancellationToken).ConfigureAwait(false);
        return _capabilities;
    }

    private TResponse DeserializeResponse<TResponse>(
        BrokerTransportResponse response,
        string context,
        string endpoint,
        bool strictSuccessBody = false)
    {
        if (string.IsNullOrWhiteSpace(response.Body))
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.EmptyResponse,
                $"Broker returned an empty response body for {context} (HTTP {response.StatusCode}).",
                endpoint,
                response.StatusCode);
        }

        if (response.StatusCode >= 400)
        {
            if (TryDeserializeBrokerError(response.Body, out var error, out var parseError))
            {
                throw new BrokerClientException(
                    BrokerClientErrorKind.BrokerError,
                    $"Broker returned HTTP {response.StatusCode} ({error.Code}) for {context}: {error.Message}",
                    endpoint,
                    response.StatusCode,
                    error);
            }

            throw new BrokerClientException(
                BrokerClientErrorKind.BrokerError,
                $"Broker returned HTTP {response.StatusCode} for {context}, but the error body could not be parsed: {parseError?.Message ?? "unknown parse error"}",
                endpoint,
                response.StatusCode,
                innerException: parseError);
        }

        try
        {
            var value = strictSuccessBody
                ? BrokerJson.DeserializeStrict<TResponse>(response.Body)
                : BrokerJson.Deserialize<TResponse>(response.Body);
            if (value is null)
            {
                throw new BrokerClientException(
                    BrokerClientErrorKind.InvalidResponse,
                    $"Broker returned an empty JSON value for {context}.",
                    endpoint,
                    response.StatusCode);
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.InvalidResponse,
                $"Broker returned malformed JSON for {context}: {ex.Message}",
                endpoint,
                response.StatusCode,
                innerException: ex);
        }
    }

    private static bool TryDeserializeBrokerError(
        string body,
        [NotNullWhen(true)] out ErrorResponse? error,
        out JsonException? parseError)
    {
        try
        {
            error = BrokerJson.Deserialize<ErrorResponse>(body);
            parseError = null;
            return error is not null;
        }
        catch (JsonException ex)
        {
            error = null;
            parseError = ex;
            return false;
        }
    }

    private void EnsurePackageRequestSupported(PackageRequest request, CapabilitiesResponse capabilities, string endpoint)
    {
        EnsureTransportSupported(capabilities, _transport.Kind, $"send package operation requests to {endpoint}", endpoint);

        var manager = capabilities.Managers.FirstOrDefault(manager => manager.Manager == request.Manager);
        if (manager is null)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker does not advertise support for package manager '{request.Manager}'.",
                endpoint);
        }

        if (!manager.Operations.Contains(request.Operation))
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker package manager '{request.Manager}' does not support operation '{request.Operation}'.",
                endpoint);
        }

        if (request.Options.Scope is { } scope && !manager.Scopes.Contains(scope))
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker package manager '{request.Manager}' does not support scope '{scope}'.",
                endpoint);
        }

        if (request.Package.Architecture is { } architecture && !manager.Architectures.Contains(architecture))
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker package manager '{request.Manager}' does not support architecture '{architecture}'.",
                endpoint);
        }

        if (request.Options.CustomParameters.Count > 0 && !manager.SupportsCustomParameters)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker package manager '{request.Manager}' does not support custom parameters.",
                endpoint);
        }

        if (!string.IsNullOrWhiteSpace(request.Options.CustomInstallLocation) && !manager.SupportsCustomInstallLocation)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker package manager '{request.Manager}' does not support custom install locations.",
                endpoint);
        }

        if (request.CaptureOutput && !manager.SupportsCaptureOutput)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker package manager '{request.Manager}' does not support captured operation output.",
                endpoint);
        }
    }

    private static void EnsureTransportSupported(
        CapabilitiesResponse capabilities,
        Transport transport,
        string operationDescription,
        string endpoint)
    {
        if (!capabilities.Transports.Contains(transport))
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.UnsupportedCapability,
                $"Broker does not advertise '{transport}' transport support; cannot {operationDescription}.",
                endpoint);
        }
    }

    private static void EnsureRequestBodySize(string body, CapabilitiesResponse capabilities, string endpoint)
    {
        var bodyLength = Encoding.UTF8.GetByteCount(body);
        if (bodyLength <= capabilities.MaxRequestBodyBytes)
        {
            return;
        }

        throw new BrokerClientException(
            BrokerClientErrorKind.RequestTooLarge,
            $"Request body for {endpoint} is {bodyLength} bytes, which exceeds broker limit of {capabilities.MaxRequestBodyBytes} bytes.",
            endpoint);
    }

    private ClientContext CreateClientContext() => new()
    {
        Transport = _transport.Kind,
        RequestedElevation = _requestedElevation,
        EffectiveUser = _effectiveUser,
        ClientExecutablePath = _clientExecutablePath,
        ClientVersion = _clientVersion,
    };

    private static string ResolveClientExecutablePath()
    {
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine client executable path. Pass ClientExecutablePath explicitly.");
    }

    private static string ResolveClientVersion()
    {
        return typeof(BrokerClient).Assembly.GetName().Version?.ToString()
            ?? "0.0.0.0";
    }

    private static string ResolveEffectiveUser()
    {
        var userName = Environment.UserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("Unable to determine effective user. Pass EffectiveUser explicitly.");
        }

        var domainName = Environment.UserDomainName;
        return string.IsNullOrWhiteSpace(domainName)
            ? userName
            : $"{domainName}\\{userName}";
    }
}