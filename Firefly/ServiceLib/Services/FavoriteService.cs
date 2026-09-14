namespace ServiceLib.Services;

/// <summary>Owns a separate database so upstream node imports cannot erase bookmarks.</summary>
public sealed class FavoriteService
{
    private static readonly Lazy<FavoriteService> _instance = new(() =>
        new FavoriteService(Utils.GetConfigPath("palette-favorites.db")));
    public static FavoriteService Instance => _instance.Value;
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    internal FavoriteService(string databasePath) => _db = new SQLiteAsyncConnection(databasePath);

    private async Task EnterAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_initialized)
            {
                await _db.CreateTableAsync<FavoriteItem>();
                await _db.CreateTableAsync<FavoriteConnectionPin>();
                _initialized = true;
            }
        }
        catch { _gate.Release(); throw; }
    }

    public static string SourceKey(SubItem? sub) => sub is null ? "local" :
        FireflyManagedSubscriptionPolicy.IsManagedSubscription(sub)
            ? "managed:" + FireflyManagedSubscriptionPolicy.GetSourceId(sub)
            : "subscription:" + sub.Id;

    public static bool Supports(ProfileItem profile) => !profile.IsComplex()
        && profile.ConfigType != EConfigType.Outbound && profile.Address.IsNotEmpty() && profile.Port > 0;

    public async Task<FavoriteItem> AddAsync(ProfileItem profile, SubItem? sub)
    {
        if (!Supports(profile)) throw new ArgumentException("此类型节点暂不支持收藏。");
        var key = SourceKey(sub);
        var fingerprint = FavoriteFingerprint.Create(profile);
        await EnterAsync();
        try
        {
            var existing = await _db.Table<FavoriteItem>().FirstOrDefaultAsync(f =>
                f.SourceKey == key && f.ProfileFingerprint == fingerprint);
            if (existing is not null) return existing;
            var favorite = new FavoriteItem { SourceKey = key, Subid = profile.Subid };
            UpdateSnapshot(favorite, profile);
            await _db.InsertAsync(favorite);
            return favorite;
        }
        finally { _gate.Release(); }
    }

    private static void UpdateSnapshot(FavoriteItem favorite, ProfileItem profile)
    {
        favorite.OriginalName = profile.Remarks;
        favorite.ConfigType = profile.ConfigType;
        favorite.Subid = profile.Subid;
        favorite.LastKnownProfileId = profile.IndexId;
        favorite.ProfileFingerprint = FavoriteFingerprint.Create(profile);
        favorite.ProtectedSnapshot = FireflyManagedProfileStorage.Encrypt(JsonUtils.Serialize(profile, false));
    }

    internal static ProfileItem ReadSnapshot(FavoriteItem favorite) =>
        JsonUtils.Deserialize<ProfileItem>(FireflyManagedProfileStorage.Decrypt(favorite.ProtectedSnapshot))
            ?? throw new CryptographicException("Invalid favorite snapshot.");

    public async Task RemoveAsync(string id)
    {
        await EnterAsync();
        try
        {
            await _db.DeleteAsync<FavoriteItem>(id);
            var pin = await _db.FindAsync<FavoriteConnectionPin>("active");
            if (pin?.FavoriteId == id) await _db.DeleteAsync(pin);
        }
        finally { _gate.Release(); }
    }

    public async Task SetAliasAsync(string id, string alias)
    {
        await EnterAsync();
        try
        {
            var item = await _db.FindAsync<FavoriteItem>(id);
            if (item is null) return;
            item.Alias = alias.Trim()[..Math.Min(alias.Trim().Length, 80)];
            await _db.UpdateAsync(item);
        }
        finally { _gate.Release(); }
    }

    public async Task<FavoriteItem?> FindAsync(ProfileItem profile, SubItem? sub)
    {
        var key = SourceKey(sub);
        var fingerprint = FavoriteFingerprint.Create(profile);
        await EnterAsync();
        try { return await _db.Table<FavoriteItem>().FirstOrDefaultAsync(f =>
            f.SourceKey == key && f.ProfileFingerprint == fingerprint); }
        finally { _gate.Release(); }
    }

    // Keep an active bookmark's last approved configuration if its upstream row disappears.
    // This is only for the already-selected connection, never an automatic replacement.
    public async Task<ProfileItem?> GetRetainedProfileAsync(string indexId)
    {
        if (indexId.IsNullOrEmpty()) return null;
        await EnterAsync();
        try
        {
            var pin = await _db.FindAsync<FavoriteConnectionPin>("active");
            if (pin?.ProfileId == indexId)
                return JsonUtils.Deserialize<ProfileItem>(FireflyManagedProfileStorage.Decrypt(pin.ProtectedSnapshot));
            var item = await _db.Table<FavoriteItem>().FirstOrDefaultAsync(f => f.LastKnownProfileId == indexId);
            if (item is null) return null;
            var retained = ReadSnapshot(item);
            retained.IndexId = item.LastKnownProfileId;
            retained.Subid = item.Subid;
            retained.Remarks = item.OriginalName;
            return retained;
        }
        finally { _gate.Release(); }
    }

    public async Task<List<FavoriteMatch>> MatchAsync(IReadOnlyList<ProfileItem> profiles, IReadOnlyList<SubItem> subscriptions)
    {
        var sourceKeys = subscriptions.ToDictionary(sub => sub.Id, SourceKey);
        await EnterAsync();
        try
        {
            var results = new List<FavoriteMatch>();
            foreach (var favorite in await _db.Table<FavoriteItem>().OrderBy(f => f.CreatedUtc).ToListAsync())
            {
                ProfileItem original;
                try { original = ReadSnapshot(favorite); }
                catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
                {
                    results.Add(new(favorite, FavoriteState.Unreadable, null, []));
                    continue;
                }
                var sameSource = profiles.Where(p => Supports(p) &&
                    (p.Subid.IsNullOrEmpty() ? "local" : sourceKeys.GetValueOrDefault(p.Subid)) == favorite.SourceKey).ToList();
                var exact = sameSource.Where(p => FavoriteFingerprint.Create(p) == favorite.ProfileFingerprint)
                    .OrderByDescending(p => p.IndexId == favorite.LastKnownProfileId).ThenBy(p => p.IndexId, StringComparer.Ordinal).FirstOrDefault();
                if (exact is not null)
                {
                    // Preserve the original encrypted snapshot; metadata can follow renamed/recreated rows.
                    var changed = favorite.OriginalName != exact.Remarks || favorite.LastKnownProfileId != exact.IndexId
                        || favorite.Subid != exact.Subid;
                    favorite.OriginalName = exact.Remarks;
                    favorite.LastKnownProfileId = exact.IndexId;
                    favorite.Subid = exact.Subid;
                    if (changed) await _db.UpdateAsync(favorite);
                    results.Add(new(favorite, FavoriteState.Matched, exact, []));
                    continue;
                }
                var candidates = sameSource.Where(p =>
                    (favorite.OriginalName.IsNotEmpty() && p.Remarks == favorite.OriginalName)
                    || string.Equals(p.Address.Trim(), original.Address.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                results.Add(new(favorite, candidates.Count switch
                {
                    0 => FavoriteState.Missing, 1 => FavoriteState.Changed, _ => FavoriteState.Ambiguous
                }, null, candidates));
            }
            return results;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> AcceptAsync(string id, ProfileItem candidate, SubItem? sub, string expectedFingerprint)
    {
        if (FavoriteFingerprint.Create(candidate) != expectedFingerprint) return false;
        await EnterAsync();
        try
        {
            var item = await _db.FindAsync<FavoriteItem>(id);
            if (item is null || item.SourceKey != SourceKey(sub)) return false;
            var duplicate = await _db.Table<FavoriteItem>().FirstOrDefaultAsync(f => f.Id != id &&
                f.SourceKey == item.SourceKey && f.ProfileFingerprint == expectedFingerprint);
            if (duplicate is not null) return false;
            UpdateSnapshot(item, candidate);
            await _db.UpdateAsync(item);
            return true;
        }
        finally { _gate.Release(); }
    }

    internal Task CloseAsync() => _db.CloseAsync();

    public async Task TrackActiveAsync(string indexId, ProfileItem? profile, SubItem? sub)
    {
        await EnterAsync();
        try
        {
            var oldPin = await _db.FindAsync<FavoriteConnectionPin>("active");
            if (profile is null)
            {
                if (oldPin is not null && oldPin.ProfileId != indexId) await _db.DeleteAsync(oldPin);
                return;
            }
            var key = SourceKey(sub);
            var fingerprint = FavoriteFingerprint.Create(profile);
            var favorite = await _db.Table<FavoriteItem>().FirstOrDefaultAsync(f =>
                f.SourceKey == key && f.ProfileFingerprint == fingerprint);
            if (favorite is not null)
            {
                await _db.InsertOrReplaceAsync(new FavoriteConnectionPin
                {
                    FavoriteId = favorite.Id, ProfileId = indexId,
                    ProtectedSnapshot = FireflyManagedProfileStorage.Encrypt(JsonUtils.Serialize(profile, false))
                });
            }
            else if (oldPin is not null && oldPin.ProfileId != indexId) await _db.DeleteAsync(oldPin);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> IsApprovedFavoriteAsync(ProfileItem profile, SubItem? sub) =>
        await FindAsync(profile, sub) is not null ||
        (await GetRetainedProfileAsync(profile.IndexId) is { } retained && FavoriteFingerprint.SameConnection(retained, profile));
}
