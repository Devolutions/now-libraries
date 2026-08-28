using System.Text.Json;
using System.Text.Json.Serialization;

namespace Devolutions.Now.Policy.Model;

public static class SchemaUris
{
    public const string Policy = "https://devolutions.net/schemas/now-policy.schema.1.0.json";
}

/// <summary>A policy document governing which package operations are allowed or denied.</summary>
public sealed class PolicyDocument
{
    [JsonPropertyName("$schema")]
    [JsonRequired]
    public string Schema { get; set; } = SchemaUris.Policy;

    [JsonPropertyName("PolicyVersion")]
    [JsonRequired]
    public string PolicyVersion { get; set; } = "1.0.0";

    [JsonPropertyName("PolicyType")]
    [JsonRequired]
    public string PolicyType { get; set; } = "PackageBrokerPolicy";

    [JsonPropertyName("Metadata")]
    [JsonRequired]
    public PolicyMetadata Metadata { get; set; } = new();

    [JsonPropertyName("Enforcement")]
    [JsonRequired]
    public PolicyEnforcement Enforcement { get; set; } = new();

    [JsonPropertyName("Rules")]
    [JsonRequired]
    public List<PolicyRule> Rules { get; set; } = [];

    public static PolicyDocument Create(string id, string publisher, Decision defaultDecision = Decision.Deny)
    {
        return new PolicyDocument
        {
            Metadata = new PolicyMetadata
            {
                Id = id,
                Publisher = publisher,
                Revision = 1,
                PublishedAt = DateTimeOffset.UtcNow,
            },
            Enforcement = new PolicyEnforcement
            {
                DefaultDecision = defaultDecision,
                RulePrecedence = RulePrecedence.PriorityThenDeny,
            },
        };
    }

    public static PolicyDocument ParseJson(string json)
    {
        return PolicyJson.DeserializePolicyDocumentStrict(json)
            ?? throw new JsonException("policy document was null");
    }

    public PolicyDraftDocument ToDraft()
    {
        return new PolicyDraftDocument
        {
            Schema = Schema,
            PolicyVersion = PolicyVersion,
            PolicyType = PolicyType,
            Metadata = PolicyModelClone.ToDraftMetadata(Metadata),
            Enforcement = PolicyModelClone.Enforcement(Enforcement),
            Rules = PolicyModelClone.Rules(Rules),
        };
    }

    public string ToJson() => PolicyJson.Serialize(this);
}

/// <summary>An editable policy document without server-managed commit metadata.</summary>
public sealed class PolicyDraftDocument
{
    [JsonPropertyName("$schema")]
    [JsonRequired]
    public string Schema { get; set; } = SchemaUris.Policy;

    [JsonPropertyName("PolicyVersion")]
    [JsonRequired]
    public string PolicyVersion { get; set; } = "1.0.0";

    [JsonPropertyName("PolicyType")]
    [JsonRequired]
    public string PolicyType { get; set; } = "PackageBrokerPolicy";

    [JsonPropertyName("Metadata")]
    [JsonRequired]
    public PolicyDraftMetadata Metadata { get; set; } = new();

    [JsonPropertyName("Enforcement")]
    [JsonRequired]
    public PolicyEnforcement Enforcement { get; set; } = new();

    [JsonPropertyName("Rules")]
    [JsonRequired]
    public List<PolicyRule> Rules { get; set; } = [];

    public static PolicyDraftDocument Create(
        string id,
        string publisher,
        Decision defaultDecision = Decision.Deny)
    {
        return new PolicyDraftDocument
        {
            Metadata = new PolicyDraftMetadata
            {
                Id = id,
                Publisher = publisher,
            },
            Enforcement = new PolicyEnforcement
            {
                DefaultDecision = defaultDecision,
                RulePrecedence = RulePrecedence.PriorityThenDeny,
            },
        };
    }

    public static PolicyDraftDocument ParseJson(string json)
    {
        return PolicyJson.DeserializePolicyDraftDocumentStrict(json)
            ?? throw new JsonException("policy draft document was null");
    }

    public PolicyDocument ToPolicyDocument(uint revision, DateTimeOffset publishedAt)
    {
        if (revision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Policy revisions start at 1.");
        }

        return new PolicyDocument
        {
            Schema = Schema,
            PolicyVersion = PolicyVersion,
            PolicyType = PolicyType,
            Metadata = PolicyModelClone.ToCommittedMetadata(Metadata, revision, publishedAt),
            Enforcement = PolicyModelClone.Enforcement(Enforcement),
            Rules = PolicyModelClone.Rules(Rules),
        };
    }

    public string ToJson() => PolicyJson.Serialize(this);
}

public sealed class PolicyMetadata
{
    [JsonPropertyName("Id")]
    [JsonRequired]
    public string Id { get; set; } = "";

    [JsonPropertyName("Publisher")]
    [JsonRequired]
    public string Publisher { get; set; } = "";

    [JsonPropertyName("Revision")]
    [JsonRequired]
    public uint Revision { get; set; }

    [JsonPropertyName("PublishedAt")]
    [JsonRequired]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonPropertyName("ValidFrom")]
    public DateTimeOffset? ValidFrom { get; set; }

    [JsonPropertyName("ValidUntil")]
    public DateTimeOffset? ValidUntil { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("SupportUrl")]
    public string? SupportUrl { get; set; }
}

public sealed class PolicyDraftMetadata
{
    [JsonPropertyName("Id")]
    [JsonRequired]
    public string Id { get; set; } = "";

    [JsonPropertyName("Publisher")]
    [JsonRequired]
    public string Publisher { get; set; } = "";

    [JsonPropertyName("ValidFrom")]
    public DateTimeOffset? ValidFrom { get; set; }

    [JsonPropertyName("ValidUntil")]
    public DateTimeOffset? ValidUntil { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("SupportUrl")]
    public string? SupportUrl { get; set; }
}

public sealed class PolicyEnforcement
{
    [JsonPropertyName("DefaultDecision")]
    [JsonRequired]
    public Decision DefaultDecision { get; set; }

    [JsonPropertyName("RulePrecedence")]
    [JsonRequired]
    public RulePrecedence RulePrecedence { get; set; }

    [JsonPropertyName("AuditMode")]
    public bool? AuditMode { get; set; }
}

public sealed class PolicyRule
{
    [JsonPropertyName("Id")]
    [JsonRequired]
    public string Id { get; set; } = "";

    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("Priority")]
    [JsonRequired]
    public uint Priority { get; set; }

    [JsonPropertyName("Decision")]
    [JsonRequired]
    public Decision Decision { get; set; }

    [JsonPropertyName("Reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("Match")]
    [JsonRequired]
    public PolicyMatch Match { get; set; } = new();

    [JsonPropertyName("Constraints")]
    public PolicyConstraints? Constraints { get; set; }
}

public sealed class PolicyMatch
{
    [JsonPropertyName("Operations")]
    public List<Operation> Operations { get; set; } = [];

    [JsonPropertyName("Managers")]
    public List<ManagerName> Managers { get; set; } = [];

    [JsonPropertyName("Sources")]
    public List<string> Sources { get; set; } = [];

    [JsonPropertyName("PackageIdentifiers")]
    public List<string> PackageIdentifiers { get; set; } = [];

    [JsonPropertyName("PackageNames")]
    public List<string> PackageNames { get; set; } = [];

    [JsonPropertyName("Versions")]
    public List<string> Versions { get; set; } = [];

    [JsonPropertyName("VersionRange")]
    public VersionRange? VersionRange { get; set; }

    [JsonPropertyName("Scopes")]
    public List<Scope> Scopes { get; set; } = [];

    [JsonPropertyName("Architectures")]
    public List<Architecture> Architectures { get; set; } = [];

    [JsonPropertyName("Elevation")]
    public List<Elevation> Elevation { get; set; } = [];

    [JsonPropertyName("Interactive")]
    public List<bool> Interactive { get; set; } = [];

    [JsonPropertyName("SkipHashCheck")]
    public List<bool> SkipHashCheck { get; set; } = [];

    [JsonPropertyName("PreRelease")]
    public List<bool> PreRelease { get; set; } = [];

    [JsonPropertyName("HasCustomParameters")]
    public List<bool> HasCustomParameters { get; set; } = [];

    [JsonPropertyName("HasCustomInstallLocation")]
    public List<bool> HasCustomInstallLocation { get; set; } = [];

    [JsonPropertyName("HasPrePostCommands")]
    public List<bool> HasPrePostCommands { get; set; } = [];

    [JsonPropertyName("HasKillBeforeOperation")]
    public List<bool> HasKillBeforeOperation { get; set; } = [];

    [JsonPropertyName("HasUninstallPrevious")]
    public List<bool> HasUninstallPrevious { get; set; } = [];
}

public sealed class VersionRange
{
    [JsonPropertyName("MinVersion")]
    public string? MinVersion { get; set; }

    [JsonPropertyName("MaxVersion")]
    public string? MaxVersion { get; set; }

    [JsonPropertyName("IncludePrerelease")]
    public bool IncludePrerelease { get; set; }
}

public sealed class PolicyConstraints
{
    [JsonPropertyName("AllowInteractive")]
    public bool AllowInteractive { get; set; } = true;

    [JsonPropertyName("AllowSkipHashCheck")]
    public bool AllowSkipHashCheck { get; set; } = true;

    [JsonPropertyName("AllowPreRelease")]
    public bool AllowPreRelease { get; set; } = true;

    [JsonPropertyName("AllowCustomInstallLocation")]
    public bool AllowCustomInstallLocation { get; set; } = true;

    [JsonPropertyName("AllowedInstallLocationPatterns")]
    public List<string> AllowedInstallLocationPatterns { get; set; } = [];

    [JsonPropertyName("AllowCustomParameters")]
    public bool AllowCustomParameters { get; set; } = true;

    [JsonPropertyName("AllowedCustomParameters")]
    public List<string> AllowedCustomParameters { get; set; } = [];

    [JsonPropertyName("AllowedCustomParameterPatterns")]
    public List<string> AllowedCustomParameterPatterns { get; set; } = [];

    [JsonPropertyName("DeniedCustomParameters")]
    public List<string> DeniedCustomParameters { get; set; } = [];

    [JsonPropertyName("AllowPrePostCommands")]
    public bool AllowPrePostCommands { get; set; } = true;

    [JsonPropertyName("AllowKillBeforeOperation")]
    public bool AllowKillBeforeOperation { get; set; } = true;

    [JsonPropertyName("AllowUninstallPrevious")]
    public bool AllowUninstallPrevious { get; set; } = true;

    [JsonPropertyName("AllowUpgrade")]
    public bool AllowUpgrade { get; set; } = true;
}

internal static class PolicyModelClone
{
    internal static PolicyDraftMetadata ToDraftMetadata(PolicyMetadata value) => new()
    {
        Id = value.Id,
        Publisher = value.Publisher,
        ValidFrom = value.ValidFrom,
        ValidUntil = value.ValidUntil,
        Description = value.Description,
        SupportUrl = value.SupportUrl,
    };

    internal static PolicyMetadata ToCommittedMetadata(
        PolicyDraftMetadata value,
        uint revision,
        DateTimeOffset publishedAt) => new()
        {
            Id = value.Id,
            Publisher = value.Publisher,
            Revision = revision,
            PublishedAt = publishedAt,
            ValidFrom = value.ValidFrom,
            ValidUntil = value.ValidUntil,
            Description = value.Description,
            SupportUrl = value.SupportUrl,
        };

    internal static PolicyEnforcement Enforcement(PolicyEnforcement value) => new()
    {
        DefaultDecision = value.DefaultDecision,
        RulePrecedence = value.RulePrecedence,
        AuditMode = value.AuditMode,
    };

    internal static List<PolicyRule> Rules(IEnumerable<PolicyRule> values) => values.Select(Rule).ToList();

    private static PolicyRule Rule(PolicyRule value) => new()
    {
        Id = value.Id,
        Enabled = value.Enabled,
        Priority = value.Priority,
        Decision = value.Decision,
        Reason = value.Reason,
        Match = Match(value.Match),
        Constraints = value.Constraints is null ? null : Constraints(value.Constraints),
    };

    private static PolicyMatch Match(PolicyMatch value) => new()
    {
        Operations = [.. value.Operations],
        Managers = [.. value.Managers],
        Sources = [.. value.Sources],
        PackageIdentifiers = [.. value.PackageIdentifiers],
        PackageNames = [.. value.PackageNames],
        Versions = [.. value.Versions],
        VersionRange = value.VersionRange is null
            ? null
            : new VersionRange
            {
                MinVersion = value.VersionRange.MinVersion,
                MaxVersion = value.VersionRange.MaxVersion,
                IncludePrerelease = value.VersionRange.IncludePrerelease,
            },
        Scopes = [.. value.Scopes],
        Architectures = [.. value.Architectures],
        Elevation = [.. value.Elevation],
        Interactive = [.. value.Interactive],
        SkipHashCheck = [.. value.SkipHashCheck],
        PreRelease = [.. value.PreRelease],
        HasCustomParameters = [.. value.HasCustomParameters],
        HasCustomInstallLocation = [.. value.HasCustomInstallLocation],
        HasPrePostCommands = [.. value.HasPrePostCommands],
        HasKillBeforeOperation = [.. value.HasKillBeforeOperation],
        HasUninstallPrevious = [.. value.HasUninstallPrevious],
    };

    private static PolicyConstraints Constraints(PolicyConstraints value) => new()
    {
        AllowInteractive = value.AllowInteractive,
        AllowSkipHashCheck = value.AllowSkipHashCheck,
        AllowPreRelease = value.AllowPreRelease,
        AllowCustomInstallLocation = value.AllowCustomInstallLocation,
        AllowedInstallLocationPatterns = [.. value.AllowedInstallLocationPatterns],
        AllowCustomParameters = value.AllowCustomParameters,
        AllowedCustomParameters = [.. value.AllowedCustomParameters],
        AllowedCustomParameterPatterns = [.. value.AllowedCustomParameterPatterns],
        DeniedCustomParameters = [.. value.DeniedCustomParameters],
        AllowPrePostCommands = value.AllowPrePostCommands,
        AllowKillBeforeOperation = value.AllowKillBeforeOperation,
        AllowUninstallPrevious = value.AllowUninstallPrevious,
        AllowUpgrade = value.AllowUpgrade,
    };
}