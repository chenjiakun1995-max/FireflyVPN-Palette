using AwesomeAssertions;
using ServiceLib.ViewModels;
using Xunit;

namespace ServiceLib.Tests.Services;

public sealed class FavoriteServiceTests : IAsyncDisposable
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "firefly-favorites-tests", Guid.NewGuid().ToString("N"));
    private readonly FavoriteService _service;
    private readonly SubItem _source = new() { Id = "subscription-a", Memo = "firefly-worker:source-a" };

    public FavoriteServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _service = new FavoriteService(Path.Combine(_directory, "favorites.db"));
    }

    private ProfileItem Node(string id = "old-id") => new()
    {
        IndexId = id, Subid = _source.Id, Remarks = "美国1中负载临时", ConfigType = EConfigType.VLESS,
        Address = "node.example.com", Port = 443, Password = "secret-pass-marker", Network = "tcp"
    };

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task RenamedAndRecreatedNode_PreservesBookmarkAndAlias_AfterReopeningDatabase()
    {
        var favorite = await _service.AddAsync(Node(), _source);
        await _service.SetAliasAsync(favorite.Id, "家里主用");
        var renamed = Node("new-id"); renamed.Remarks = "美国1高负载临时";
        var match = (await _service.MatchAsync([renamed], [_source])).Single();
        match.State.Should().Be(FavoriteState.Matched);
        match.Favorite.Id.Should().Be(favorite.Id);
        match.Favorite.Alias.Should().Be("家里主用");
        match.Favorite.OriginalName.Should().Be(renamed.Remarks);
        await _service.CloseAsync();
        var reopened = new FavoriteService(Path.Combine(_directory, "favorites.db"));
        try
        {
            var persisted = (await reopened.MatchAsync([renamed], [_source])).Single();
            persisted.Favorite.Alias.Should().Be("家里主用");
            (await reopened.GetRetainedProfileAsync("new-id"))!.IndexId.Should().Be("new-id");
        }
        finally { await reopened.CloseAsync(); }
    }

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task ChangedCredentialsAndPort_RequireAcceptance_WithoutChangingTheActiveConnection()
    {
        var original = Node();
        var favorite = await _service.AddAsync(original, _source);
        await _service.SetAliasAsync(favorite.Id, "主用");
        await _service.TrackActiveAsync(original.IndexId, original, _source);
        var changed = Node("replacement"); changed.Port = 8443; changed.Password = "rotated-password";
        var match = (await _service.MatchAsync([changed], [_source])).Single();
        match.State.Should().Be(FavoriteState.Changed);
        match.Profile.Should().BeNull();
        match.Favorite.ProfileFingerprint.Should().Be(FavoriteFingerprint.Create(original));
        FavoriteFingerprint.DescribeChanges(original, changed).Should().Be("端口、认证信息已变化");
        (await _service.AcceptAsync(favorite.Id, changed, _source, FavoriteFingerprint.Create(changed))).Should().BeTrue();
        var approved = (await _service.MatchAsync([changed], [_source])).Single();
        approved.State.Should().Be(FavoriteState.Matched);
        approved.Favorite.Alias.Should().Be("主用");
        (await _service.GetRetainedProfileAsync(original.IndexId))!.Port.Should().Be(443);
        (await _service.IsApprovedFavoriteAsync(original, _source)).Should().BeTrue();
        await _service.TrackActiveAsync(changed.IndexId, changed, _source);
        (await _service.GetRetainedProfileAsync(changed.IndexId))!.Port.Should().Be(8443);
        (await _service.GetRetainedProfileAsync(original.IndexId)).Should().BeNull();
    }

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task MissingNode_RemainsSaved_AndReappearsWithoutLosingItsAlias()
    {
        var favorite = await _service.AddAsync(Node(), _source);
        await _service.SetAliasAsync(favorite.Id, "备用");
        var missing = (await _service.MatchAsync([], [_source])).Single();
        missing.State.Should().Be(FavoriteState.Missing);
        missing.Favorite.Alias.Should().Be("备用");
        (await _service.MatchAsync([Node("returned")], [_source])).Single().State.Should().Be(FavoriteState.Matched);
    }

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task SameNameAndAddressFromDifferentSource_MustNotMatch()
    {
        await _service.AddAsync(Node(), _source);
        var other = new SubItem { Id = "subscription-b", Memo = "firefly-worker:source-b" };
        var node = Node(); node.Subid = other.Id;
        var match = (await _service.MatchAsync([node], [_source, other])).Single();
        match.State.Should().Be(FavoriteState.Missing);
        match.Candidates.Should().BeEmpty();
    }

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task MultipleChangedCandidates_AreNeverChosenAutomatically()
    {
        await _service.AddAsync(Node(), _source);
        var one = Node("one"); one.Port = 8443;
        var two = Node("two"); two.Port = 9443;
        var match = (await _service.MatchAsync([one, two], [_source])).Single();
        match.State.Should().Be(FavoriteState.Ambiguous);
        match.Profile.Should().BeNull();
        match.Candidates.Should().HaveCount(2);
    }

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task CandidateChangedDuringReview_IsRejected()
    {
        var favorite = await _service.AddAsync(Node(), _source);
        var candidate = Node("new"); candidate.Port = 8443;
        var displayedFingerprint = FavoriteFingerprint.Create(candidate);
        candidate.Password = "changed-again";
        (await _service.AcceptAsync(favorite.Id, candidate, _source, displayedFingerprint)).Should().BeFalse();
        (await _service.MatchAsync([Node()], [_source])).Single().State.Should().Be(FavoriteState.Matched);
    }

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task ConcurrentStars_DoNotCreateDuplicateFavorites_AndSnapshotsAreEncrypted()
    {
        var favorites = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _service.AddAsync(Node(), _source)));
        favorites.Select(f => f.Id).Distinct().Should().ContainSingle();
        favorites[0].ProtectedSnapshot.Should().StartWith("dpapi:");
        favorites[0].ProtectedSnapshot.Should().NotContain("secret-pass-marker");
        using var database = new FileStream(Path.Combine(_directory, "favorites.db"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var contents = new MemoryStream();
        await database.CopyToAsync(contents, TestContext.Current.CancellationToken);
        var databaseText = Encoding.UTF8.GetString(contents.ToArray());
        databaseText.Should().NotContain("secret-pass-marker").And.NotContain("node.example.com");
        await _service.RemoveAsync(favorites[0].Id);
        (await _service.MatchAsync([Node()], [_source])).Should().BeEmpty();
    }

    [Fact(Skip = "Requires Windows DPAPI; other platforms must not modify the user keychain.", SkipUnless = nameof(IsWindows))]
    public async Task SeparatePortsOnTheSameHost_CanBothBeFavorited()
    {
        var one = Node(); var two = Node("second"); two.Port = 8443;
        var first = await _service.AddAsync(one, _source);
        var second = await _service.AddAsync(two, _source);
        first.Id.Should().NotBe(second.Id);
        (await _service.MatchAsync([one, two], [_source])).Should().HaveCount(2)
            .And.OnlyContain(m => m.State == FavoriteState.Matched);
    }

    [Fact]
    public void Fingerprint_IgnoresMetadataAndJsonPropertyOrder_ButPreservesCredentialCase()
    {
        var one = Node(); one.ProtoExtra = "{\"Flow\":\"xtls-rprx-vision\",\"SalamanderPass\":\"abc\"}";
        var two = Node("another-id"); two.Remarks = "新名称";
        two.Address = " NODE.EXAMPLE.COM ";
        two.ProtoExtra = "{ \"SalamanderPass\":\"abc\", \"Flow\":\"xtls-rprx-vision\" }";
        FavoriteFingerprint.SameConnection(one, two).Should().BeTrue();
        two.Password = "SECRET-PASS-MARKER";
        FavoriteFingerprint.SameConnection(one, two).Should().BeFalse();
    }

    [Fact]
    public void FavoriteFilter_SearchesAliasAndOriginalName_AndKeepsUnavailableBookmarks()
    {
        var normal = new ProfileItemModel { IndexId = "normal", Remarks = "日本1" };
        var favorite = new ProfileItemModel { IndexId = "", FavoriteId = "f", FavoriteAlias = "家里主用",
            Remarks = "美国1高负载临时", FavoriteStatus = "订阅中已找不到" };
        ProfilesViewModel.FilterFavoriteRows([normal, favorite], true, "家里").Should().Equal(favorite);
        ProfilesViewModel.FilterFavoriteRows([normal, favorite], true, "美国").Should().Equal(favorite);
        ProfilesViewModel.FilterFavoriteRows([normal], true, "").Should().BeEmpty();
        favorite.OriginalNameSuffix.Should().Be("美国1高负载临时");
        favorite.FavoriteAlias = "";
        favorite.DisplayName.Should().Be("美国1高负载临时");
        favorite.OriginalNameSuffix.Should().BeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        await _service.CloseAsync();
        foreach (var name in new[] { "favorites.db", "favorites.db-wal", "favorites.db-shm", "favorites.db-journal" })
            File.Delete(Path.Combine(_directory, name));
        Directory.Delete(_directory);
    }
}
