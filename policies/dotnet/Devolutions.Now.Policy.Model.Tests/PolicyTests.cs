using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using NJsonSchema;

using Xunit;

namespace Devolutions.Now.Policy.Model.Tests;

public class PolicyTests
{
    private static string PolicyCrateRoot { get; } = ResolvePolicyCrateRoot();

    private static string SamplesDir => Path.Combine(PolicyCrateRoot, "assets", "samples");

    private static string PolicySchema => Path.Combine(PolicyCrateRoot, "schema", "devolutions.now-policy.schema.json");

    private static string PolicyDraftSchema =>
        Path.Combine(PolicyCrateRoot, "schema", "devolutions.now-policy-draft.schema.json");

    public static IEnumerable<object[]> PolicySamples() =>
        Directory.GetFiles(SamplesDir, "*.policy.json").Select(f => new object[] { f });

    public static IEnumerable<object[]> DuplicatePropertySamples() =>
        Directory.GetFiles(
                Path.Combine(SamplesDir, "invalid", "duplicates"),
                "*.policy.json")
            .Select(f => new object[] { f });

    public static TheoryData<string, int> ConstraintTextCollections() => new()
    {
        { nameof(PolicyConstraints.AllowedInstallLocationPatterns), 256 },
        { nameof(PolicyConstraints.AllowedCustomParameters), 512 },
        { nameof(PolicyConstraints.AllowedCustomParameterPatterns), 512 },
        { nameof(PolicyConstraints.DeniedCustomParameters), 512 },
    };

    public static TheoryData<string> BooleanMatchProperties() => new()
    {
        nameof(PolicyMatch.Interactive),
        nameof(PolicyMatch.SkipHashCheck),
        nameof(PolicyMatch.PreRelease),
        nameof(PolicyMatch.HasCustomParameters),
        nameof(PolicyMatch.HasCustomInstallLocation),
        nameof(PolicyMatch.HasPrePostCommands),
        nameof(PolicyMatch.HasKillBeforeOperation),
        nameof(PolicyMatch.HasUninstallPrevious),
    };

    public static TheoryData<string, string> CollectionMatchProperties() => new()
    {
        { nameof(PolicyMatch.Operations), "\"Install\"" },
        { nameof(PolicyMatch.Managers), "\"Winget\"" },
        { nameof(PolicyMatch.SourceNames), "\"winget\"" },
        { nameof(PolicyMatch.Scopes), "\"User\"" },
        { nameof(PolicyMatch.Architectures), "\"X64\"" },
        { nameof(PolicyMatch.ExecutionElevation), "\"Standard\"" },
    };

    [Fact]
    public void Tests_run_with_reflection_json_disabled()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
    }

    [Theory]
    [MemberData(nameof(PolicySamples))]
    public async Task Policy_samples_parse_and_validate_against_rust_schema(string path)
    {
        var policy = ParsePolicy(path);
        var schema = await JsonSchema.FromFileAsync(PolicySchema);
        var errors = schema.Validate(policy.ToJson());

        Assert.True(
            errors.Count == 0,
            $"{Path.GetFileName(path)} failed policy schema validation:\n" +
            string.Join("\n", errors.Select(e => $"  {e.Kind} at {e.Path}")));
    }

    [Fact]
    public async Task Created_policy_validates_against_rust_schema()
    {
        var policy = PolicyDocument.Create("contoso.policy", "Contoso IT");
        policy.Rules.Add(new PolicyRule
        {
            Id = "allow.vscode",
            Priority = 100,
            Decision = Decision.Allow,
            Match = new PolicyMatch
            {
                Operations = [Operation.Install],
                Managers = [ManagerName.Winget],
                PackageIdentifiers = new PackageIdentifierCondition
                {
                    Exact = ["Microsoft.VisualStudioCode"],
                },
            },
        });

        var schema = await JsonSchema.FromFileAsync(PolicySchema);
        var json = policy.ToJson();
        var reparsed = PolicySerializer.DeserializeStrict<PolicyDocument>(json);
        var errors = schema.Validate(json);

        Assert.NotNull(reparsed);
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => $"  {e.Kind} at {e.Path}")));
    }

    [Fact]
    public async Task Draft_conversion_uses_and_validates_against_draft_schema()
    {
        var policy = PolicyDocument.Create("contoso.policy", "Contoso IT");
        var draft = policy.ToDraft();
        var schema = await JsonSchema.FromFileAsync(PolicyDraftSchema);
        var json = draft.ToJson();

        Assert.Equal(PolicyFormatVersions.Current, draft.PolicyFormatVersion.Value);
        Assert.Null(JsonNode.Parse(json)!["$schema"]);
        Assert.Empty(schema.Validate(json));
        Assert.Equal(
            PolicyFormatVersions.Current,
            draft.ToPolicyDocument(1, DateTimeOffset.UtcNow).PolicyFormatVersion.Value);
    }

    [Fact]
    public void Policy_and_draft_parsers_reject_schema_member()
    {
        var policy = PolicyDocument.Create("contoso.policy", "Contoso IT");
        var policyJson = JsonNode.Parse(policy.ToJson())!;
        policyJson["$schema"] = "https://example.invalid/policy.schema.json";
        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(policyJson.ToJsonString()));

        var draftJson = JsonNode.Parse(policy.ToDraft().ToJson())!;
        draftJson["$schema"] = "https://example.invalid/policy-draft.schema.json";
        Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializePolicyDraftDocumentStrict(draftJson.ToJsonString()));
    }

    [Theory]
    [MemberData(nameof(DuplicatePropertySamples))]
    public void All_policy_deserialization_entry_points_reject_duplicate_properties(string path)
    {
        var json = File.ReadAllText(path);

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(json));
        Assert.ThrowsAny<JsonException>(() => PolicySerializer.DeserializePolicyDocument(json));
        Assert.Throws<JsonException>(() => PolicySerializer.DeserializePolicyDocumentStrict(json));
        Assert.Throws<JsonException>(() => PolicySerializer.DeserializeStrict<PolicyDocument>(json));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDocument>(json, PolicySerializer.Options));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDocument>(json, PolicySerializer.StrictOptions));
    }

    [Fact]
    public void All_draft_deserialization_entry_points_reject_nested_escaped_duplicate_properties()
    {
        const string Json = """
            {
              "PolicyFormatVersion": "1.0.0",
              "Metadata": {
                "Id": "duplicate.test",
                "\u0049d": "duplicate.test",
                "Publisher": "Test"
              },
              "Enforcement": {
                "DefaultDecision": "Deny"
              },
              "Rules": []
            }
            """;

        Assert.Throws<JsonException>(() => PolicyDraftDocument.ParseJson(Json));
        Assert.Throws<JsonException>(() => PolicySerializer.DeserializePolicyDraftDocumentStrict(Json));
        Assert.Throws<JsonException>(() => PolicySerializer.DeserializeStrict<PolicyDraftDocument>(Json));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDraftDocument>(Json, PolicySerializer.Options));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDraftDocument>(Json, PolicySerializer.StrictOptions));
    }

    [Fact]
    public void Duplicate_property_comparison_is_ordinal_and_case_sensitive()
    {
        var json = MinimalPolicyJson(
            """
                "Revision": 1,
            """,
            """
                "policyFormatVersion": "1.1.0",
                "Rules": []
            """);

        var exception = Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializePolicyDocument(json));
        Assert.DoesNotContain("Duplicate JSON property name", exception.Message, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => PolicySerializer.DeserializePolicyDocumentStrict(json));
    }

    [Fact]
    public void Escaped_surrogate_pairs_match_literal_unicode_property_names()
    {
        var json = MinimalPolicyJson(
            """
                "Revision": 1,
            """,
            """
                "Extension": {
                    "😀": true,
                    "\uD83D\uDE00": false
                },
                "Rules": []
            """);

        var exception = Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializePolicyDocument(json));
        Assert.Contains("Duplicate JSON property name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_preprocessing_observes_the_serializer_depth_limit()
    {
        var nested = string.Concat(Enumerable.Repeat("""{"Nested":""", 65))
            + "true"
            + new string('}', 65);
        var json = MinimalPolicyJson(
            """
                "Revision": 1,
            """,
            $$"""
                "Extension": {{nested}},
                "Rules": []
            """);

        Assert.ThrowsAny<JsonException>(() => PolicySerializer.DeserializePolicyDocument(json));
    }

    [Theory]
    [InlineData("PolicyType")]
    [InlineData("RulePrecedence")]
    [InlineData("PackageNames")]
    [InlineData("Elevation")]
    [InlineData("Sources")]
    [InlineData("Versions")]
    [InlineData("VersionRange")]
    public void Removed_policy_members_are_rejected_as_unknown(string member)
    {
        var policy = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")));
        var documents = new (JsonNode Document, Action<string> Parse)[]
        {
            (
                JsonNode.Parse(policy.ToJson())!,
                json => PolicySerializer.DeserializeStrict<PolicyDocument>(json)),
            (
                JsonNode.Parse(policy.ToDraft().ToJson())!,
                json => PolicySerializer.DeserializeStrict<PolicyDraftDocument>(json)),
        };

        foreach (var (document, parse) in documents)
        {
            switch (member)
            {
                case "PolicyType":
                    document[member] = "PackageBrokerPolicy";
                    break;
                case "RulePrecedence":
                    document["Enforcement"]![member] = "PriorityThenDeny";
                    break;
                case "VersionRange":
                    document["Rules"]![0]!["Match"]![member] = new JsonObject { ["MinVersion"] = "1.0.0" };
                    break;
                default:
                    document["Rules"]![0]!["Match"]![member] = new JsonArray("Friendly package name");
                    break;
            }

            Assert.Throws<JsonException>(() => parse(document.ToJsonString()));
        }
    }

    [Fact]
    public void Non_strict_policy_inputs_reject_removed_members_instead_of_broadening_rules()
    {
        var committed = JsonNode.Parse(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")))!;
        committed["Rules"]![3]!["Match"]!["PackageNames"] =
            new JsonArray("Visual Studio Code");
        committed["Rules"]![3]!["Match"]!["Sources"] =
            new JsonArray("winget");
        committed["Rules"]![3]!["Match"]!["Versions"] =
            new JsonArray("1.0.0");
        var committedJson = committed.ToJsonString();

        Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializePolicyDocument(committedJson));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDocument>(
                committedJson,
                PolicySerializer.Options));

        var draft = committed.DeepClone();
        draft["Metadata"]!.AsObject().Remove("Revision");
        draft["Metadata"]!.AsObject().Remove("PublishedAt");
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDraftDocument>(
                draft.ToJsonString(),
                PolicySerializer.Options));
    }

    [Theory]
    [InlineData("PolicyType")]
    [InlineData("RulePrecedence")]
    [InlineData("PackageNames")]
    [InlineData("Elevation")]
    [InlineData("Sources")]
    [InlineData("Versions")]
    [InlineData("VersionRange")]
    public async Task Rust_schemas_reject_removed_policy_members(string member)
    {
        var policy = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")));
        var documents = new[]
        {
            (
                Document: JsonNode.Parse(policy.ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicySchema)),
            (
                Document: JsonNode.Parse(policy.ToDraft().ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicyDraftSchema)),
        };

        foreach (var (document, schema) in documents)
        {
            switch (member)
            {
                case "PolicyType":
                    document[member] = "PackageBrokerPolicy";
                    break;
                case "RulePrecedence":
                    document["Enforcement"]![member] = "PriorityThenDeny";
                    break;
                case "PackageNames":
                    document["Rules"]![0]!["Match"]![member] = new JsonArray("Friendly package name");
                    break;
                case "VersionRange":
                    document["Rules"]![0]!["Match"]![member] = new JsonObject { ["MinVersion"] = "1.0.0" };
                    break;
                default:
                    document["Rules"]![0]!["Match"]![member] = new JsonArray("Elevated");
                    break;
            }

            Assert.NotEmpty(schema.Validate(document.ToJsonString()));
        }
    }

    [Fact]
    public void Canonical_policy_json_omits_removed_members()
    {
        var policy = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "boolean-characteristics.policy.json")));
        var json = policy.ToJson();

        Assert.DoesNotContain("\"PolicyType\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RulePrecedence\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"PackageNames\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Elevation\":", json, StringComparison.Ordinal);
        Assert.Contains("\"ExecutionElevation\"", json, StringComparison.Ordinal);
        Assert.Contains("\"SourceNames\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Sources\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_surrogate_property_names_remain_json_errors()
    {
        var json = MinimalPolicyJson(
            """
                "Revision": 1,
                "\uD800": true,
            """,
            """
                "Rules": []
            """);

        Assert.ThrowsAny<JsonException>(() => PolicySerializer.DeserializePolicyDocument(json));
        Assert.ThrowsAny<JsonException>(() => PolicySerializer.DeserializePolicyDocumentStrict(json));
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDocument>(json, PolicySerializer.Options));
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<PolicyDocument>(json, PolicySerializer.StrictOptions));
    }

    [Fact]
    public void Duplicate_rejecting_options_preserve_caller_serialization_settings()
    {
        var options = new JsonSerializerOptions(PolicySerializer.Options)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        var json = JsonSerializer.Serialize(
            PolicyDocument.Create("custom.options", "Test"),
            options);

        Assert.Contains("\"ValidFrom\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"ValidUntil\": null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_policy_fixture_is_rejected_by_parser()
    {
        var path = Path.Combine(SamplesDir, "invalid", "policies", "invalid-failure-decision.policy.json");
        var content = File.ReadAllText(path);

        Assert.ThrowsAny<Exception>(() => PolicyDocument.ParseJson(content));
    }

    [Fact]
    public void Negative_revision_is_rejected_by_parser()
    {
        var json = MinimalPolicyJson("""
                "Revision": -1,
        """, """
                "Rules": []
        """);

        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(json));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2147483648")]
    public void Out_of_range_revision_is_rejected_by_parser(string revision)
    {
        var json = MinimalPolicyJson($"""
                "Revision": {revision},
        """, """
                "Rules": []
        """);

        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(json));
    }

    [Fact]
    public void Negative_priority_is_rejected_by_parser()
    {
        var json = MinimalPolicyJson("""
                "Revision": 1,
        """, """
                "Rules": [
                    {
                        "Id": "deny.test",
                        "Enabled": true,
                        "Priority": -1,
                        "Decision": "Deny",
                        "Match": {
                            "Operations": ["Install"]
                        }
                    }
                ]
        """);

        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(json));
    }

    [Theory]
    [InlineData("PolicyFormatVersion")]
    [InlineData("Metadata")]
    [InlineData("Enforcement")]
    [InlineData("Rules")]
    [InlineData("Metadata.Id")]
    [InlineData("Metadata.Publisher")]
    [InlineData("Metadata.Revision")]
    [InlineData("Metadata.PublishedAt")]
    [InlineData("Enforcement.DefaultDecision")]
    [InlineData("Rules.0.Id")]
    [InlineData("Rules.0.Priority")]
    [InlineData("Rules.0.Decision")]
    [InlineData("Rules.0.Match")]
    public void Missing_rust_required_property_is_rejected_by_parser(string propertyPath)
    {
        var path = Path.Combine(SamplesDir, "corporate-allowlist.policy.json");
        var document = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException("policy sample should parse");

        RemoveProperty(document, propertyPath);

        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(document.ToJsonString()));
    }

    [Theory]
    [InlineData("PolicyFormatVersion")]
    [InlineData("Metadata")]
    [InlineData("Enforcement")]
    [InlineData("Rules")]
    [InlineData("Metadata.Id")]
    [InlineData("Metadata.Publisher")]
    [InlineData("Metadata.Revision")]
    [InlineData("Metadata.PublishedAt")]
    [InlineData("Enforcement.DefaultDecision")]
    [InlineData("Rules.0.Id")]
    [InlineData("Rules.0.Priority")]
    [InlineData("Rules.0.Decision")]
    [InlineData("Rules.0.Match")]
    public void Null_rust_required_property_is_rejected_by_parser(string propertyPath)
    {
        var path = Path.Combine(SamplesDir, "corporate-allowlist.policy.json");
        var document = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException("policy sample should parse");

        SetPropertyToNull(document, propertyPath);

        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(document.ToJsonString()));
    }

    [Theory]
    [InlineData("Rules.0")]
    [InlineData("Rules.3.Match.SourceNames.0")]
    [InlineData("Rules.3.Match.PackageIdentifiers.Exact.0")]
    public void Null_policy_collection_element_is_rejected_by_parser(string elementPath)
    {
        var path = Path.Combine(SamplesDir, "corporate-allowlist.policy.json");
        var document = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException("policy sample should parse");
        SetPropertyToNull(document, elementPath);

        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(document.ToJsonString()));
    }

    [Fact]
    public void Draft_conversion_omits_and_restores_server_metadata_without_aliasing()
    {
        var committed = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")));

        var draft = committed.ToDraft();
        var draftJson = JsonNode.Parse(draft.ToJson())!;
        Assert.Null(draftJson["$schema"]);
        Assert.Null(draftJson["Metadata"]!["Revision"]);
        Assert.Null(draftJson["Metadata"]!["PublishedAt"]);

        draft.Rules[0].Id = "changed";
        Assert.NotEqual(draft.Rules[0].Id, committed.Rules[0].Id);

        var publishedAt = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        var recommitted = draft.ToPolicyDocument(7, publishedAt);
        Assert.Equal(committed.PolicyFormatVersion, recommitted.PolicyFormatVersion);
        Assert.Equal(committed.Metadata.Id, recommitted.Metadata.Id);
        Assert.Equal(committed.Metadata.Publisher, recommitted.Metadata.Publisher);
        Assert.Equal(committed.Metadata.Description, recommitted.Metadata.Description);
        Assert.Equal(committed.Metadata.SupportUrl, recommitted.Metadata.SupportUrl);
        Assert.Equal(committed.Metadata.ValidFrom, recommitted.Metadata.ValidFrom);
        Assert.Equal(committed.Metadata.ValidUntil, recommitted.Metadata.ValidUntil);
        Assert.Equal(7U, recommitted.Metadata.Revision);
        Assert.Equal(publishedAt, recommitted.Metadata.PublishedAt);
        Assert.Equal("changed", recommitted.Rules[0].Id);
    }

    [Theory]
    [InlineData("not-semver")]
    [InlineData("2.0.0")]
    [InlineData("1.18446744073709551616.0")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.0.0\n")]
    public void Unsupported_policy_format_versions_are_rejected(string value)
    {
        var document = JsonNode.Parse(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")))!;
        document["PolicyFormatVersion"] = value;

        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(document.ToJsonString()));
    }

    [Theory]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-.")]
    [InlineData("1.0.0\n")]
    public async Task Rust_schema_rejects_unsupported_policy_format_versions(string value)
    {
        var document = JsonNode.Parse(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")))!;
        document["PolicyFormatVersion"] = value;
        var schema = await JsonSchema.FromFileAsync(PolicySchema);

        Assert.NotEmpty(schema.Validate(document.ToJsonString()));
    }

    [Fact]
    public void Compatible_policy_format_version_is_preserved_by_conversion()
    {
        var document = JsonNode.Parse(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")))!;
        document["PolicyFormatVersion"] = "1.2.3";

        var committed = PolicyDocument.ParseJson(document.ToJsonString());
        var recommitted = committed.ToDraft().ToPolicyDocument(8, DateTimeOffset.UtcNow);

        Assert.Equal("1.2.3", recommitted.PolicyFormatVersion.Value);
    }

    [Fact]
    public void Draft_conversion_enforces_revision_bounds()
    {
        var draft = PolicyDraftDocument.Create("contoso.policy", "Contoso IT");
        var publishedAt = DateTimeOffset.Parse("2026-08-29T00:00:00Z");

        Assert.Equal(
            (uint)int.MaxValue,
            draft.ToPolicyDocument(int.MaxValue, publishedAt).Metadata.Revision);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => draft.ToPolicyDocument((uint)int.MaxValue + 1, publishedAt));
    }

    [Fact]
    public void Draft_conversions_reject_invalid_union_conditions_before_cloning()
    {
        var committed = PolicyDocument.Create("invalid.clone", "Test");
        committed.Rules.Add(new PolicyRule
        {
            Id = "allow.invalid",
            Priority = 1,
            Decision = Decision.Allow,
            Match = new PolicyMatch
            {
                Managers = [ManagerName.Winget],
                PackageIdentifiers = new PackageIdentifierCondition(),
            },
        });
        Assert.Throws<JsonException>(() => committed.ToDraft());

        committed.Rules[0].Match.PackageIdentifiers = new PackageIdentifierCondition
        {
            Exact = ["Git.Git"],
            Patterns = null,
        };
        Assert.Throws<JsonException>(() => committed.ToDraft());

        var draft = PolicyDraftDocument.Create("invalid.clone", "Test");
        draft.Rules.Add(new PolicyRule
        {
            Id = "allow.invalid",
            Priority = 1,
            Decision = Decision.Allow,
            Match = new PolicyMatch
            {
                Managers = [ManagerName.Winget],
                Version = new VersionCondition(),
            },
        });
        Assert.Throws<JsonException>(
            () => draft.ToPolicyDocument(1, DateTimeOffset.UtcNow));

        draft.Rules[0].Match.Version = new VersionCondition
        {
            Exact = ["1.0.0"],
            Range = null,
        };
        Assert.Throws<JsonException>(
            () => draft.ToPolicyDocument(1, DateTimeOffset.UtcNow));
    }

    [Theory]
    [MemberData(nameof(BooleanMatchProperties))]
    public void Boolean_match_characteristics_accept_omitted_null_false_and_true(string propertyName)
    {
        var omitted = PolicySerializer.DeserializeStrict<PolicyMatch>("{}")!;
        Assert.Null(GetBooleanMatch(omitted, propertyName));
        Assert.DoesNotContain($"\"{propertyName}\"", PolicySerializer.Serialize(omitted));

        var explicitNull = PolicySerializer.DeserializeStrict<PolicyMatch>(
            $$"""{"{{propertyName}}":null}""")!;
        Assert.Null(GetBooleanMatch(explicitNull, propertyName));
        Assert.DoesNotContain($"\"{propertyName}\"", PolicySerializer.Serialize(explicitNull));

        foreach (var expected in new[] { false, true })
        {
            var match = PolicySerializer.DeserializeStrict<PolicyMatch>(
                $$"""{"{{propertyName}}":{{expected.ToString().ToLowerInvariant()}}}""")!;
            Assert.Equal(expected, GetBooleanMatch(match, propertyName));

            var serialized = JsonNode.Parse(PolicySerializer.Serialize(match))!;
            Assert.Equal(expected, serialized[propertyName]!.GetValue<bool>());
        }
    }

    [Theory]
    [MemberData(nameof(BooleanMatchProperties))]
    public void Boolean_match_characteristics_reject_legacy_arrays_and_wrong_types(string propertyName)
    {
        foreach (var invalidValue in new[] { "[]", "[false]", "[true]", "[false,true]", "\"true\"", "0", "{}" })
        {
            var matchJson = $$"""{"{{propertyName}}":{{invalidValue}}}""";
            var ruleJson =
                $$"""{"Id":"test.rule","Priority":1,"Decision":"Allow","Match":{{matchJson}}}""";

            Assert.Throws<JsonException>(() => PolicySerializer.DeserializeStrict<PolicyMatch>(matchJson));
            Assert.Throws<JsonException>(() => PolicySerializer.DeserializeStrict<PolicyRule>(ruleJson));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyMatch>(matchJson, PolicySerializer.Options));
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyMatch>(matchJson, PolicySerializer.StrictOptions));
        }
    }

    [Theory]
    [MemberData(nameof(BooleanMatchProperties))]
    public void Null_only_boolean_match_is_not_an_effective_rule_criterion(string propertyName)
    {
        var rule = new JsonObject
        {
            ["Id"] = "test.rule",
            ["Priority"] = 1,
            ["Decision"] = "Allow",
            ["Match"] = new JsonObject { [propertyName] = null },
        };
        Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializeStrict<PolicyRule>(rule.ToJsonString()));

        rule["Match"] = new JsonObject
        {
            ["Operations"] = new JsonArray("Install"),
            [propertyName] = null,
        };
        Assert.NotNull(PolicySerializer.DeserializeStrict<PolicyRule>(rule.ToJsonString()));
    }

    [Fact]
    public void Boolean_match_characteristics_round_trip_in_representative_mixed_match()
    {
        const string Json = """
            {
              "Operations": ["Install"],
              "Interactive": false,
              "SkipHashCheck": true,
              "HasCustomParameters": false,
              "HasUninstallPrevious": true
            }
            """;

        var match = PolicySerializer.DeserializeStrict<PolicyMatch>(Json)!;
        Assert.Equal([Operation.Install], match.Operations);
        Assert.False(match.Interactive);
        Assert.True(match.SkipHashCheck);
        Assert.False(match.HasCustomParameters);
        Assert.True(match.HasUninstallPrevious);
        Assert.Null(match.PreRelease);
        Assert.Null(match.HasCustomInstallLocation);
        Assert.Null(match.HasPrePostCommands);
        Assert.Null(match.HasKillBeforeOperation);

        var serialized = JsonNode.Parse(PolicySerializer.Serialize(match))!;
        Assert.False(serialized["Interactive"]!.GetValue<bool>());
        Assert.True(serialized["SkipHashCheck"]!.GetValue<bool>());
        Assert.Null(serialized["PreRelease"]);
        Assert.Null(serialized["HasCustomInstallLocation"]);
    }

    [Theory]
    [MemberData(nameof(CollectionMatchProperties))]
    public void Collection_match_filters_accept_empty_input_and_canonicalize_to_omitted(
        string propertyName,
        string elementJson)
    {
        var omitted = PolicySerializer.DeserializeStrict<PolicyMatch>("{}")!;
        Assert.Equal(0, GetCollectionMatchCount(omitted, propertyName));
        Assert.DoesNotContain($"\"{propertyName}\"", PolicySerializer.Serialize(omitted));

        var empty = PolicySerializer.DeserializeStrict<PolicyMatch>(
            $$"""{"{{propertyName}}":[]}""")!;
        Assert.Equal(0, GetCollectionMatchCount(empty, propertyName));
        Assert.DoesNotContain($"\"{propertyName}\"", PolicySerializer.Serialize(empty));
        foreach (var options in new[] { PolicySerializer.Options, PolicySerializer.StrictOptions })
        {
            Assert.DoesNotContain(
                $"\"{propertyName}\"",
                JsonSerializer.Serialize(empty, options),
                StringComparison.Ordinal);
        }

        var populatedJson = new JsonObject
        {
            [propertyName] = new JsonArray(JsonNode.Parse(elementJson)),
        };
        if (propertyName == nameof(PolicyMatch.SourceNames))
        {
            populatedJson[nameof(PolicyMatch.Managers)] = new JsonArray("Winget");
        }
        var populated = PolicySerializer.DeserializeStrict<PolicyMatch>(populatedJson.ToJsonString())!;
        Assert.Equal(1, GetCollectionMatchCount(populated, propertyName));
        var serialized = JsonNode.Parse(PolicySerializer.Serialize(populated))!;
        Assert.Single(serialized[propertyName]!.AsArray());
    }

    [Theory]
    [MemberData(nameof(CollectionMatchProperties))]
    public void Empty_collection_only_match_is_not_an_effective_rule_criterion(
        string propertyName,
        string elementJson)
    {
        var emptyOnly = new JsonObject
        {
            ["Id"] = "test.rule",
            ["Priority"] = 1,
            ["Decision"] = "Allow",
            ["Match"] = new JsonObject { [propertyName] = new JsonArray() },
        };
        Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializeStrict<PolicyRule>(emptyOnly.ToJsonString()));

        emptyOnly["Match"] = new JsonObject
        {
            [propertyName] = new JsonArray(),
            ["Interactive"] = false,
        };
        var rule = PolicySerializer.DeserializeStrict<PolicyRule>(emptyOnly.ToJsonString())!;
        var serialized = JsonNode.Parse(PolicySerializer.Serialize(rule))!;
        Assert.Null(serialized["Match"]![propertyName]);
        Assert.False(serialized["Match"]!["Interactive"]!.GetValue<bool>());

        emptyOnly["Match"] = new JsonObject
        {
            [propertyName] = new JsonArray(JsonNode.Parse(elementJson)),
        };
        if (propertyName == nameof(PolicyMatch.SourceNames))
        {
            emptyOnly["Match"]!["Managers"] = new JsonArray("Winget");
        }
        Assert.NotNull(PolicySerializer.DeserializeStrict<PolicyRule>(emptyOnly.ToJsonString()));
    }

    [Theory]
    [MemberData(nameof(CollectionMatchProperties))]
    public void Collection_match_filters_reject_duplicate_values(
        string propertyName,
        string elementJson)
    {
        var match = new JsonObject
        {
            [propertyName] = new JsonArray(
                JsonNode.Parse(elementJson),
                JsonNode.Parse(elementJson)),
        };
        if (propertyName == nameof(PolicyMatch.SourceNames))
        {
            match[nameof(PolicyMatch.Managers)] = new JsonArray("Winget");
        }

        Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializeStrict<PolicyMatch>(match.ToJsonString()));
    }

    [Fact]
    public void Source_names_require_managers_and_preserve_exact_literal_names()
    {
        const string WithoutManager = """
            {
              "Id": "source.rule",
              "Priority": 1,
              "Decision": "Allow",
              "Match": { "SourceNames": ["corp*"] }
            }
            """;
        var exception = Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializeStrict<PolicyRule>(WithoutManager));
        Assert.Contains("$.Match.SourceNames", exception.Message, StringComparison.Ordinal);

        const string WithManager = """
            {
              "Id": "source.rule",
              "Priority": 1,
              "Decision": "Allow",
              "Match": {
                "Managers": ["Winget"],
                "SourceNames": ["corp*", "PSGallery"]
              }
            }
            """;
        var rule = PolicySerializer.DeserializeStrict<PolicyRule>(WithManager)!;
        Assert.Equal([ManagerName.Winget], rule.Match.Managers);
        Assert.Equal(["corp*", "PSGallery"], rule.Match.SourceNames);

        var serialized = JsonNode.Parse(PolicySerializer.Serialize(rule))!;
        Assert.Equal("corp*", serialized["Match"]!["SourceNames"]![0]!.GetValue<string>());

        rule.Match.Managers.Clear();
        exception = Assert.Throws<JsonException>(() => PolicySerializer.Serialize(rule));
        Assert.Contains("$.Match.SourceNames", exception.Message, StringComparison.Ordinal);

        const string MultipleManagers = """
            {
              "Id": "source.rule",
              "Priority": 1,
              "Decision": "Allow",
              "Match": {
                "Managers": ["Winget", "PowerShell"],
                "SourceNames": ["corp"]
              }
            }
            """;
        exception = Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializeStrict<PolicyRule>(MultipleManagers));
        Assert.Contains("$.Match.SourceNames", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rust_schemas_require_exactly_one_manager_for_source_names()
    {
        foreach (var schemaPath in new[] { PolicySchema, PolicyDraftSchema })
        {
            var schema = JsonNode.Parse(File.ReadAllText(schemaPath))!;
            var match = schema["definitions"]!["PolicyRule"]!["properties"]!["Match"]!;

            Assert.Contains(
                "SourceNames",
                match["if"]!["required"]!.AsArray().Select(value => value!.GetValue<string>()));
            Assert.Contains(
                "Managers",
                match["then"]!["required"]!.AsArray().Select(value => value!.GetValue<string>()));
            Assert.Equal(1, match["then"]!["properties"]!["Managers"]!["minItems"]!.GetValue<int>());
            Assert.Equal(1, match["then"]!["properties"]!["Managers"]!["maxItems"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Package_identifier_condition_requires_exactly_one_nonempty_mode()
    {
        const string Exact = """{"Exact":["Microsoft.VisualStudioCode"]}""";
        var exact = PolicySerializer.DeserializeStrict<PackageIdentifierCondition>(Exact)!;
        Assert.Equal(["Microsoft.VisualStudioCode"], exact.Exact);
        Assert.Null(exact.Patterns);

        const string Patterns = """{"Patterns":["Microsoft.*"]}""";
        var patterns = PolicySerializer.DeserializeStrict<PackageIdentifierCondition>(Patterns)!;
        Assert.Equal(["Microsoft.*"], patterns.Patterns);
        Assert.Null(patterns.Exact);

        foreach (var invalid in new[]
        {
            "{}",
            """{"Exact":[]}""",
            """{"Patterns":[]}""",
            """{"Exact":["Microsoft.VisualStudioCode"],"Patterns":["Microsoft.*"]}""",
            """{"Exact":["Microsoft.VisualStudioCode"],"Patterns":null}""",
            """{"Patterns":null,"Exact":["Microsoft.VisualStudioCode"]}""",
            """{"Patterns":["Microsoft.*"],"Exact":null}""",
            """{"Exact":null,"Patterns":["Microsoft.*"]}""",
            """{"Exact":["Microsoft.*"]}""",
            """{"Exact":["Git.Git","Git.Git"]}""",
            """{"Patterns":["Git.*","Git.*"]}""",
            """{"Exact":["Microsoft.VisualStudioCode"],"\u0045xact":["Git.Git"]}""",
        })
        {
            Assert.Throws<JsonException>(
                () => PolicySerializer.DeserializeStrict<PackageIdentifierCondition>(invalid));
        }

        const string OldFlatList = """{"PackageIdentifiers":["Microsoft.VisualStudioCode"]}""";
        Assert.Throws<JsonException>(() => PolicySerializer.DeserializeStrict<PolicyMatch>(OldFlatList));

        var absent = PolicySerializer.DeserializeStrict<PolicyMatch>(
            """{"PackageIdentifiers":null}""")!;
        Assert.DoesNotContain("\"PackageIdentifiers\"", PolicySerializer.Serialize(absent));

        var match = PolicySerializer.DeserializeStrict<PolicyMatch>(
            """{"PackageIdentifiers":{"Patterns":["Microsoft.*"]}}""")!;
        Assert.Equal(["Microsoft.*"], match.PackageIdentifiers!.Patterns);
        Assert.Contains("\"Patterns\"", PolicySerializer.Serialize(match), StringComparison.Ordinal);
    }

    [Fact]
    public void Version_condition_requires_exactly_one_nonempty_mode()
    {
        const string Exact = """{"Exact":["5.6.0.0","2026.09-preview"]}""";
        var exact = PolicySerializer.DeserializeStrict<VersionCondition>(Exact)!;
        Assert.Equal(["5.6.0.0", "2026.09-preview"], exact.Exact);
        Assert.Null(exact.Range);

        const string Range = """{"Range":{"MinVersion":"1.0.0","MaxVersion":"2.0.0"}}""";
        var range = PolicySerializer.DeserializeStrict<VersionCondition>(Range)!;
        Assert.Equal("1.0.0", range.Range!.MinVersion);
        Assert.Null(range.Exact);
        Assert.NotNull(PolicySerializer.DeserializeStrict<VersionCondition>(
            """{"Range":{"MinVersion":"1.0.0-beta.1","IncludePrerelease":true}}"""));

        foreach (var invalid in new[]
        {
            "{}",
            """{"Exact":[]}""",
            """{"Range":{}}""",
            """{"Range":{"MinVersion":null,"MaxVersion":null}}""",
            """{"Range":{"MinVersion":"not-semver"}}""",
            """{"Range":{"MinVersion":"1.18446744073709551616.0"}}""",
            """{"Range":{"MinVersion":"1.0.0-١a"}}""",
            """{"Range":{"MaxVersion":"1.0.0\n"}}""",
            """{"Exact":["1.0.0"],"Range":{"MinVersion":"1.0.0"}}""",
            """{"Exact":["1.0.0"],"Range":null}""",
            """{"Range":null,"Exact":["1.0.0"]}""",
            """{"Range":{"MinVersion":"1.0.0"},"Exact":null}""",
            """{"Exact":null,"Range":{"MinVersion":"1.0.0"}}""",
            """{"Exact":["1.0.0","1.0.0"]}""",
            """{"Exact":["1.0.0"],"\u0045xact":["2.0.0"]}""",
        })
        {
            Assert.Throws<JsonException>(
                () => PolicySerializer.DeserializeStrict<VersionCondition>(invalid));
        }

        foreach (var old in new[]
        {
            """{"Versions":["1.0.0"]}""",
            """{"VersionRange":{"MinVersion":"1.0.0"}}""",
        })
        {
            Assert.Throws<JsonException>(() => PolicySerializer.DeserializeStrict<PolicyMatch>(old));
        }

        var absent = PolicySerializer.DeserializeStrict<PolicyMatch>("""{"Version":null}""")!;
        Assert.DoesNotContain("\"Version\"", PolicySerializer.Serialize(absent));

        Assert.Throws<JsonException>(
            () => PolicySerializer.Serialize(new VersionRange()));
        Assert.Throws<JsonException>(
            () => PolicySerializer.Serialize(new VersionRange { MinVersion = "not-semver" }));
    }

    [Fact]
    public async Task Rust_schemas_enforce_package_identifier_and_version_modes()
    {
        var policy = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "boolean-characteristics.policy.json")));
        var documents = new[]
        {
            (
                Document: JsonNode.Parse(policy.ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicySchema)),
            (
                Document: JsonNode.Parse(policy.ToDraft().ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicyDraftSchema)),
        };

        foreach (var (document, schema) in documents)
        {
            var match = document["Rules"]![0]!["Match"]!;
            foreach (var invalidIdentifiers in new JsonNode[]
            {
                new JsonArray("Microsoft.VisualStudioCode"),
                new JsonObject(),
                new JsonObject { ["Exact"] = new JsonArray() },
                new JsonObject { ["Exact"] = new JsonArray("Microsoft.*") },
                new JsonObject { ["Exact"] = new JsonArray("Git.Git\n") },
                new JsonObject
                {
                    ["Exact"] = new JsonArray("Microsoft.VisualStudioCode"),
                    ["Patterns"] = new JsonArray("Microsoft.*"),
                },
            })
            {
                match["PackageIdentifiers"] = invalidIdentifiers.DeepClone();
                Assert.NotEmpty(schema.Validate(document.ToJsonString()));
            }

            match["PackageIdentifiers"] = new JsonObject
            {
                ["Patterns"] = new JsonArray("Microsoft.*"),
            };
            Assert.Empty(schema.Validate(document.ToJsonString()));

            foreach (var invalidVersion in new JsonNode[]
            {
                new JsonObject(),
                new JsonObject { ["Exact"] = new JsonArray() },
                new JsonObject { ["Range"] = new JsonObject() },
                new JsonObject
                {
                    ["Range"] = new JsonObject { ["MinVersion"] = "not-semver" },
                },
                new JsonObject
                {
                    ["Range"] = new JsonObject
                    {
                        ["MinVersion"] = "1.18446744073709551616.0",
                    },
                },
                new JsonObject
                {
                    ["Range"] = new JsonObject { ["MinVersion"] = "1.0.0-١a" },
                },
                new JsonObject
                {
                    ["Exact"] = new JsonArray("1.0.0"),
                    ["Range"] = new JsonObject { ["MinVersion"] = "1.0.0" },
                },
            })
            {
                match["Version"] = invalidVersion.DeepClone();
                Assert.NotEmpty(schema.Validate(document.ToJsonString()));
            }

            match["Version"] = new JsonObject
            {
                ["Range"] = new JsonObject { ["MinVersion"] = "1.0.0" },
            };
            Assert.Empty(schema.Validate(document.ToJsonString()));
        }
    }

    [Fact]
    public void Shared_boolean_characteristics_sample_has_expected_scalar_values()
    {
        var policy = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "boolean-characteristics.policy.json")));
        var match = Assert.Single(policy.Rules).Match;

        Assert.False(match.Interactive);
        Assert.True(match.SkipHashCheck);
        Assert.False(match.PreRelease);
        Assert.True(match.HasCustomParameters);
        Assert.False(match.HasCustomInstallLocation);
        Assert.True(match.HasPrePostCommands);
        Assert.False(match.HasKillBeforeOperation);
        Assert.True(match.HasUninstallPrevious);
        Assert.Equal([ManagerName.Winget], match.Managers);
        Assert.Equal(["corp*"], match.SourceNames);
        Assert.Equal(["Microsoft.VisualStudioCode"], match.PackageIdentifiers!.Exact);
        Assert.Equal(["5.6.0.0"], match.Version!.Exact);
        Assert.Equal([Elevation.Elevated], match.ExecutionElevation);
    }

    [Fact]
    public void Constraints_are_valid_only_for_allow_rules()
    {
        const string AllowWithConstraints = """
            {
              "Id": "allow.rule",
              "Priority": 1,
              "Decision": "Allow",
              "Match": { "Operations": ["Install"] },
              "Constraints": { "AllowInteractive": false }
            }
            """;
        var allow = PolicySerializer.DeserializeStrict<PolicyRule>(AllowWithConstraints)!;
        Assert.NotNull(allow.Constraints);
        Assert.Contains("\"Constraints\"", PolicySerializer.Serialize(allow), StringComparison.Ordinal);

        const string AllowWithoutConstraints = """
            {
              "Id": "allow.rule",
              "Priority": 1,
              "Decision": "Allow",
              "Match": { "Operations": ["Install"] }
            }
            """;
        Assert.NotNull(PolicySerializer.DeserializeStrict<PolicyRule>(AllowWithoutConstraints));

        const string DenyWithoutConstraints = """
            {
              "Id": "deny.rule",
              "Priority": 1,
              "Decision": "Deny",
              "Match": { "Operations": ["Install"] }
            }
            """;
        Assert.NotNull(PolicySerializer.DeserializeStrict<PolicyRule>(DenyWithoutConstraints));

        const string DenyWithNullConstraints = """
            {
              "Id": "deny.rule",
              "Priority": 1,
              "Decision": "Deny",
              "Match": { "Operations": ["Install"] },
              "Constraints": null
            }
            """;
        var deny = PolicySerializer.DeserializeStrict<PolicyRule>(DenyWithNullConstraints)!;
        Assert.DoesNotContain("\"Constraints\"", PolicySerializer.Serialize(deny), StringComparison.Ordinal);

        const string DenyWithConstraints = """
            {
              "Id": "deny.rule",
              "Enabled": false,
              "Priority": 1,
              "Decision": "Deny",
              "Match": { "Operations": ["Install"] },
              "Constraints": { "AllowInteractive": false }
            }
            """;
        var exception = Assert.Throws<JsonException>(
            () => PolicySerializer.DeserializeStrict<PolicyRule>(DenyWithConstraints));
        Assert.Contains("$.Constraints", exception.Message, StringComparison.Ordinal);

        allow.Decision = Decision.Deny;
        exception = Assert.Throws<JsonException>(() => PolicySerializer.Serialize(allow));
        Assert.Contains("$.Constraints", exception.Message, StringComparison.Ordinal);

        foreach (var options in new[] { PolicySerializer.Options, PolicySerializer.StrictOptions })
        {
            exception = Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<PolicyRule>(DenyWithConstraints, options));
            Assert.Contains("$.Constraints", exception.Message, StringComparison.Ordinal);
            Assert.Throws<JsonException>(() => JsonSerializer.Serialize(allow, options));
        }
    }

    [Fact]
    public async Task Rust_schemas_allow_constraints_only_for_allow_rules()
    {
        var policy = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "boolean-characteristics.policy.json")));
        var documents = new[]
        {
            (
                Document: JsonNode.Parse(policy.ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicySchema)),
            (
                Document: JsonNode.Parse(policy.ToDraft().ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicyDraftSchema)),
        };

        foreach (var (document, schema) in documents)
        {
            var rule = document["Rules"]!.AsArray()
                .First(rule => rule!["Decision"]!.GetValue<string>() == "Allow")!;
            rule["Constraints"] = new JsonObject { ["AllowInteractive"] = false };
            Assert.Empty(schema.Validate(document.ToJsonString()));

            rule["Decision"] = "Deny";
            Assert.NotEmpty(schema.Validate(document.ToJsonString()));

            rule["Constraints"] = null;
            Assert.Empty(schema.Validate(document.ToJsonString()));

            rule.AsObject().Remove("Constraints");
            Assert.Empty(schema.Validate(document.ToJsonString()));
        }
    }

    [Fact]
    public void Rust_schemas_define_boolean_match_characteristics_as_nullable_scalars()
    {
        foreach (var schemaPath in new[] { PolicySchema, PolicyDraftSchema })
        {
            var schema = JsonNode.Parse(File.ReadAllText(schemaPath))!;
            var properties = schema["definitions"]!["PolicyRule"]!["properties"]!["Match"]!["properties"]!;

            foreach (var propertyName in new[]
            {
                nameof(PolicyMatch.Interactive),
                nameof(PolicyMatch.SkipHashCheck),
                nameof(PolicyMatch.PreRelease),
                nameof(PolicyMatch.HasCustomParameters),
                nameof(PolicyMatch.HasCustomInstallLocation),
                nameof(PolicyMatch.HasPrePostCommands),
                nameof(PolicyMatch.HasKillBeforeOperation),
                nameof(PolicyMatch.HasUninstallPrevious),
            })
            {
                var property = properties[propertyName]!;
                var types = property["type"]!.AsArray().Select(value => value!.GetValue<string>()).ToList();

                Assert.Contains("boolean", types);
                Assert.Contains("null", types);
                Assert.Null(property["items"]);
                Assert.Null(property["maxItems"]);
                Assert.Null(property["uniqueItems"]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BooleanMatchProperties))]
    public async Task Rust_schemas_reject_legacy_boolean_match_arrays_and_wrong_types(string propertyName)
    {
        var committed = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "boolean-characteristics.policy.json")));
        var documents = new[]
        {
            (
                Document: JsonNode.Parse(committed.ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicySchema)),
            (
                Document: JsonNode.Parse(committed.ToDraft().ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicyDraftSchema)),
        };

        foreach (var invalidValue in new[] { "[]", "[false]", "[true]", "[false,true]", "\"true\"", "0", "{}" })
        {
            foreach (var (document, schema) in documents)
            {
                document["Rules"]![0]!["Match"]![propertyName] = JsonNode.Parse(invalidValue);
                Assert.NotEmpty(schema.Validate(document.ToJsonString()));
            }
        }
    }

    [Theory]
    [MemberData(nameof(BooleanMatchProperties))]
    public async Task Rust_schemas_require_an_effective_rule_criterion_when_boolean_is_null(string propertyName)
    {
        var committed = PolicyDocument.ParseJson(
            File.ReadAllText(Path.Combine(SamplesDir, "boolean-characteristics.policy.json")));
        var documents = new[]
        {
            (
                Document: JsonNode.Parse(committed.ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicySchema)),
            (
                Document: JsonNode.Parse(committed.ToDraft().ToJson())!,
                Schema: await JsonSchema.FromFileAsync(PolicyDraftSchema)),
        };

        foreach (var (document, schema) in documents)
        {
            document["Rules"]![0]!["Match"] = new JsonObject { [propertyName] = null };
            Assert.NotEmpty(schema.Validate(document.ToJsonString()));

            document["Rules"]![0]!["Match"] = new JsonObject
            {
                ["Operations"] = new JsonArray("Install"),
                [propertyName] = null,
            };
            Assert.Empty(schema.Validate(document.ToJsonString()));
        }
    }

    [Theory]
    [InlineData("StringPattern", 256)]
    [InlineData("SourceName", 128)]
    [InlineData("VersionString", 128)]
    [InlineData("CustomParameterString", 512)]
    public void Policy_text_lists_count_unicode_scalars_at_length_boundaries(string valueKind, int maximum)
    {
        var document = JsonNode.Parse(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")))!;
        var rule = document["Rules"]!.AsArray()
            .First(rule => rule!["Decision"]!.GetValue<string>() == "Allow")!;
        var values = new JsonArray();
        switch (valueKind)
        {
            case "StringPattern":
                rule["Match"]!["PackageIdentifiers"] = new JsonObject { ["Patterns"] = values };
                break;
            case "SourceName":
                rule["Match"]!["SourceNames"] = values;
                break;
            case "VersionString":
                rule["Match"]!["Version"] = new JsonObject { ["Exact"] = values };
                break;
            default:
                var constraints = rule["Constraints"] as JsonObject ?? new JsonObject();
                rule["Constraints"] = constraints;
                constraints["AllowedCustomParameters"] = values;
                break;
        }
        var multibyteScalar = "\U0001F600";

        values.Add(ParseJsonString(string.Concat(Enumerable.Repeat(multibyteScalar, maximum))));
        Assert.NotNull(PolicyDocument.ParseJson(document.ToJsonString()));

        values[0] = ParseJsonString(string.Concat(Enumerable.Repeat(multibyteScalar, maximum + 1)));
        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(document.ToJsonString()));

        values[0] = ParseJsonString("");
        Assert.Throws<JsonException>(() => PolicyDocument.ParseJson(document.ToJsonString()));
    }

    [Theory]
    [MemberData(nameof(ConstraintTextCollections))]
    public void Direct_policy_constraints_reject_invalid_bounded_strings(string collectionName, int maximum)
    {
        foreach (var value in new[] { "", new string('x', maximum + 1) })
        {
            var constraints = CreateConstraints(collectionName, value);
            var json = new JsonObject { [collectionName] = new JsonArray(value) }.ToJsonString();

            Assert.Throws<JsonException>(() => PolicySerializer.Serialize(constraints));
            Assert.Throws<JsonException>(() => PolicySerializer.DeserializeStrict<PolicyConstraints>(json));

            foreach (var options in new[] { PolicySerializer.Options, PolicySerializer.StrictOptions })
            {
                Assert.Throws<JsonException>(() => JsonSerializer.Serialize(constraints, options));
                Assert.Throws<JsonException>(
                    () => JsonSerializer.Deserialize<PolicyConstraints>(json, options));
            }
        }
    }

    [Theory]
    [MemberData(nameof(ConstraintTextCollections))]
    public void Direct_policy_constraints_accept_valid_boundary_strings(string collectionName, int maximum)
    {
        var value = string.Concat(Enumerable.Repeat("\U0001F600", maximum));
        var constraints = CreateConstraints(collectionName, value);
        var json = new JsonObject { [collectionName] = new JsonArray(value) }.ToJsonString();

        Assert.NotNull(PolicySerializer.Serialize(constraints));
        Assert.NotNull(PolicySerializer.DeserializeStrict<PolicyConstraints>(json));

        foreach (var options in new[] { PolicySerializer.Options, PolicySerializer.StrictOptions })
        {
            Assert.NotNull(JsonSerializer.Serialize(constraints, options));
            Assert.NotNull(JsonSerializer.Deserialize<PolicyConstraints>(json, options));
        }
    }

    [Fact]
    public void Draft_rejects_server_managed_metadata()
    {
        var committed = JsonNode.Parse(
            File.ReadAllText(Path.Combine(SamplesDir, "corporate-allowlist.policy.json")))!;

        Assert.Throws<JsonException>(() => PolicyDraftDocument.ParseJson(committed.ToJsonString()));
    }

    private static PolicyDocument ParsePolicy(string path)
    {
        var content = File.ReadAllText(path);
        return PolicyDocument.ParseJson(content);
    }

    private static JsonNode ParseJsonString(string value) => JsonNode.Parse($"\"{value}\"")!;

    private static PolicyConstraints CreateConstraints(string collectionName, string value)
    {
        var constraints = new PolicyConstraints();
        switch (collectionName)
        {
            case nameof(PolicyConstraints.AllowedInstallLocationPatterns):
                constraints.AllowedInstallLocationPatterns = [value];
                break;
            case nameof(PolicyConstraints.AllowedCustomParameters):
                constraints.AllowedCustomParameters = [value];
                break;
            case nameof(PolicyConstraints.AllowedCustomParameterPatterns):
                constraints.AllowedCustomParameterPatterns = [value];
                break;
            case nameof(PolicyConstraints.DeniedCustomParameters):
                constraints.DeniedCustomParameters = [value];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(collectionName), collectionName, null);
        }

        return constraints;
    }

    private static bool? GetBooleanMatch(PolicyMatch match, string propertyName) =>
        propertyName switch
        {
            nameof(PolicyMatch.Interactive) => match.Interactive,
            nameof(PolicyMatch.SkipHashCheck) => match.SkipHashCheck,
            nameof(PolicyMatch.PreRelease) => match.PreRelease,
            nameof(PolicyMatch.HasCustomParameters) => match.HasCustomParameters,
            nameof(PolicyMatch.HasCustomInstallLocation) => match.HasCustomInstallLocation,
            nameof(PolicyMatch.HasPrePostCommands) => match.HasPrePostCommands,
            nameof(PolicyMatch.HasKillBeforeOperation) => match.HasKillBeforeOperation,
            nameof(PolicyMatch.HasUninstallPrevious) => match.HasUninstallPrevious,
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null),
        };

    private static int GetCollectionMatchCount(PolicyMatch match, string propertyName) =>
        propertyName switch
        {
            nameof(PolicyMatch.Operations) => match.Operations.Count,
            nameof(PolicyMatch.Managers) => match.Managers.Count,
            nameof(PolicyMatch.SourceNames) => match.SourceNames.Count,
            nameof(PolicyMatch.Scopes) => match.Scopes.Count,
            nameof(PolicyMatch.Architectures) => match.Architectures.Count,
            nameof(PolicyMatch.ExecutionElevation) => match.ExecutionElevation.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null),
        };

    private static string ResolvePolicyCrateRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "rust", "now-policy"));
    }

    private static string MinimalPolicyJson(string revision, string rules)
    {
        return $$"""
        {
            "PolicyFormatVersion": "1.0.0",
            "Metadata": {
                "Id": "test.policy",
                "Publisher": "Test",
        {{revision}}
                "PublishedAt": "2026-01-01T00:00:00Z"
            },
            "Enforcement": {
                "DefaultDecision": "Deny"
            },
        {{rules}}
        }
        """;
    }

    private static void RemoveProperty(JsonNode document, string propertyPath)
    {
        var segments = propertyPath.Split('.');
        var parent = document;
        foreach (var segment in segments[..^1])
        {
            parent = int.TryParse(segment, out var index)
                ? parent.AsArray()[index]!
                : parent[segment]!;
        }

        Assert.True(parent.AsObject().Remove(segments[^1]), $"missing fixture property {propertyPath}");
    }

    private static void SetPropertyToNull(JsonNode document, string propertyPath)
    {
        var segments = propertyPath.Split('.');
        var parent = document;
        foreach (var segment in segments[..^1])
        {
            parent = int.TryParse(segment, out var index)
                ? parent.AsArray()[index]!
                : parent[segment]!;
        }

        if (int.TryParse(segments[^1], out var finalIndex))
        {
            Assert.NotNull(parent.AsArray()[finalIndex]);
            parent.AsArray()[finalIndex] = null;
        }
        else
        {
            Assert.NotNull(parent.AsObject()[segments[^1]]);
            parent.AsObject()[segments[^1]] = null;
        }
    }
}