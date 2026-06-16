NOW-proto
=========

Devolutions::Now::Agent RDP virtual channel protocol libraries and clients.

### Specification

Current protocol specification: [read](./docs/NOW-spec.md)

### Updating protocol

In order to update the protocol, the following steps should be followed:

1. Update the protocol specification in `./docs/NOW-spec.md`.
    - Bump the protocol version number.
1. Update the Rust implementation of the protocol in `./rust/now-proto-pdu`.
    - Bump current protocol version defined in `now_proto_pdu::channel::capset::NowProtoVersion::CURRENT`
1. Update the C# implementation of the protocol in `./dotnet/Devolutions.NowProto`
    - Bump current protocol version in `Devolutions.NowProto.NowProtoVersion.Current`
