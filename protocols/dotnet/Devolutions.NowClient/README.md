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
