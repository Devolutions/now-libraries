using System.Security.Principal;

using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>Configuration of <see cref="NamedPipeBrokerTransport"/>.</summary>
public sealed record NamedPipeBrokerTransportOptions
{
    /// <summary>Windows service name used by the Devolutions Agent installer.</summary>
    public const string AgentServiceName = "DevolutionsAgent";

    /// <summary>Windows service name used when the Devolutions Agent service is registered manually.</summary>
    public const string AgentManualServiceName = "devolutions-agent";

    /// <summary>Default value of <see cref="MaxResponseHeaderBytes"/> (64 KiB).</summary>
    public const int DefaultMaxResponseHeaderBytes = 64 * 1024;

    /// <summary>
    /// Default value of <see cref="MaxResponseBodyBytes"/> (64 MiB). A policy replacement response can carry
    /// a policy of up to <see cref="BrokerApi.MaxPolicyManagementBodyBytes"/> three times, plus its envelope.
    /// </summary>
    public const int DefaultMaxResponseBodyBytes = BrokerApi.MaxPolicyManagementBodyBytes * 4;

    /// <summary>Default value of <see cref="MaxBusyRetries"/>.</summary>
    public const int DefaultMaxBusyRetries = 3;

    /// <summary>Default value of <see cref="ConnectTimeout"/>.</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Default value of <see cref="ResponseTimeout"/>.</summary>
    public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default value of <see cref="ServerAuthenticatorTimeout"/>.</summary>
    public static readonly TimeSpan DefaultServerAuthenticatorTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Default value of <see cref="MaxBusyRetryDelay"/>.</summary>
    public static readonly TimeSpan DefaultMaxBusyRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Named pipe exposed by the package broker. Defaults to <see cref="BrokerApi.DefaultPipeName"/>.</summary>
    public string? PipeName { get; init; }

    /// <summary>Maximum time to wait for the pipe connection, including while the broker pipe is busy.</summary>
    public TimeSpan ConnectTimeout { get; init; } = DefaultConnectTimeout;

    /// <summary>Maximum time to send the request and receive the complete response, per attempt.</summary>
    public TimeSpan ResponseTimeout { get; init; } = DefaultResponseTimeout;

    /// <summary>
    /// Maximum accepted response body size in bytes. A response declaring a larger <c>Content-Length</c>
    /// is rejected before its body buffer is allocated.
    /// </summary>
    public int MaxResponseBodyBytes { get; init; } = DefaultMaxResponseBodyBytes;

    /// <summary>Maximum accepted size of the response status line and headers in bytes.</summary>
    public int MaxResponseHeaderBytes { get; init; } = DefaultMaxResponseHeaderBytes;

    /// <summary>
    /// Impersonation level granted to the pipe server. Defaults to <see cref="TokenImpersonationLevel.Identification"/>,
    /// which lets the broker identify the client without acting on its behalf.
    /// <see cref="TokenImpersonationLevel.None"/> keeps the Windows default, which allows full impersonation.
    /// Ignored on platforms other than Windows.
    /// </summary>
    public TokenImpersonationLevel ImpersonationLevel { get; init; } = TokenImpersonationLevel.Identification;

    /// <summary>
    /// Verify, after connecting and before sending any request data, that the pipe server process is the
    /// package broker service. Enabled by default; disable it only for development or test brokers.
    /// </summary>
    /// <remarks>
    /// The server process reported by the kernel for the connected pipe must be the running process of one of
    /// <see cref="ServerServiceNames"/>, and that service must be configured to run as LocalSystem. This works for
    /// standard users. When the caller can also open the server process token (elevated callers), its user must be
    /// LocalSystem. When <see cref="ServerServiceNames"/> is empty, only the token check applies, and it must succeed,
    /// which requires an elevated caller. When the caller can open the server process, the handle is held until the
    /// exchange completes. Verification is only supported on Windows; on other platforms it fails closed.
    /// </remarks>
    public bool VerifyServer { get; init; } = true;

    /// <summary>Windows services allowed to host the package broker when <see cref="VerifyServer"/> is enabled.</summary>
    public IReadOnlyList<string> ServerServiceNames { get; init; } = [AgentServiceName, AgentManualServiceName];

    /// <summary>
    /// Optional additional server authentication, run after <see cref="VerifyServer"/> checks and before any
    /// request data is sent. Throwing rejects the server; the returned value, if any, is disposed once the
    /// exchange completes.
    /// </summary>
    public BrokerPipeServerAuthenticator? ServerAuthenticator { get; init; }

    /// <summary>
    /// Maximum time allowed for <see cref="ServerAuthenticator"/>. The broker closes connections that do not
    /// send a complete request header within a few seconds, so this should stay well below that limit.
    /// </summary>
    public TimeSpan ServerAuthenticatorTimeout { get; init; } = DefaultServerAuthenticatorTimeout;

    /// <summary>
    /// Number of additional attempts, each over a new connection, after the broker replies
    /// <c>503 Service Unavailable</c> with a <c>Retry-After</c> header, or closes the connection without sending any
    /// response byte. A closed connection is retried only when the request did not fully reach the broker, or when the
    /// request has no side effects (<c>GET</c>, <c>HEAD</c>, evaluation, status and policy validation requests).
    /// Set to zero to disable retries.
    /// </summary>
    /// <remarks>
    /// Connection attempts while the pipe is busy or temporarily missing are already retried until
    /// <see cref="ConnectTimeout"/> elapses; a connect timeout is not retried.
    /// </remarks>
    public int MaxBusyRetries { get; init; } = DefaultMaxBusyRetries;

    /// <summary>
    /// Upper bound of the delay honored from a <c>Retry-After</c> header, and of the exponential backoff
    /// (starting at 100 ms) used after the broker closes a connection without responding.
    /// </summary>
    public TimeSpan MaxBusyRetryDelay { get; init; } = DefaultMaxBusyRetryDelay;

    /// <summary>
    /// Error kind reported when the broker closes the connection after starting, but before completing, its response.
    /// Defaults to <see cref="BrokerClientErrorKind.InvalidResponse"/>. A connection closed before any response byte
    /// is reported as <see cref="BrokerClientErrorKind.BrokerUnavailable"/> once retries are exhausted.
    /// </summary>
    public BrokerClientErrorKind IncompleteResponseErrorKind { get; init; } = BrokerClientErrorKind.InvalidResponse;

    internal string ResolvedPipeName => string.IsNullOrWhiteSpace(PipeName) ? BrokerApi.DefaultPipeName : PipeName;

    internal void Validate()
    {
        ThrowIfNotPositive(ConnectTimeout, nameof(ConnectTimeout));
        ThrowIfNotPositive(ResponseTimeout, nameof(ResponseTimeout));
        ThrowIfNotPositive(ServerAuthenticatorTimeout, nameof(ServerAuthenticatorTimeout));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxResponseBodyBytes, nameof(MaxResponseBodyBytes));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxResponseHeaderBytes, 16, nameof(MaxResponseHeaderBytes));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBusyRetries, nameof(MaxBusyRetries));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBusyRetryDelay, TimeSpan.Zero, nameof(MaxBusyRetryDelay));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxBusyRetryDelay, MaxTimerDelay, nameof(MaxBusyRetryDelay));
        ArgumentNullException.ThrowIfNull(ServerServiceNames, nameof(ServerServiceNames));

        if (!Enum.IsDefined(ImpersonationLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(ImpersonationLevel), ImpersonationLevel, "Unknown impersonation level.");
        }

        if (!Enum.IsDefined(IncompleteResponseErrorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(IncompleteResponseErrorKind), IncompleteResponseErrorKind, "Unknown error kind.");
        }

        foreach (var serviceName in ServerServiceNames)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                throw new ArgumentException("Server service names must not be empty.", nameof(ServerServiceNames));
            }
        }
    }

    private static void ThrowIfNotPositive(TimeSpan value, string name)
    {
        if (value == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        if (value <= TimeSpan.Zero || value > MaxTimerDelay)
        {
            throw new ArgumentOutOfRangeException(name, value, "The timeout must be positive and supported by timers, or infinite.");
        }
    }

    /// <summary>Largest delay accepted by <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> and <see cref="Task.Delay(TimeSpan)"/>.</summary>
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(int.MaxValue);
}