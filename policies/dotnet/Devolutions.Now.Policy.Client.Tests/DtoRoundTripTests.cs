using NJsonSchema;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

/// <summary>
/// Parity tests: the hand-written C# DTOs must consume the exact sample documents the
/// Rust crate uses, and re-serialize to output that still validates against the same
/// schemas. Uses source-generated strict metadata so a sample field missing from a DTO
/// fails the test (DTO completeness), mirroring the Rust `deny_unknown_fields` contract.
/// </summary>
public class DtoRoundTripTests
{
    [Theory]
    [MemberData(nameof(TestData.RequestSamples), MemberType = typeof(TestData))]
    public async Task PackageRequest_round_trips_and_validates(string path)
        => await AssertRoundTrip<PackageRequest>(path, await TestData.SchemaAsync("PackageRequest"));

    [Theory]
    [MemberData(nameof(TestData.StatusRequestSamples), MemberType = typeof(TestData))]
    public async Task StatusRequest_round_trips_and_validates(string path)
        => await AssertRoundTrip<StatusRequest>(path, await TestData.SchemaAsync("StatusRequest"));

    [Theory]
    [MemberData(nameof(TestData.ResponseSamples), MemberType = typeof(TestData))]
    public async Task EvaluationResponse_round_trips_and_validates(string path)
        => await AssertRoundTrip<EvaluationResponse>(path, await TestData.SchemaAsync("EvaluationResponse"));

    [Theory]
    [MemberData(nameof(TestData.ExecutionResponseSamples), MemberType = typeof(TestData))]
    public async Task ExecutionResponse_round_trips_and_validates(string path)
        => await AssertRoundTrip<ExecutionResponse>(path, await TestData.SchemaAsync("ExecutionResponse"));

    [Theory]
    [MemberData(nameof(TestData.StatusResponseSamples), MemberType = typeof(TestData))]
    public async Task StatusResponse_round_trips_and_validates(string path)
        => await AssertRoundTrip<StatusResponse>(path, await TestData.SchemaAsync("StatusResponse"));

    [Theory]
    [MemberData(nameof(TestData.HealthResponseSamples), MemberType = typeof(TestData))]
    public async Task HealthResponse_round_trips_and_validates(string path)
        => await AssertRoundTrip<HealthResponse>(path, await TestData.SchemaAsync("HealthResponse"));

    [Theory]
    [MemberData(nameof(TestData.CapabilitiesResponseSamples), MemberType = typeof(TestData))]
    public async Task CapabilitiesResponse_round_trips_and_validates(string path)
        => await AssertRoundTrip<CapabilitiesResponse>(path, await TestData.SchemaAsync("CapabilitiesResponse"));

    private static async Task AssertRoundTrip<T>(string samplePath, JsonSchema schema)
    {
        var original = await File.ReadAllTextAsync(samplePath);

        // 1. Deserialize the canonical sample into the DTO (strict: every field must map).
        var dto = BrokerJson.DeserializeStrict<T>(original);
        Assert.NotNull(dto);

        // 2. Re-serialize and validate the output against the same schema.
        var reserialized = BrokerJson.Serialize(dto);
        var errors = schema.Validate(reserialized);

        Assert.True(
            errors.Count == 0,
            $"Re-serialized {typeof(T).Name} from {Path.GetFileName(samplePath)} failed schema validation:\n" +
            string.Join("\n", errors.Select(e => $"  {e.Kind} at {e.Path}")));
    }
}