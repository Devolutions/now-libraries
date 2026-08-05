# Package broker event channel protocol

Version: **1.0**

This document specifies the `NOW_BROKER` frame protocol carried over a
per-operation event channel between a Devolutions NOW package broker (server)
and a broker client.

Reference implementations:

- Rust: `policies/rust/now-policy-api/src/event_channel.rs`
- .NET: `policies/dotnet/Devolutions.Now.Policy.Api/EventChannel.cs`
- Shared test fixture: `policies/rust/now-policy-server-template/assets/samples/frames/event-channel.frames.bin`

## Purpose

For each executed operation the broker (when it supports event channels) opens
a dedicated event channel and returns its descriptor in the
`ExecutionResponse` (`Operation.EventChannel`):

```json
"EventChannel": {
  "Kind": "LocalPipe",
  "Path": "Devolutions.Now.PackageBroker.Operation.op-000001"
}
```

`Kind` is extendable; `LocalPipe` (a local named pipe) is the only transport
currently defined. `Path` is the transport-specific address the client
connects to.

The channel is used to:

- push both stdout and stderr data over a single channel, greatly simplifying
  client code and preserving the correct sequential order of interleaved
  stdout/stderr output — output data frames are only sent when the execute
  request opted in via `CaptureOutput`;
- eliminate periodic status polling: the client sends `StatusRequest` HTTP
  queries only when notified that something actually changed;
- enforce correct UTF-8 character boundaries mid-transfer.

## Transport properties

- **One-way**: read-only from the client side. The client never writes.
- **Minimal overhead**: fixed 6-byte header, no encoding of payload bytes.
- **Extendable**: new frame kinds may be added down the line; decoders MUST
  ignore frames with unknown kinds.

## Frame layout

All integers are **little-endian**.

```text
NOW_BROKER_FRAME
| u32 frame_size | u16 frame_kind | [frame_size; u8] frame_body |
```

- `frame_size` is the body length in bytes; it excludes the 6-byte header.
- `frame_size` MUST NOT exceed **65536** (64 KiB). Producers MUST split larger
  output into multiple frames before encoding (the reference `Encode`
  implementations reject oversized bodies rather than splitting them);
  decoders MUST treat a larger value as a fatal
  protocol error and close the channel.
- A decode error (oversized frame, malformed body, invalid UTF-8) is not
  recoverable: the client SHOULD close the channel and fall back to
  HTTP status queries.
- End-of-stream in the middle of a frame means the stream was truncated
  (e.g. the broker crashed or the pipe broke); clients SHOULD treat this as
  an error rather than a graceful close.

## Frame kinds

| Kind     | Name                        | Body size | Direction       |
| -------- | --------------------------- | --------- | --------------- |
| `0x0000` | `NOW_BROKER_HELLO`          | 4         | server → client |
| `0x0001` | `NOW_BROKER_STATUS_UPDATED` | 0         | server → client |
| `0x0002` | `NOW_BROKER_FINISH`         | 0         | server → client |
| `0x0003` | `NOW_BROKER_STDOUT`         | variable  | server → client |
| `0x0004` | `NOW_BROKER_STDERR`         | variable  | server → client |
| `0x0005` | `NOW_BROKER_STDOUT_OVERFLOW`| 4         | server → client |
| `0x0006` | `NOW_BROKER_STDERR_OVERFLOW`| 4         | server → client |

### NOW_BROKER_HELLO (0x0000)

Sent as the first frame on the channel; acknowledges to the client that the
transport is ready and advertises the protocol version.

```text
| frame_size = 4 | frame_kind = 0x0000 | u16 version_major = 1 | u16 version_minor = 0 |
```

Clients MUST reject channels whose `version_major` they do not support.
`version_minor` increments are backward compatible (new frame kinds only).

### NOW_BROKER_STATUS_UPDATED (0x0001)

Sent when the operation status has changed and awaits being queried via a
`StatusRequest` HTTP query. This frame deliberately omits any status
information: complex data is queried over the main HTTP (pipe) API.

```text
| frame_size = 0 | frame_kind = 0x0001 |
```

### NOW_BROKER_FINISH (0x0002)

Sent when the operation is finished; the client should call `StatusRequest` to
query more info. The channel can be gracefully closed by the client after this
frame. No further frames follow.

```text
| frame_size = 0 | frame_kind = 0x0002 |
```

### NOW_BROKER_STDOUT (0x0003)

Sent when new stdout data is available. The body is UTF-8 encoded data;
character boundaries are guaranteed by the broker side — a multi-byte UTF-8
character is never split across frames.

```text
| frame_size = variable | frame_kind = 0x0003 | [frame_size; u8] data |
```

### NOW_BROKER_STDERR (0x0004)

Same as `NOW_BROKER_STDOUT` but for stderr.

```text
| frame_size = variable | frame_kind = 0x0004 | [frame_size; u8] data |
```

### NOW_BROKER_STDOUT_OVERFLOW (0x0005)

Sent when the client was too slow to read stdout and some data was truncated.

```text
| frame_size = 4 | frame_kind = 0x0005 | u32 bytes_skipped |
```

### NOW_BROKER_STDERR_OVERFLOW (0x0006)

Same as `NOW_BROKER_STDOUT_OVERFLOW` but for stderr.

```text
| frame_size = 4 | frame_kind = 0x0006 | u32 bytes_skipped |
```

## Typical session

```text
server → HELLO (1.0)
server → STDOUT "Resolving package…\n"
server → STDOUT "Downloading…\n"
server → STATUS_UPDATED            (client issues StatusRequest over HTTP)
server → STDERR "warning: …\n"
server → STDOUT_OVERFLOW 4096      (client was too slow; 4096 bytes lost)
server → STDOUT "Installed.\n"
server → STATUS_UPDATED
server → FINISH                    (client issues final StatusRequest, closes pipe)
```

## Versioning and extension rules

- New frame kinds are added with new `frame_kind` values and a
  `version_minor` bump; decoders MUST skip unknown kinds (the header is
  sufficient to do so).
- Changing the layout of an existing frame kind requires a `version_major`
  bump.
- The 64 KiB body limit is part of the protocol contract and does not change
  within major version 1.
