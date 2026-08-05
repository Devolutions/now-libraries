# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [[0.3.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.2.0...now-policy-api-v0.3.0)] - 2026-08-05

### <!-- 1 -->Features

- Add package operation cancelation support ([#88](https://github.com/Devolutions/now-libraries/issues/88)) ([d8ba0f8515](https://github.com/Devolutions/now-libraries/commit/d8ba0f85153f2d1c54953a4c2f7dd4e6a37e5448)) 

- Add per-operation event channel protocol ([#89](https://github.com/Devolutions/now-libraries/issues/89)) ([d25e7eb631](https://github.com/Devolutions/now-libraries/commit/d25e7eb631b1f6c00ef1ee27c5b1d16a1381d298)) 

  ## Summary
  
  Adds a **per-operation event channel** for delivering operation output
  and status-change notifications from the broker to the client, replacing
  HTTP polling for output entirely.
  
  For each executed operation the broker (when it supports event channels)
  opens a dedicated channel and returns an expandable `EventChannel`
  descriptor in the execution response:
  
  ```json
  "EventChannel": {
    "Kind": "LocalPipe",
    "Path": "Devolutions.Now.PackageBroker.Operation.op-…"
  }
  ```
  
  The channel always carries status-change notifications; the
  `CaptureOutput` request flag only controls whether stdout/stderr data
  frames are pushed over it.
  
  The client connects to the pipe and reads a minimal one-way binary frame
  protocol (`NOW_BROKER` frames):
  
  | Frame | Kind | Body |
  |---|---|---|
  | Hello | 0x0000 | `u16` version major + `u16` version minor |
  | StatusUpdated | 0x0001 | empty — client should issue a `GetStatus`
  HTTP request |
  | Finish | 0x0002 | empty — operation finished, pipe can be closed |
  | Stdout | 0x0003 | UTF-8 data (agent ensures char boundaries) |
  | Stderr | 0x0004 | UTF-8 data |
  | StdoutOverflow | 0x0005 | `u32` bytes skipped |
  | StderrOverflow | 0x0006 | `u32` bytes skipped |
  
  Frame layout is `u32 body_size | u16 kind | body` (little-endian, 64 KiB
  body cap). Decoders ignore unknown frame kinds, keeping the protocol
  extendable; end-of-stream mid-frame is treated as a truncated-stream
  error. Full spec: `policies/docs/event-channel-protocol.md`.
  
  ## Changes
  
  - **Rust** (`now-policy-api`): new `event_channel` module —
  `EventChannel` descriptor model, `EventFrame` enum,
  `encode`/`decode_body`, incremental `EventFrameDecoder`;
  `OperationSubmission.event_channel` field.
  - **.NET** (`Devolutions.Now.Policy.Api`): `EventChannel.cs` — mirrored
  DTO, `EventFrame` hierarchy, `EventFrameDecoder`.
  - **.NET client** (`Devolutions.Now.Policy.Client`):
  `BrokerClient.OpenEventChannel(ExecutionResponse)` connects to the
  advertised pipe and returns an `OperationEventChannel` reader —
  `ReadFrame()` for the raw frame stream, `ReadEvents()`
  (`IAsyncEnumerable`) which skips unknown frames and completes after
  `Finish` or EOF.
  - Shared binary fixture
  (`assets/samples/frames/event-channel.frames.bin`) validated
  byte-for-byte by both Rust and .NET test suites, including unknown-frame
  tolerance; .NET integration tests exercise a real local named pipe,
  including pipe-closure and mid-frame truncation cases.
  - OpenAPI schema regenerated; docs/READMEs updated.
  
  No crate/package or protocol version bumps (API stays `1.0`;
  pre-release).
  
  ---------



## [[0.2.0](https://github.com/Devolutions/now-libraries/compare/now-policy-api-v0.1.0...now-policy-api-v0.2.0)] - 2026-07-27

### <!-- 1 -->Features

- [**breaking**] Add 14 new package manager variants to `ManagerName` (Apt, Bun, Cargo, Chocolatey, Dnf, Dotnet, Flatpak, Homebrew, Npm, Pacman, Pip, Scoop, Snap, Vcpkg) ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))
- Add `CapabilitiesResponse::manager_capability` and `CapabilitiesResponse::supports_manager` helpers for querying advertised broker capabilities ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))
- Extend `policy-compat` bidirectional `ManagerName` conversions to cover the new package managers ([#84](https://github.com/Devolutions/now-libraries/issues/84)) ([7157ff6d25](https://github.com/Devolutions/now-libraries/commit/7157ff6d252417afed41521b59f73f44b5433d66))

## [[0.1.0](https://github.com/Devolutions/now-libraries/releases)] - 2026-06-30

### <!-- 1 -->Features

- Initial release of the implementation-agnostic Devolutions NOW policy package broker API model
