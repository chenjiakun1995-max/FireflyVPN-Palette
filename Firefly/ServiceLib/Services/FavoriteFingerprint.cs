namespace ServiceLib.Services;

/// <summary>Versioned connection identity; never use display names or database row IDs.</summary>
public static class FavoriteFingerprint
{
    private static readonly string[] Metadata =
    [
        nameof(ProfileItem.IndexId), nameof(ProfileItem.Subid), nameof(ProfileItem.IsSub),
        nameof(ProfileItem.Remarks), nameof(ProfileItem.DisplayLog),
        // These legacy fields have canonical equivalents in ProtoExtra/TransportExtra.
        "HeaderType", "RequestHost", "Path", "Extra", "Ports", "AlterId", "Flow", "Id", "Security"
    ];

    internal static JsonObject Connection(ProfileItem profile)
    {
        var node = JsonSerializer.SerializeToNode(profile)!.AsObject();
        foreach (var key in Metadata) node.Remove(key);
        node[nameof(ProfileItem.Address)] = NormalizeHost(profile.Address);
        node[nameof(ProfileItem.Sni)] = NormalizeHost(profile.Sni);
        node[nameof(ProfileItem.Network)] = profile.GetNetwork();
        node[nameof(ProfileItem.AllowInsecure)] = profile.GetAllowInsecure();
        node[nameof(ProfileItem.ProtoExtra)] = NormalizeExtra(profile.ProtoExtra, JsonSerializer.SerializeToNode(profile.GetProtocolExtra())!);
        node[nameof(ProfileItem.TransportExtra)] = NormalizeExtra(profile.TransportExtra, JsonSerializer.SerializeToNode(profile.GetTransportExtra())!);
        return node;
    }

    private static JsonNode NormalizeExtra(string? raw, JsonNode known)
    {
        // Retain fields introduced by a newer backend instead of silently ignoring them.
        if (!string.IsNullOrWhiteSpace(raw) && JsonNode.Parse(raw) is JsonObject original)
            foreach (var property in original)
                if (!known.AsObject().ContainsKey(property.Key)) known[property.Key] = property.Value?.DeepClone();
        return known;
    }

    private static string NormalizeHost(string? value)
    {
        var host = value?.Trim() ?? string.Empty;
        return IPAddress.TryParse(host, out var ip) ? ip.ToString() : host.ToLowerInvariant();
    }

    private static JsonNode Canonicalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, Canonicalize(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(value => (JsonNode?)Canonicalize(value)).ToArray()),
        null => JsonValue.Create(string.Empty)!,
        _ => node.DeepClone()
    };

    public static string Create(ProfileItem profile) => "v1:" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(Connection(profile)).ToJsonString())));

    public static bool SameConnection(ProfileItem a, ProfileItem b) => Create(a) == Create(b);

    public static string DescribeChanges(ProfileItem original, ProfileItem candidate)
    {
        var before = Connection(original);
        var after = Connection(candidate);
        var changed = before.Where(pair => !JsonNode.DeepEquals(Canonicalize(pair.Value), Canonicalize(after[pair.Key])))
            .Select(pair => pair.Key).ToHashSet();
        var labels = new List<string>();
        if (changed.Remove(nameof(ProfileItem.Address))) labels.Add("服务器地址");
        if (changed.Remove(nameof(ProfileItem.Port))) labels.Add("端口");
        if (changed.Remove(nameof(ProfileItem.Password)) | changed.Remove(nameof(ProfileItem.Username))) labels.Add("认证信息");
        if (changed.Remove(nameof(ProfileItem.ConfigType)) | changed.Remove(nameof(ProfileItem.ProtoExtra))) labels.Add("协议参数");
        if (changed.Remove(nameof(ProfileItem.Network)) | changed.Remove(nameof(ProfileItem.TransportExtra))) labels.Add("传输参数");
        if (changed.Count > 0) labels.Add("TLS 或其他连接参数");
        return labels.Count == 0 ? "连接配置一致" : string.Join("、", labels) + "已变化";
    }
}
