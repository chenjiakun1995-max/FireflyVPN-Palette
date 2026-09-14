namespace ServiceLib.Models.Entities;

/// <summary>The running selection is independent of a bookmark being edited.</summary>
public class FavoriteConnectionPin
{
    [PrimaryKey]
    public string Id { get; set; } = "active";
    public string FavoriteId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProtectedSnapshot { get; set; } = string.Empty;
}
