namespace ServiceLib;

/// <summary>
/// Central registry for FireflyVPN product identity, public endpoints, and
/// user-facing external links. Keep branded links here rather than scattering
/// them across views and services.
/// </summary>
public static class FireflyBranding
{
#if PALETTE_BUILD
    public const string ProductName = "FireflyVPN-Palette";
#else
    public const string ProductName = "流萤加速器";
#endif
    public static string ClientApiUrl { get; } = GetClientApiUrl();
    public const string WebsiteUrl = "https://vpn.202132.xyz";
    public const string CommunityUrl = "https://t.me/+N3h80bmqvVMwYzll";
#if PALETTE_BUILD
    public const string SourceCodeUrl = "https://github.com/chenjiakun1995-max/FireflyVPN-Palette";
#else
    public const string SourceCodeUrl = "https://github.com/Iskongkongyo/FireflyVPN-Desktop";
#endif
    public const string UpstreamProjectName = "v2rayN";
    public const string UpstreamProjectUrl = "https://github.com/2dust/v2rayN";
    public const string LicenseName = "GNU GPL v3.0";
    public const string LicenseUrl = SourceCodeUrl + "/blob/main/LICENSE";

    private static string GetClientApiUrl()
    {
        var endpoint = typeof(FireflyBranding).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "FireflyClientApiUrl")
            ?.Value
            ?.Trim();

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.AbsoluteUri.TrimEnd('/')
            : string.Empty;
    }

    public static ECoreType GetDefaultCoreType(EConfigType configType)
    {
        return Global.SingboxOnlyConfigType.Contains(configType)
            ? ECoreType.sing_box
            : ECoreType.Xray;
    }
}
