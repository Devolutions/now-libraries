using NJsonSchema;

using Xunit;

namespace Devolutions.Now.Policy.Client.Tests;

/// <summary>
/// Cross-checks that the bundled sample documents validate against their schemas, and that
/// intentionally-invalid fixtures are rejected. Request/response samples validate against
/// the OpenAPI component schemas. Policy document tests live in the dedicated
/// Devolutions.Now.Policy.Model test project.
/// </summary>
public class SchemaValidationTests
{
    [Fact]
    public async Task Broker_api_version_matches_openapi_and_message_versions()
    {
        var openApiVersion = await TestData.OpenApiVersionAsync();

        Assert.Equal(BrokerApi.Version, openApiVersion);
        Assert.Equal(BrokerApi.PackageRequestKind, new PackageRequest().RequestKind);
        Assert.Equal(BrokerApi.Version, new PackageRequest().RequestVersion);
        Assert.Equal(BrokerApi.StatusRequestKind, new StatusRequest().RequestKind);
        Assert.Equal(BrokerApi.Version, new StatusRequest().RequestVersion);
        Assert.Equal(BrokerApi.EvaluationResponseKind, new EvaluationResponse().ResponseKind);
        Assert.Equal(BrokerApi.Version, new EvaluationResponse().ResponseVersion);
        Assert.Equal(BrokerApi.ExecutionResponseKind, new ExecutionResponse().ResponseKind);
        Assert.Equal(BrokerApi.Version, new ExecutionResponse().ResponseVersion);
        Assert.Equal(BrokerApi.StatusResponseKind, new StatusResponse().ResponseKind);
        Assert.Equal(BrokerApi.Version, new StatusResponse().ResponseVersion);
        Assert.Equal(BrokerApi.HealthResponseKind, new HealthResponse().ResponseKind);
        Assert.Equal(BrokerApi.Version, new HealthResponse().ResponseVersion);
        Assert.Equal(BrokerApi.CapabilitiesResponseKind, new CapabilitiesResponse().ResponseKind);
        Assert.Equal(BrokerApi.Version, new CapabilitiesResponse().ResponseVersion);
        Assert.Equal(BrokerApi.ErrorResponseKind, new ErrorResponse().ResponseKind);
        Assert.Equal(BrokerApi.Version, new ErrorResponse().ResponseVersion);
    }

    [Theory]
    [MemberData(nameof(TestData.RequestSamples), MemberType = typeof(TestData))]
    public async Task Request_samples_are_schema_valid(string path)
        => await AssertValid(path, await TestData.SchemaAsync("PackageRequest"));

    [Theory]
    [MemberData(nameof(TestData.StatusRequestSamples), MemberType = typeof(TestData))]
    public async Task Status_request_samples_are_schema_valid(string path)
        => await AssertValid(path, await TestData.SchemaAsync("StatusRequest"));

    [Theory]
    [MemberData(nameof(TestData.ResponseSamples), MemberType = typeof(TestData))]
    public async Task Response_samples_are_schema_valid(string path)
        => await AssertValid(path, await TestData.SchemaAsync("EvaluationResponse"));

    [Theory]
    [MemberData(nameof(TestData.ExecutionResponseSamples), MemberType = typeof(TestData))]
    public async Task Execution_response_samples_are_schema_valid(string path)
        => await AssertValid(path, await TestData.SchemaAsync("ExecutionResponse"));

    [Theory]
    [MemberData(nameof(TestData.StatusResponseSamples), MemberType = typeof(TestData))]
    public async Task Status_response_samples_are_schema_valid(string path)
        => await AssertValid(path, await TestData.SchemaAsync("StatusResponse"));

    [Theory]
    [MemberData(nameof(TestData.HealthResponseSamples), MemberType = typeof(TestData))]
    public async Task Health_response_samples_are_schema_valid(string path)
        => await AssertValid(path, await TestData.SchemaAsync("HealthResponse"));

    [Theory]
    [MemberData(nameof(TestData.CapabilitiesResponseSamples), MemberType = typeof(TestData))]
    public async Task Capabilities_response_samples_are_schema_valid(string path)
        => await AssertValid(path, await TestData.SchemaAsync("CapabilitiesResponse"));

    [Fact]
    public async Task Invalid_request_is_rejected_by_schema()
    {
        var path = Path.Combine(TestData.SamplesDir, "requests", "missing-package-id.request.json");
        Assert.True(File.Exists(path), $"missing invalid fixture: {path}");

        var schema = await TestData.SchemaAsync("PackageRequest");
        var errors = schema.Validate(await File.ReadAllTextAsync(path));

        Assert.True(errors.Count > 0, "expected the empty package id to fail schema validation");
    }

    private static async Task AssertValid(string samplePath, JsonSchema schema)
    {
        var errors = schema.Validate(await File.ReadAllTextAsync(samplePath));

        Assert.True(
            errors.Count == 0,
            $"{Path.GetFileName(samplePath)} failed schema validation:\n" +
            string.Join("\n", errors.Select(e => $"  {e.Kind} at {e.Path}")));
    }
}