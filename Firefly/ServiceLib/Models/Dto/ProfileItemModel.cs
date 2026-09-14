namespace ServiceLib.Models.Dto;

[Serializable]
public partial class ProfileItemModel : ReactiveObject
{
    public bool IsActive { get; set; }
    public bool IsFireflyManaged { get; set; }
    public string IndexId { get; set; }
    public EConfigType ConfigType { get; set; }
    public string Remarks { get; set; }
    public string Address { get; set; }
    public int Port { get; set; }
    public string Network { get; set; }
    public string StreamSecurity { get; set; }
    public string Subid { get; set; }
    public string SubRemarks { get; set; }
    public int Sort { get; set; }

    public string FavoriteId { get; set; } = string.Empty;
    public string FavoriteAlias { get; set; } = string.Empty;
    public string FavoriteStatus { get; set; } = string.Empty;
    public bool IsFavorite => FavoriteId.IsNotEmpty();
    public bool CanFavorite => IsFavorite || (IndexId.IsNotEmpty() && !ConfigType.IsComplexType()
        && ConfigType != EConfigType.Outbound);
    public bool HasFavoriteAlias => FavoriteAlias.IsNotEmpty();
    public string FavoriteStar => IsFavorite ? "★" : "☆";
    public string FavoriteAction => IsFavorite ? "取消收藏" : "收藏节点";
    public string DisplayName => HasFavoriteAlias ? FavoriteAlias : Remarks;
    public string OriginalNameSuffix => HasFavoriteAlias ? Remarks : string.Empty;
    public string FavoriteTooltip => string.Join("\n", new[] { DisplayName,
        HasFavoriteAlias ? Remarks : string.Empty, FavoriteStatus }.Where(s => s.IsNotEmpty()));

    [Reactive]
    public partial int Delay { get; set; }

    public decimal Speed { get; set; }

    [Reactive]
    public partial string DelayVal { get; set; }

    [Reactive]
    public partial string SpeedVal { get; set; }

    [Reactive]
    public partial string IpInfo { get; set; }

    [Reactive]
    public partial string TodayUp { get; set; }

    [Reactive]
    public partial string TodayDown { get; set; }

    [Reactive]
    public partial string TotalUp { get; set; }

    [Reactive]
    public partial string TotalDown { get; set; }

    public string GetSummary()
    {
        var summary = $"[{ConfigType}] {Remarks}";
        if (!ConfigType.IsComplexType())
        {
            summary += $"({Address}:{Port})";
        }

        return summary;
    }
}
