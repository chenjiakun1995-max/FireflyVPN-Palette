namespace ServiceLib.Models.Entities;

/// <summary>A local bookmark. Subscription refreshes never own or delete this row.</summary>
public class FavoriteItem
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceKey { get; set; } = string.Empty;
    public string Subid { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public EConfigType ConfigType { get; set; }
    public string ProfileFingerprint { get; set; } = string.Empty;
    public string ProtectedSnapshot { get; set; } = string.Empty;
    public string LastKnownProfileId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
