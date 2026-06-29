# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [[0.1.0](https://github.com/Devolutions/now-libraries/releases/tag/now-policy-server-template-v0.1.0)] - 2026-06-29

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


