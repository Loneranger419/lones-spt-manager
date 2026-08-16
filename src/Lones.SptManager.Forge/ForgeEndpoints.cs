namespace Lones.SptManager.Forge;

public static class ForgeEndpoints
{
    public const string SiteOrigin = "https://sp-mod.com";
    public const string ApiBase = "https://sp-mod.com/api/v0/";

    public static bool IsForbiddenHost(Uri uri)
    {
        var host = uri.Host;
        return host.Equals("forge.sp-tarkov.com", StringComparison.OrdinalIgnoreCase)
               || host.Equals("forge.sp-mod.com", StringComparison.OrdinalIgnoreCase);
    }
}
