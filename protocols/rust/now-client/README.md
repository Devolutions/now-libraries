# NOW client

`now-client` is a high-level, transport-agnostic Rust client for the NOW execution
channel. It accepts a caller-provided Tokio `AsyncRead + AsyncWrite` byte stream;
consumers create DVCs, pipes, sockets, and replacement clients after reconnects.

Connect with `NowClient::connect`, then query negotiated `NowCapabilities` from the
returned handle. `process`, `batch`, `win_ps`, and `pwsh` submit tracked executions;
their `_detached` counterparts submit detached executions. `run` is submission-only.
The client defensively negotiates capabilities, applies bounded frame/command/event
queues, and permits **one tracked execution at a time per stream**. Tracked operations
expose raw `Vec<u8>` stdout/stderr chunks, stdin forwarding, normal cancellation, and
terminal status.

Use `Execution::cancel_handle()` to obtain an owned, cloneable `ExecutionCancelHandle`.
Its `cancel()` method waits for the peer's cancellation response while the caller continues polling `Execution::next_event()`.
Queue acceptance is not cancellation success: a peer rejection returns `NowClientError::RemoteStatus`, leaves the execution tracked, and permits another cancellation attempt.
After acceptance, use `Execution::wait()` to observe the terminal result.
The existing `Execution::cancel()` remains available when concurrent event polling is not needed.

`session_lock` and `session_logoff` ask the peer to lock or log off the interactive user
session that hosts it. They require the matching negotiated session capability, so a custom
`client_capset` must advertise `NowSessionCapsetFlags::LOCK` or `LOGOFF`; otherwise they
fail with `UnsupportedCapability` without writing anything. Neither message has a response:
they complete once the request is written, so callers confirm a logoff by observing the
end of the session that carries the channel.

Run and detached requests are submission-only. Gateway may emit an immediate
Started/Data/Result sequence for Run; the client records and discards those matching
frames so they cannot affect the next tracked operation. This recent-session quarantine
is bounded and evicts its oldest entry; evicted Run traffic remains harmless because
session IDs are never reused. This crate deliberately does not provide Abort, Shell
submission, DVC/pipe setup, reconnect policy, or retained operation output.
