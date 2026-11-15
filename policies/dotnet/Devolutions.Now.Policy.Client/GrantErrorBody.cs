using System.Text.Json;

using Devolutions.Now.Policy.Api;

namespace Devolutions.Now.Policy.Client;

/// <summary>
/// Strict check of an untrusted <c>ErrorResponse</c> body returned by the grant route. The shape
/// is the grant-route error contract: no unknown members, and neither <c>Validation</c> nor
/// <c>Management</c>, even as null. The body is never copied into a message.
/// </summary>
internal static class GrantErrorBody
{
    private static readonly string[] s_required = ["ResponseKind", "ResponseVersion", "Server", "Code", "Message"];
    private static readonly string[] s_optional = ["Details"];
    private static readonly string[] s_transports = ["HttpNamedPipe", "HttpLoopbackSimulator"];

    internal static bool TryParse(string? body, out ErrorCode code)
    {
        code = default;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!IsValidShape(document.RootElement))
            {
                return false;
            }

            // The typed read rejects duplicate members at every level and yields the code.
            var error = BrokerSerializer.DeserializeStrict<ErrorResponse>(body);
            if (error is null
                || !document.RootElement.GetProperty("Code").ValueEquals(error.Code.ToString()))
            {
                return false;
            }

            code = error.Code;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidShape(JsonElement root)
    {
        if (!TryMembers(root, s_required, s_optional, out var members))
        {
            return false;
        }

        if (!IsString(members["ResponseKind"], out var kind) || kind != BrokerApi.ErrorResponseKind
            || !IsString(members["ResponseVersion"], out var version) || version != BrokerApi.Version
            || !IsString(members["Code"], out _)
            || !IsString(members["Message"], out var message) || !LengthWithin(message, 1, 2048))
        {
            return false;
        }

        if (!TryMembers(members["Server"], ["ServerVersion", "Transport"], [], out var server)
            || !IsString(server["ServerVersion"], out var serverVersion) || !LengthWithin(serverVersion, 1, 128)
            || !IsString(server["Transport"], out var transport) || !s_transports.Contains(transport, StringComparer.Ordinal))
        {
            return false;
        }

        if (!members.TryGetValue("Details", out var details))
        {
            return true;
        }

        if (details.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var detail in details.EnumerateArray())
        {
            if (!TryMembers(detail, ["Message"], ["Code", "Path"], out var detailMembers)
                || !IsString(detailMembers["Message"], out var detailMessage) || !LengthWithin(detailMessage, 1, 2048)
                || !IsOptionalString(detailMembers, "Code", 128)
                || !IsOptionalString(detailMembers, "Path", 512))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMembers(
        JsonElement element,
        string[] required,
        string[] optional,
        out Dictionary<string, JsonElement> members)
    {
        members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name, StringComparer.Ordinal)
                && !optional.Contains(property.Name, StringComparer.Ordinal))
            {
                return false;
            }

            members[property.Name] = property.Value;
        }

        // Required members must be present and non-null.
        foreach (var name in required)
        {
            if (!members.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsString(JsonElement element, out string value)
    {
        value = element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
        return element.ValueKind == JsonValueKind.String;
    }

    private static bool IsOptionalString(Dictionary<string, JsonElement> members, string name, int max)
    {
        if (!members.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return IsString(element, out var value) && LengthWithin(value, 0, max);
    }

    private static bool LengthWithin(string value, int min, int max)
    {
        var length = value.EnumerateRunes().Count();
        return length >= min && length <= max;
    }
}