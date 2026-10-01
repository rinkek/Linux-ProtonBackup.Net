using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtonBackup.Core;

/// One line from `filesystem list --json`. Names are a Result type because decryption can fail.
public sealed class RemoteNode
{
    [JsonPropertyName("uid")] public string Uid { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("name")] public RemoteValue<string>? Name { get; init; }
    [JsonPropertyName("activeRevision")] public RemoteRevision? ActiveRevision { get; init; }

    public bool IsFolder => Type == "folder";
    public string? FileName => Name is { Ok: true } ? Name.Value : null;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<RemoteNode> ParseList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<List<RemoteNode>>(json, Options) ?? [];
    }
}

public sealed class RemoteValue<T>
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("value")] public T? Value { get; init; }
}

public sealed class RemoteRevision
{
    [JsonPropertyName("claimedSize")] public long? ClaimedSize { get; init; }
    [JsonPropertyName("claimedModificationTime")] public DateTimeOffset? ClaimedModificationTime { get; init; }
}
