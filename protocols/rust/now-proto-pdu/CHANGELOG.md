# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [[0.4.4](https://github.com/Devolutions/now-libraries/compare/now-proto-pdu-v0.4.3...now-proto-pdu-v0.4.4)] - 2026-07-14

### <!-- 1 -->Features

- Implement broker APIs and dotnet client ([#76](https://github.com/Devolutions/now-libraries/issues/76)) ([5d343c5f2e](https://github.com/Devolutions/now-libraries/commit/5d343c5f2e611511762812c61238dfec0699daf2)) 

  ## Summary
  
  Adds the package broker API/client foundation to `now-proto` across Rust
  and .NET.
  
  The main goal is to keep the broker protocol definition
  implementation-agnostic and transport-independent while making Rust the
  source of truth for schema/OpenAPI generation. .NET implementations and
  clients can then validate against the generated contract and stay
  synchronized with the Rust API model.
  
  ## Architecture
  
  ### Rust contract source of truth
  
  `policies/rust/now-policy-api` defines the canonical package broker wire
  model:
  
  - request, response, status, health, capabilities, and error DTOs;
  - package-manager enums and strongly validated newtypes;
  - API version and default broker pipe name constants;
  - fixed `RequestKind` / `ResponseKind` marker types that serialize as
  constant strings and validate discriminator values on deserialization;
  - OpenAPI output under
  `policies/rust/now-policy-api/openapi/now-policy-api.yaml`.
  
  This crate intentionally contains no broker implementation. Its purpose
  is to describe the protocol shape, generate OpenAPI, and provide a
  stable Rust model for tests and server facades.
  
  ### Rust server facade and mock template
  
  `policies/rust/now-policy-server-template` provides the implementation
  boundary around the API crate:
  
  - `PackageBrokerServer` trait as the server-side facade;
  - Axum router/template that maps HTTP endpoints to the facade without
  embedding policy/package-manager logic;
  - fixture-backed mock broker for deterministic request/response testing;
  - sample request, response, and scenario documents derived from the
  Uniget broker shape;
  - OpenAPI generation tool that writes the contract back into the API
  crate.
  
  This keeps real broker behavior out of `now-proto` while still allowing
  the API to be exercised end-to-end.
  
  ### Transport-independent message shape
  
  The protocol now treats request/response identity as part of the body
  schema instead of HTTP metadata:
  
  - `RequestKind` and `RequestVersion` live at the top level of request
  messages.
  - `ResponseKind` and `ResponseVersion` live at the top level of response
  messages.
  
  This keeps the schema usable if the broker later moves away from
  HTTP-over-named-pipe to another transport.
  
  ### .NET API package
  
  `policies/dotnet/Devolutions.Now.Policy.Api` mirrors the Rust schema in
  C#:
  
  - API constants, JSON serialization options, and policy-model
  compatibility conversions are centralized here.
  - Tests validate DTO behavior against the Rust-generated OpenAPI and
  shared sample documents.
  
  This package contains only API types and no transport/client execution
  logic.
  
  ### .NET client package
  
  `policies/dotnet/Devolutions.Now.Policy.Client` contains the actual
  client behavior:
  
  - `BrokerClient` exposes high-level methods for health, capabilities,
  evaluate, execute, and operation status.
  - `BrokerClientOptions` is the single configuration entry point.
  - Client context fields are filled implicitly where possible, including
  effective user, client version, executable path, transport, timestamps,
  and generated request IDs.
  - Public request wrapper types avoid exposing raw wire DTO fields that
  the client owns.
  - `IBrokerTransport` abstracts transport so named pipes are only one
  implementation detail.
  - `NamedPipeBrokerTransport` handles the current HTTP-over-named-pipe
  transport.
  - Capabilities are fetched and cached before operation/status requests
  so unsupported operations fail locally before sending the request.
  
  ### Test and fixture strategy
  
  Shared Rust sample documents are used as contract fixtures for both Rust
  and .NET. The Rust server-template tests verify sample deserialization,
  mock broker behavior, and router/facade wiring. The .NET tests validate
  DTO schema compatibility, fixed discriminator handling, policy enum
  compatibility, fake transport behavior, capability preflight, error
  mapping, and client-generated request metadata.
  
  ## Changes
  
  - Added Rust `now-policy-api` and `now-policy-server-template` crates.
  - Added generated package broker OpenAPI contract.
  - Added shared request/response/scenario samples.
  - Added .NET `Devolutions.Now.Policy.Api` and
  `Devolutions.Now.Policy.Client` projects.
  - Added .NET client/API tests using fake transport and Rust fixture
  validation.
  - Updated Cargo workspace, .NET solution, NuGet publishing workflow, and
  package documentation.



## [[0.4.3](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.4.2...now-proto-pdu-v0.4.3)] - 2026-05-15

### <!-- 1 -->Features

- Add support for utf-8 transcoding and unicode console mode for exec commands ([#62](https://github.com/Devolutions/now-proto/issues/62)) ([1e41deee6a](https://github.com/Devolutions/now-proto/commit/1e41deee6aaad4f2b43e3e1ba6ca6c551b43d622)) 

  This PR updates now proto to v1.6, adding the following:
  
  - `now-proto` now requires agent to implement server-side OEM code page
  transcoding to UTF-8.
  - By default redirected IO for cmd, PowerShell5 and PowerShell7 now
  automatically transcoded to UTF-8. Current RDM implementation already
  expects UTF-8, so when new agent version will be installed, "garbaged
  input" issue [DGW-370](https://devolutions.atlassian.net/browse/DGW-370)
  will be automatically fixed.
  - Note that for `process` execution mode, raw (no-transcoding) is still
  default, but transcoding could be enabled by
  `NOW_EXEC_FLAG_PROCESS_ENCODING_UTF8` flag instead, as `process`
  execution is more advanced use case usually needed for bit-bit output,
  so no explicit transcoding is provided.
  - Added `NOW_CAP_EXEC_UNICODE_CONSOLE` capability to signify that new
  flags are supported and the redirected streams are indeed correct utf-8.
  - [testing] Updated CLI test app for encoding testing purposes



## [[0.4.2](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.4.1...now-proto-pdu-v0.4.2)] - 2025-12-02

### <!-- 1 -->Features

- Add window recording support to protocol and libraries ([#52](https://github.com/Devolutions/now-proto/issues/52)) ([e455c4c6e3](https://github.com/Devolutions/now-proto/commit/e455c4c6e3c06e54fc585c8d6f14c315177dd7cf)) 

  Adds new messages for the current active windows tracking.

## [[0.4.1](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.4.0...now-proto-pdu-v0.4.1)] - 2025-11-18

### <!-- 1 -->Features

- Add detached exec mode (ARC-411) ([#48](https://github.com/Devolutions/now-proto/issues/48)) ([a4ce1b2d16](https://github.com/Devolutions/now-proto/commit/a4ce1b2d163b023e4268b6bb6a0afeaf851e23f5)) 

## [[0.4.0](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.3.2...now-proto-pdu-v0.4.0)] - 2025-09-24

### <!-- 1 -->Features

- Implemented NOW-Proto 1.3 features in rust crate ([b99bbeae0c](https://github.com/Devolutions/now-proto/commit/b99bbeae0cda6f6ee20e0f29b6b36ee9abdd34e9)) 

### <!-- 4 -->Bug Fixes

- Update version numbers in libraries ([7296b6d325](https://github.com/Devolutions/now-proto/commit/7296b6d325df4fc08ca18faa1a4e24a322ba2bb7)) 

## [[0.3.2](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.3.1...now-proto-pdu-v0.3.2)] - 2025-09-11

### <!-- 4 -->Bug Fixes

- Add missing NowExecPwshMsg::is_server_mode method ([27fe1341f8](https://github.com/Devolutions/now-proto/commit/27fe1341f8145316f911cd89f83c223a539bc048)) 



## [[0.3.1](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.3.0...now-proto-pdu-v0.3.1)] - 2025-09-11

### <!-- 1 -->Features

- Add PowerShell server mode flag support ([2177c8ece1](https://github.com/Devolutions/now-proto/commit/2177c8ece131a9e82c545caa9a38769cb6b9267b)) 



## [[0.3.0](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.2.0...now-proto-pdu-v0.3.0)] - 2025-08-20

### <!-- 1 -->Features

- Add IO redirection flags to all exec sessions; add missing working directory option to ShellExecute (#29) ([ce0afe06c4](https://github.com/Devolutions/now-proto/commit/ce0afe06c4d1a9f1750eb0055034fd0b896db407)) 

### <!-- 4 -->Bug Fixes

- Add missing forward-compatibility logic to message decoding (#32) ([0adfc78cfa](https://github.com/Devolutions/now-proto/commit/0adfc78cfa350b3086f6444758d7a5da220c23e8)) 

- [**breaking**] Change incorrect `NowExecRunMsg::directory` method (#33) ([edba71a91e](https://github.com/Devolutions/now-proto/commit/edba71a91ec63735c0aeb3ae839fda3b570d0bc6)) 

## [[0.2.0](https://github.com/Devolutions/now-proto/compare/now-proto-pdu-v0.1.0...now-proto-pdu-v0.2.0)] - 2025-03-14

### <!-- 1 -->Features

- Set keyboard layout functionality (#22) ([31e0c79318](https://github.com/Devolutions/now-proto/commit/31e0c793186d558c0369fe188a2525b99911af30)) 

### <!-- 6 -->Documentation

- Update README.md and uniformize wording (#19) ([17719140e7](https://github.com/Devolutions/now-proto/commit/17719140e7b52b209cda9c17d0ef892cf006f723)) 

