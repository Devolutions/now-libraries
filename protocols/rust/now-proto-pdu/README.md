NOW-proto PDU encoding/decoding library
=======================================

This crate provides a Rust implementation of the NOW protocol encoding/decoding library.

## Library architecture details

- The library only provides encoding/decoding functions for the NOW protocol, transport layer
  and session processing logic should be implemented by the client/server.
- `#[no_std]` compatible. Requires `alloc`.
- PDUs could contain borrowed data by default to avoid unnecessary string/vec allocations when
  parsing. The only exception is error types which are `'static` and may allocate if optional
  message is set.
- PDUs are immutable by default. PDU constructors take only required fields, optional fields are
  set using implicit builder pattern (consuming `.with_*` and `.set_*` methods).
- User-facing `bitfield` types should be avoied in the public API if incorrect usage could lead to
  invalid PDUs. E.g. `ExecData`'s stdio stream flags are represented as a bitfield, but exactly
  one of the flags should be set at a time. The public API should provide a safe way to set and
  retrieve these flags. Channel capabilities flags on the other hand could all be set independently,
  therefore it is safe to expose them in the public API.
- Primitive protocol types e.g `NOW_STRING` should not be exposed in the public API.
- Message validition should be always checked in the PDU constructor(s). If the message have
  variable fields, it should be ensured that it could fit into the message body (`u32`).
- PDUs should NOT fail on deserialization if message body have more data to ensure backwards
  compatibility with future protocol versions (e.g. new fields added to the end of the message in
  the new protocol version).

## Versioning

Crate version is not tied to the protocol version (e.g. Introduction of breaking changes in the
crate API does not necessarily mean a protocol version bump and vice versa). The currently
implemented protocol version is stored in [`NowProtoVersion::CURRENT`] and should be updated
accordingly when the protocol is updated.
