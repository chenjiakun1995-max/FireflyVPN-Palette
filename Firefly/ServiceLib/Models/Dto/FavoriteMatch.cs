namespace ServiceLib.Models.Dto;

public enum FavoriteState { Matched, Missing, Changed, Ambiguous, Unreadable }

public sealed record FavoriteMatch(FavoriteItem Favorite, FavoriteState State,
    ProfileItem? Profile, IReadOnlyList<ProfileItem> Candidates)
{
    public string StatusText => State switch
    {
        FavoriteState.Missing => "订阅中已找不到",
        FavoriteState.Changed => "发现新配置 · 待确认",
        FavoriteState.Ambiguous => "多个候选 · 待确认",
        FavoriteState.Unreadable => "收藏数据无法解密",
        _ => string.Empty
    };
}

public sealed record FavoriteCandidate(string IndexId, string Name, string Changes)
{
    public string DisplayText => $"{Name} — {Changes}";
}

public sealed record FavoriteReviewRequest(string Title, IReadOnlyList<FavoriteCandidate> Candidates);
