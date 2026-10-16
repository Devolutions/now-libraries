namespace Devolutions.NowClient.Tests;

internal sealed class ClientPeer : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task<Client>? _connection;
    private Client? _client;
    private bool _dedicated;

    public WireTransport Wire { get; } = new();
    public Client Client => _client ?? throw new InvalidOperationException("Connect first.");

    public Task<Client> BeginConnect(bool dedicated = true, TimeSpan? timeout = null,
        NowClientExecution? execution = null)
    {
        _dedicated = dedicated && execution == null;
        _connection = Client.Connect(Wire, new NowClientOptions
        {
            Scheduling = execution != null
                ? NowClientScheduling.SharedThread(execution)
                : _dedicated ? NowClientScheduling.DedicatedThread : NowClientScheduling.ThreadPool,
            ConnectTimeout = timeout ?? TimeSpan.FromSeconds(10),
        }, _lifetime.Token);
        return _connection;
    }

    public Client Connect(bool dedicated = true, TimeSpan? heartbeat = null, bool legacyOverload = false,
        NowClientExecution? execution = null)
    {
        var connection = legacyOverload
            ? _connection = Client.Connect(Wire)
            : BeginConnect(dedicated, execution: execution);
        var offer = Wire.Receive<NowMsgChannelCapset>();
        Assert.Equal(NowCapabilitySystem.All, offer.SystemCapset);
        Assert.Equal(NowCapabilitySession.All, offer.SessionCapset);
        Assert.Equal(NowCapabilityExec.All, offer.ExecCapset);
        Wire.SendFragmented(Capabilities(heartbeat));
        return FinishConnect(connection);
    }

    public Client FinishConnect(Task<Client> connection)
    {
        _client = TestDriver.Success(connection);
        Wire.WaitForPendingRead();
        return _client;
    }

    public void Cancel() => _lifetime.Cancel();

    public static NowMsgChannelCapset Capabilities(TimeSpan? heartbeat = null)
    {
        var builder = new NowMsgChannelCapset.Builder()
            .SystemCapset(NowCapabilitySystem.All)
            .SessionCapset(NowCapabilitySession.All)
            .ExecCapset(NowCapabilityExec.All);
        if (heartbeat != null)
            builder.HeartbeatInterval(heartbeat.Value);
        return builder.Build();
    }

    public static NowMsgRdmCapabilities RdmCapabilities() =>
        new NowMsgRdmCapabilities.Builder(1672531200, "2026.3.1.0")
            .VersionExtra("integration")
            .WithAppAvailable()
            .Build();

    public void AssertStopped(bool successful = true)
    {
        TestDriver.Completed(Client.Completion);
        if (successful)
            TestDriver.Success(Client.Completion);
        Assert.True(Wire.IsDisposed);
        Assert.Equal(1, Wire.DisposeCalls);
        Assert.True(Wire.AllOperationsCompleted);
        if (_dedicated)
            TestDriver.ThreadExited(Wire.AssertDedicatedThread());
    }

    public void Dispose()
    {
        try
        {
            _lifetime.Cancel();
            if (_client != null)
            {
                TestDriver.Completed(_client.StopAsync());
                _ = _client.Completion.Exception;
                TestDriver.Completed(_client.DisposeAsync().AsTask());
            }
            else if (_connection != null)
            {
                if (!Wire.IsDisposed)
                    Wire.Dispose();
                TestDriver.Completed(_connection);
                if (_connection.IsCompletedSuccessfully)
                {
                    _client = _connection.Result;
                    TestDriver.Completed(_client.DisposeAsync().AsTask());
                }
                _ = _connection.Exception;
            }
        }
        finally
        {
            if (!Wire.IsDisposed)
                Wire.Dispose();
            _lifetime.Dispose();
            if (_dedicated && Wire.Calls.TryPeek(out var first))
                TestDriver.ThreadExited(first.Thread);
        }
    }
}