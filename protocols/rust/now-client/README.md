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

Run and detached requests are submission-only. Gateway may emit an immediate
Started/Data/Result sequence for Run; the client records and discards those matching
frames so they cannot affect the next tracked operation. This recent-session quarantine
is bounded and evicts its oldest entry; evicted Run traffic remains harmless because
session IDs are never reused. This crate deliberately does not provide Abort, Shell
submission, DVC/pipe setup, reconnect policy, or retained operation output.
