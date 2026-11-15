using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;

using Devolutions.Now.Policy.Api;

using Microsoft.Win32.SafeHandles;

namespace Devolutions.Now.Policy.Client;

/// <summary>Package broker transport using HTTP/1.1 over a Windows named pipe.</summary>
/// <remarks>
/// Each request uses a new connection. On Windows, the pipe is opened with read and write-data access only and
/// grants the server identification-level impersonation by default, and the server is verified before any
/// request data is sent. Busy replies are retried. See <see cref="NamedPipeBrokerTransportOptions"/>.
/// </remarks>
public sealed class NamedPipeBrokerTransport : IBrokerTransport
{
    /// <summary>Access requested on the broker pipe: generic read access and write-data access only.</summary>
    /// <remarks>
    /// <see cref="PipeAccessRights.Read"/> with <see cref="PipeAccessRights.Synchronize"/> is <c>FILE_GENERIC_READ</c>.
    /// Write-attributes access is not needed because the pipe read mode is never changed, and generic write access
    /// is avoided because it includes the right to create pipe instances.
    /// </remarks>
    internal const PipeAccessRights BrokerPipeAccessRights =
        PipeAccessRights.Read | PipeAccessRights.Synchronize | PipeAccessRights.WriteData;

    private readonly NamedPipeBrokerTransportOptions _options;
    private readonly string _pipeName;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Create a transport with default options for <paramref name="pipeName"/>.</summary>
    public NamedPipeBrokerTransport(string? pipeName = null)
        : this(new NamedPipeBrokerTransportOptions { PipeName = pipeName })
    {
    }

    /// <summary>Create a transport with the given options.</summary>
    public NamedPipeBrokerTransport(NamedPipeBrokerTransportOptions options)
        : this(options, Task.Delay)
    {
    }

    internal NamedPipeBrokerTransport(NamedPipeBrokerTransportOptions options, Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options with { ServerServiceNames = [.. options.ServerServiceNames] };
        _pipeName = options.ResolvedPipeName;
        _delay = delay;
    }

    public Transport Kind => Transport.HttpNamedPipe;

    /// <summary>Optional diagnostic sink; receives human-readable trace lines.</summary>
    public Action<string>? Trace { get; init; }

    public async Task<BrokerTransportResponse> Send(BrokerTransportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        for (var attempt = 0; ; attempt++)
        {
            BrokerHttpResponse response;
            try
            {
                response = await SendOnce(request, cancellationToken).ConfigureAwait(false);
            }
            catch (BrokerConnectionClosedException ex)
            {
                // An overloaded broker may close a connection right after accepting it. Resend only when the broker
                // cannot have processed the request, or when processing it again has no side effects.
                if (attempt < _options.MaxBusyRetries && (!ex.RequestSent || BrokerBusyRetry.IsSafeToResend(request)))
                {
                    var retryDelay = BrokerBusyRetry.GetDisconnectRetryDelay(attempt, _options.MaxBusyRetryDelay);
                    Trace?.Invoke($"Package broker closed the connection without responding; retrying {request.Path} in {retryDelay.TotalMilliseconds:0} ms.");
                    await _delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new BrokerClientException(
                    BrokerClientErrorKind.BrokerUnavailable,
                    $"The package broker closed the connection without responding to {request.Path}.",
                    request.Path,
                    innerException: ex.InnerException);
            }

            if (attempt < _options.MaxBusyRetries
                && BrokerBusyRetry.GetRetryDelay(response, _options.MaxBusyRetryDelay, DateTimeOffset.UtcNow) is { } delay)
            {
                Trace?.Invoke($"Package broker is busy; retrying {request.Path} in {delay.TotalMilliseconds:0} ms.");
                await _delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return new BrokerTransportResponse { StatusCode = response.StatusCode, Body = response.Body };
        }
    }

    public void Dispose()
    {
        // No persistent resources to dispose.
    }

    private async Task<BrokerHttpResponse> SendOnce(BrokerTransportRequest request, CancellationToken cancellationToken)
    {
        BrokerPipeConnection? connection = null;
        try
        {
            connection = await Connect(request.Path, cancellationToken).ConfigureAwait(false);

            using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseCts.CancelAfter(_options.ResponseTimeout);

            await BrokerHttp.WriteRequest(connection.Pipe, request, responseCts.Token).ConfigureAwait(false);
            return await BrokerHttp.ReadResponse(
                connection.Pipe,
                request.Path,
                _options.MaxResponseHeaderBytes,
                _options.MaxResponseBodyBytes,
                _options.IncompleteResponseErrorKind,
                responseCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.Timeout,
                $"Timed out communicating with the package broker at {request.Path}.",
                request.Path,
                innerException: ex);
        }
        catch (IOException ex)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.BrokerUnavailable,
                $"Unable to communicate with the package broker at {request.Path}.",
                request.Path,
                innerException: ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BrokerClientException(
                BrokerClientErrorKind.BrokerUnavailable,
                $"Access to the package broker pipe was denied while calling {request.Path}.",
                request.Path,
                innerException: ex);
        }
        finally
        {
            connection?.Dispose();
        }
    }

    /// <summary>Connect to the broker pipe, verify its server, and run the optional server authenticator.</summary>
    private async Task<BrokerPipeConnection> Connect(string endpoint, CancellationToken cancellationToken)
    {
        var pipe = CreatePipe();
        SafeProcessHandle? serverProcess = null;
        var ownershipTransferred = false;
        try
        {
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(_options.ConnectTimeout);
                await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);
            }

            int? serverProcessId = null;
            if (OperatingSystem.IsWindows())
            {
                (serverProcessId, serverProcess) = InspectServer(pipe, endpoint);
            }
            else if (_options.VerifyServer)
            {
                throw BrokerServerVerifier.Failure(
                    "server verification is only supported on Windows; disable VerifyServer to use a development broker",
                    endpoint);
            }

            IDisposable? authentication = null;
            if (_options.ServerAuthenticator is { } authenticator)
            {
                var context = new BrokerPipeServerContext(pipe, _pipeName, endpoint, serverProcessId, serverProcess);
                authentication = await Authenticate(authenticator, context, endpoint, () => ownershipTransferred = true, cancellationToken)
                    .ConfigureAwait(false);
            }

            var connection = new BrokerPipeConnection(pipe, serverProcess, authentication);
            ownershipTransferred = true;
            return connection;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                serverProcess?.Dispose();
                pipe.Dispose();
            }
        }
    }

    private NamedPipeClientStream CreatePipe()
    {
        if (OperatingSystem.IsWindows())
        {
            return new NamedPipeClientStream(
                ".",
                _pipeName,
                BrokerPipeAccessRights,
                PipeOptions.Asynchronous,
                _options.ImpersonationLevel,
                HandleInheritability.None);
        }

        return new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    [SupportedOSPlatform("windows")]
    private (int? ServerProcessId, SafeProcessHandle? ServerProcess) InspectServer(NamedPipeClientStream pipe, string endpoint)
    {
        int serverProcessId;
        SafeProcessHandle? serverProcess;
        try
        {
            serverProcessId = WindowsBrokerServerInspector.GetServerProcessId(pipe.SafePipeHandle);
            serverProcess = WindowsBrokerServerInspector.TryOpenServerProcess(serverProcessId);
        }
        catch (Win32Exception ex) when (WindowsBrokerServerInspector.IsPipeDisconnected(ex.NativeErrorCode))
        {
            // The broker closed the connection right after accepting it; nothing was sent yet.
            throw new BrokerConnectionClosedException(requestSent: false, ex);
        }
        catch (Win32Exception ex)
        {
            if (_options.VerifyServer)
            {
                throw BrokerServerVerifier.Failure("the pipe server process could not be determined", endpoint, ex);
            }

            return (null, null);
        }

        if (!_options.VerifyServer)
        {
            return (serverProcessId, serverProcess);
        }

        try
        {
            BrokerServerVerifier.Verify(
                serverProcessId,
                _options.ServerServiceNames,
                new WindowsBrokerServerInspector(serverProcess),
                endpoint);
            return (serverProcessId, serverProcess);
        }
        catch (Win32Exception ex)
        {
            serverProcess?.Dispose();
            throw BrokerServerVerifier.Failure("the pipe server process or service could not be queried", endpoint, ex);
        }
        catch
        {
            serverProcess?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Run the authenticator with a deadline. When the transport stops waiting for it, ownership of the pipe, the
    /// server process handle and the late authentication result moves to a continuation that disposes them.
    /// </summary>
    private async Task<IDisposable?> Authenticate(
        BrokerPipeServerAuthenticator authenticator,
        BrokerPipeServerContext context,
        string endpoint,
        Action transferOwnership,
        CancellationToken cancellationToken)
    {
        var authenticationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authenticationCts.CancelAfter(_options.ServerAuthenticatorTimeout);
        var authenticationToken = authenticationCts.Token;

        // Run on the thread pool so that an authenticator blocking before its first await is bounded too.
        var authentication = Task.Run(() => authenticator(context, authenticationToken).AsTask(), CancellationToken.None);

        try
        {
            var result = await authentication.WaitAsync(authenticationCts.Token).ConfigureAwait(false);
            authenticationCts.Dispose();
            return result;
        }
        catch (OperationCanceledException) when (authenticationToken.IsCancellationRequested)
        {
            transferOwnership();
            _ = authentication.ContinueWith(
                static (completed, state) =>
                {
                    var (cts, ctx) = ((CancellationTokenSource, BrokerPipeServerContext))state!;
                    try
                    {
                        if (completed.IsCompletedSuccessfully)
                        {
                            completed.Result?.Dispose();
                        }
                        else
                        {
                            // Observe the late failure; the caller already received a timeout or cancellation.
                            _ = completed.Exception;
                        }
                    }
                    catch
                    {
                        // The caller is gone; a failure disposing the late result must not surface as an unobserved exception.
                    }
                    finally
                    {
                        ctx.ServerProcess?.Dispose();
                        ctx.Pipe.Dispose();
                        cts.Dispose();
                    }
                },
                (authenticationCts, context),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            cancellationToken.ThrowIfCancellationRequested();
            throw new BrokerClientException(
                BrokerClientErrorKind.Timeout,
                $"Timed out verifying the package broker pipe server for {endpoint}.",
                endpoint);
        }
        catch (Exception ex)
        {
            authenticationCts.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw AuthenticationFailure(ex, endpoint);
        }
    }

    private static BrokerClientException AuthenticationFailure(Exception ex, string endpoint) =>
        ex as BrokerClientException
        ?? BrokerServerVerifier.Failure("the server authenticator rejected the pipe server", endpoint, ex);

    private sealed class BrokerPipeConnection(NamedPipeClientStream pipe, SafeProcessHandle? serverProcess, IDisposable? authentication)
        : IDisposable
    {
        public NamedPipeClientStream Pipe { get; } = pipe;

        public void Dispose()
        {
            try
            {
                authentication?.Dispose();
            }
            finally
            {
                serverProcess?.Dispose();
                Pipe.Dispose();
            }
        }
    }
}