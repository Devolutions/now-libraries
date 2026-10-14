NOW-proto client (C# library)
=====================================

This library provides a high level async client for the NOW protocol.

### NowClient Architecture overview

- Provides async client implementation for NOW-proto remote execution channel.
- Transport layer is detached from the actual underlying transport implementation details via
  `INowTransport` interface.
- All long-running operations (e.g. remote execution session & message box responses) return
  proxy objects which could be used to wait for the operation to complete or send additional
  commands on demand (e.g. cancel execution or send stdin data).
- Client uses background worker task to handle incoming messages both from user code calling client
  and messages coming from the server.
- `NowClient` is thread safe and could be used from multiple
  threads simultaneously (only mpsc `Channel` and a few atomic
  variables are used)

### Dedicated protocol execution

`Connect(transport)` continues to use a thread-pool worker. Applications that need
protocol recovery to remain responsive during thread-pool starvation can opt in:

```csharp
await using var client = await NowClient.Connect(
    transport,
    new NowClientOptions { UseDedicatedThread = true },
    connectionLifetimeToken);

await client.RdmStart();
await client.Completion;
```

Each dedicated connection owns one background thread with an async message pump
and monotonic deadline queue. Negotiation, command processing, response-dependent
commands, heartbeat checks and protocol timeouts execute there, including when an
API is called from a UI thread. `ConnectTimeout` defaults to ten seconds and covers
the initial capability exchange, including its write.

The cancellation token covers the **whole connection lifetime**, not just
negotiation. The client owns and disposes the transport even if connection setup
fails. `Completion` finishes after protocol and transport cleanup and shutdown of
the dedicated pump. A local stop or lifetime cancellation is successful
completion; protocol or transport failures fault the task. `StopAsync()` requests
a local stop, interrupts pending transport waits and returns `Completion`.
`DisposeAsync()` does the same. Neither synchronously joins the protocol thread.
Faults from transport waits abandoned during cancellation are observed even when
the caller has already received the cancellation result.
`ForceTerminate()` retains its existing graceful, queued close behavior; use
`StopAsync()` if a blocked write or unresponsive peer must not delay shutdown.

This isolates **library protocol work**, not arbitrary transport or application
code. `INowTransport.Read`/`Write` must return promptly and their tasks must be
completed without needing an available pool worker (for example, directly from
native DVC callbacks). Their asynchronous task completions may use
`RunContinuationsAsynchronously`. Disposing the transport must return promptly
and release its pending I/O. A transport implemented using `Task.Run`, or an
async I/O provider that itself dispatches through the pool, retains that
dependency.

Notification and execution-output callbacks run synchronously on the protocol
thread: do not block, synchronously invoke a UI dispatcher, or synchronously wait
for another client operation from them. Post work to the consumer instead.
Consumer awaits (including message-box and execution-result proxy awaits) retain
their normal UI/thread-pool scheduling and are not part of the independence
guarantee. Do not synchronously wait for `Completion` inside a protocol callback.

### Keeping an application attach sequence on the same thread

If application work after `Connect` is also recovery-critical, use a caller-owned
`NowClientExecution` rather than awaiting `Connect` from a pool-dependent caller:

```csharp
await using var execution = new NowClientExecution();
var attached = execution.Run(async () =>
{
    var client = await NowClient.Connect(
        transport,
        new NowClientOptions { Execution = execution },
        connectionLifetimeToken);
    await client.SetRdmAppNotifyHandler(OnNotify);
    await client.RdmSync();
    return client;
});
```

The application can poll `attached` from its lifecycle actor without blocking it.
`Run` and `Run<T>` capture the execution context across awaits; `Delay` uses its
monotonic deadline queue. Use that delay rather than `Task.Delay` in critical
application code, and do not use `ConfigureAwait(false)` or `Task.Run` there.
An awaited external task still needs a completion producer independent of the pool.

`Execution` and `UseDedicatedThread` are mutually exclusive. A borrowed client
does not shut down the shared thread when it terminates. Stop/cancel each client
and await its `Completion` **before** disposing the execution. Execution disposal
rejects new `Run` operations and waits for already-admitted operations (including
each client's worker) to finish; it does not forcibly cancel arbitrary application
tasks. `execution.Completion` signals pump shutdown. Never await execution disposal
from an operation whose completion that disposal is waiting for.
