namespace ServiceLib.ViewModels;

public partial class ProfilesViewModel
{
    public Interaction<string, string?> EditFavoriteAliasInteraction { get; } = new();
    public Interaction<FavoriteReviewRequest, string?> ReviewFavoriteInteraction { get; } = new();
    public ReactiveCommand<RxVoid, RxVoid> ToggleFavoriteCmd { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> EditFavoriteAliasCmd { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> ReviewFavoriteCmd { get; private set; } = null!;
    private readonly SemaphoreSlim _favoriteActionGate = new(1, 1);

    [Reactive]
    public partial bool FavoritesOnly { get; set; }

    private void InitializeFavorites()
    {
        ToggleFavoriteCmd = ReactiveCommand.CreateFromTask(() => ToggleFavoriteAsync(SelectedProfile),
            this.WhenAnyValue(x => x.SelectedProfile, p => p?.CanFavorite == true));
        EditFavoriteAliasCmd = ReactiveCommand.CreateFromTask(EditFavoriteAliasAsync,
            this.WhenAnyValue(x => x.SelectedProfile, p => p?.IsFavorite == true));
        ReviewFavoriteCmd = ReactiveCommand.CreateFromTask(ReviewFavoriteAsync,
            this.WhenAnyValue(x => x.SelectedProfile, p => p?.IsFavorite == true && p.FavoriteStatus.IsNotEmpty()));
        foreach (var command in new[] { ToggleFavoriteCmd, EditFavoriteAliasCmd, ReviewFavoriteCmd })
            command.ThrownExceptions.Subscribe(ex =>
            {
                Logging.SaveLog("Favorites", ex);
                NoticeManager.Instance.Enqueue("收藏操作失败，请重试或检查本地数据目录。");
            });
        this.WhenAnyValue(x => x.FavoritesOnly).Skip(1).Subscribe(_ => RefreshServersRequested.Publish());
    }

    public async Task ToggleFavoriteAsync(ProfileItemModel? row)
    {
        if (row?.CanFavorite != true || !await _favoriteActionGate.WaitAsync(0)) return;
        try
        {
            if (row.IsFavorite) await FavoriteService.Instance.RemoveAsync(row.FavoriteId);
            else
            {
                var profile = await AppManager.Instance.GetProfileItem(row.IndexId);
                if (profile is null) { NoticeManager.Instance.Enqueue("节点已更新，请刷新列表后再收藏。"); return; }
                await FavoriteService.Instance.AddAsync(profile, await AppManager.Instance.GetSubItem(profile.Subid));
                if (_config.IndexId == profile.IndexId)
                    await FavoriteService.Instance.TrackActiveAsync(profile.IndexId, profile, await AppManager.Instance.GetSubItem(profile.Subid));
            }
            _pendingSelectIndexId = row.IndexId;
            await RefreshServers();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Favorites", ex);
            NoticeManager.Instance.Enqueue("收藏保存失败，请检查本地数据目录。");
        }
        finally { _favoriteActionGate.Release(); }
    }

    private async Task EditFavoriteAliasAsync()
    {
        var row = SelectedProfile;
        if (row?.IsFavorite != true) return;
        var alias = await EditFavoriteAliasInteraction.Handle(row.FavoriteAlias);
        if (alias is null) return;
        await FavoriteService.Instance.SetAliasAsync(row.FavoriteId, alias);
        await RefreshServers();
    }

    private async Task ReviewFavoriteAsync()
    {
        var row = SelectedProfile;
        if (row?.IsFavorite != true) return;
        var profiles = await AppManager.Instance.ProfileItems(string.Empty) ?? [];
        var matches = await FavoriteService.Instance.MatchAsync(profiles, await AppManager.Instance.SubItems() ?? []);
        var match = matches.FirstOrDefault(m => m.Favorite.Id == row.FavoriteId);
        if (match is null || match.Candidates.Count == 0)
        {
            NoticeManager.Instance.Enqueue(match?.StatusText.IsNotEmpty() == true ? match.StatusText : "收藏配置已重新匹配。");
            await RefreshServers();
            return;
        }
        var original = FavoriteService.ReadSnapshot(match.Favorite);
        var request = new FavoriteReviewRequest(row.DisplayName, match.Candidates.Select(p =>
            new FavoriteCandidate(p.IndexId, p.Remarks, FavoriteFingerprint.DescribeChanges(original, p))).ToList());
        var candidateId = await ReviewFavoriteInteraction.Handle(request);
        if (candidateId is null) return;
        var displayed = match.Candidates.FirstOrDefault(p => p.IndexId == candidateId);
        var current = await AppManager.Instance.GetProfileItem(candidateId);
        if (displayed is null || current is null || !await FavoriteService.Instance.AcceptAsync(
            row.FavoriteId, current, await AppManager.Instance.GetSubItem(current.Subid), FavoriteFingerprint.Create(displayed)))
        {
            NoticeManager.Instance.Enqueue("候选配置已更新，或已在收藏中，请重新查看。");
        }
        else NoticeManager.Instance.Enqueue("已更新收藏配置；双击节点可连接。");
        await RefreshServers();
    }

    private async Task<List<ProfileItemModel>> ApplyFavoritesAsync(List<ProfileItemModel> rows, string subid, string filter)
    {
        var matches = await FavoriteService.Instance.MatchAsync(
            await AppManager.Instance.ProfileItems(string.Empty) ?? [], await AppManager.Instance.SubItems() ?? []);
        foreach (var match in matches)
        {
            var row = match.Profile is null ? null : rows.FirstOrDefault(p => p.IndexId == match.Profile.IndexId);
            if (row is null && match.Profile is null && (subid.IsNullOrEmpty() || subid == match.Favorite.Subid))
            {
                row = new ProfileItemModel
                {
                    IndexId = string.Empty, Remarks = match.Favorite.OriginalName,
                    Subid = match.Favorite.Subid, Sort = int.MaxValue, FavoriteStatus = match.StatusText,
                    ConfigType = match.Favorite.ConfigType, IsActive = match.Favorite.LastKnownProfileId == _config.IndexId
                };
                rows.Add(row);
            }
            if (row is null) continue;
            row.FavoriteId = match.Favorite.Id;
            row.FavoriteAlias = match.Favorite.Alias;
        }
        return FilterFavoriteRows(rows, FavoritesOnly, filter);
    }

    internal static List<ProfileItemModel> FilterFavoriteRows(IEnumerable<ProfileItemModel> rows, bool favoritesOnly, string? filter) =>
        rows.Where(p => (!favoritesOnly || p.IsFavorite) &&
            (string.IsNullOrWhiteSpace(filter) || p.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (p.Remarks?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();
}
