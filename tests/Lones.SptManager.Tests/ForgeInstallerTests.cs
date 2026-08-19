using System.Net;
using System.Net.Http.Headers;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Forge;

namespace Lones.SptManager.Tests;

public sealed class ForgeInstallerTests
{
    [Fact]
    public async Task Search_DeserializesModsAndPicks41Version()
    {
        var handler = new RouteHandler
        {
            ["GET /mods"] = """{"success":true,"data":[{"id":1343,"guid":"com.terkoiz.skipper","name":"Skipper","slug":"skipper","teaser":"skip","thumbnail":"https://files.sp-mod.com/mods/1861.png","fika_compatibility":false,"versions":[{"id":1,"version":"1.1.4","link":"https://sp-mod.com/mod/download/1343/skipper/1.1.4","content_length":10,"spt_version_constraint":"~4.0","fika_compatibility":"unknown"},{"id":2,"version":"1.1.5","link":"https://sp-mod.com/mod/download/1343/skipper/1.1.5","content_length":12,"spt_version_constraint":"~4.1","fika_compatibility":"unknown"}]}]}"""
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
        using var client = new ForgeClient(http);
        var mods = await client.ListModsAsync("skipper");
        var hits = ForgeClient.ToSearchHits(mods);
        Assert.Single(hits);
        Assert.Equal(1343, hits[0].ModId);
        Assert.Equal("1.1.5", hits[0].Version);
        Assert.Equal("https://files.sp-mod.com/mods/1861.png", hits[0].Thumbnail);
    }

    [Fact]
    public void Search_OmitsSptModManager()
    {
        var mods = new[]
        {
            new ForgeMod { Id = 1343, Name = "Skipper", Slug = "skipper" },
            new ForgeMod
            {
                Id = ForgeRestrictedMods.SptModManagerId,
                Name = ForgeRestrictedMods.SptModManagerName,
                Slug = ForgeRestrictedMods.SptModManagerSlug,
                Guid = ForgeRestrictedMods.SptModManagerGuid
            }
        };
        var hits = ForgeClient.ToSearchHits(mods);
        Assert.Single(hits);
        Assert.Equal(1343, hits[0].ModId);
    }

    [Fact]
    public async Task Install_RestrictedSptModManager_BlocksWithoutDownload()
    {
        var handler = new RouteHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
        using var client = new ForgeClient(http);
        var result = await new ForgeInstaller(client).InstallAsync(
            ForgeRestrictedMods.SptModManagerId,
            Path.Combine(Path.GetTempPath(), "lones-forge-" + Guid.NewGuid().ToString("N")));
        Assert.False(result.Success);
        Assert.Contains("incompatible", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lone's SPT Manager", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_Conflict_Blocks()
    {
        var handler = new RouteHandler
        {
            ["GET /mod/5/versions"] = """{"success":true,"data":[{"id":9,"version":"2.0.5","link":"https://sp-mod.com/mod/download/5/example/2.0.5","content_length":4,"spt_version_constraint":"~4.1","fika_compatibility":"compatible"}]}""",
            ["GET /mods/dependencies"] = """{"success":true,"data":{"5:2.0.5":[{"id":6,"guid":"com.dep","name":"Dep","slug":"dep","conflict":true,"latest_compatible_version":null,"dependencies":[]}]}}""",
            ["GET /addons"] = """{"success":true,"data":[]}"""
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
        using var client = new ForgeClient(http);
        var result = await new ForgeInstaller(client).InstallAsync(5, Path.Combine(Path.GetTempPath(), "lones-forge-" + Guid.NewGuid().ToString("N")), includeAddons: true);
        Assert.False(result.Success);
        Assert.Contains("conflict", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Install_DownloadsAndImports()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-forge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zip = ZipFixture.WriteZip(
            Path.Combine(root, "pkg.zip"),
            [("BepInEx/plugins/Skipper/Skipper.dll", "dll")]);
        var bytes = File.ReadAllBytes(zip);
        var handler = new RouteHandler
        {
            ["GET /mod/1343/versions"] =
                "{\"success\":true,\"data\":[{\"id\":2,\"version\":\"1.1.5\",\"link\":\"https://sp-mod.com/mod/download/1343/skipper/1.1.5\",\"content_length\":"
                + bytes.Length
                + ",\"spt_version_constraint\":\"~4.1\",\"fika_compatibility\":\"unknown\"}]}",
            ["GET /mods/dependencies"] = """{"success":true,"data":{"1343:1.1.5":[]}}""",
            ["GET /addons"] = """{"success":true,"data":[]}""",
            ["GET /mod/download/1343/skipper/1.1.5"] = bytes
        };
        try
        {
            using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(http);
            var result = await new ForgeInstaller(client).InstallAsync(1343, root, profileId: "default");
            Assert.True(result.Success, result.Message);
            Assert.Single(result.Documents);
            Assert.NotNull(ModStore.TryRead(root, result.Documents[0].ModKey, result.Documents[0].Version));
            Assert.Equal(1343, result.Documents[0].ForgeModId);
            Assert.Null(result.Documents[0].ForgeAddonId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Install_Addon_UsesAddonEndpoints_SkipsNullCompatibleDep()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-forge-addon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zip = ZipFixture.WriteZip(
            Path.Combine(root, "addon.zip"),
            [("BepInEx/plugins/MergeConsumablesFika/MergeConsumablesFika.dll", "dll")]);
        var bytes = File.ReadAllBytes(zip);
        var handler = new RouteHandler
        {
            ["GET /addon/4/versions"] =
                "{\"success\":true,\"data\":[{\"id\":11,\"version\":\"1.1.0\",\"link\":\"https://sp-mod.com/addon/download/4/mergeconsumables-fika-sync/1.1.0\",\"content_length\":"
                + bytes.Length
                + ",\"spt_version_constraint\":\"~4.1\",\"fika_compatibility\":\"unknown\"}]}",
            ["GET /addons/dependencies"] =
                """{"success":true,"data":{"4:1.1.0":[{"id":2326,"name":"Project Fika","slug":"project-fika","conflict":false,"latest_compatible_version":null,"dependencies":[]}]}}""",
            ["GET /addon/4"] =
                """{"success":true,"data":{"id":4,"mod_id":1657,"name":"Merge Consumables - Fika sync","thumbnail":"https://files.sp-mod.com/addons/4.png"}}""",
            ["GET /addon/download/4/mergeconsumables-fika-sync/1.1.0"] = bytes
        };
        try
        {
            using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(http);
            var result = await new ForgeInstaller(client).InstallAsync(
                4,
                root,
                displayName: "Merge Consumables - Fika sync",
                isAddon: true);
            Assert.True(result.Success, result.Message);
            Assert.Single(result.Documents);
            Assert.Equal(4, result.Documents[0].ForgeAddonId);
            Assert.Null(result.Documents[0].ForgeModId);
            Assert.Contains(result.Warnings, line => line.Contains("Project Fika", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(handler.Requests, path => path.Contains("/mod/4", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(handler.Requests, path => path.Contains("/mods/dependencies", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Updates_SurfacesBlockedReason()
    {
        var handler = new RouteHandler
        {
            ["GET /mods/updates"] = """{"success":true,"data":{"spt_version":"4.1.2","updates":[],"blocked_updates":[{"current_version":{"mod_id":5,"guid":"com.example","name":"Example","version":"1.0.0"},"reason":"dependency missing foo"}],"up_to_date":[],"incompatible_with_spt":[]}}"""
        };
        var manager = Path.Combine(Path.GetTempPath(), "lones-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(manager, "store", "Example", "1.0.0"));
        File.WriteAllText(
            Path.Combine(manager, "store", "Example", "1.0.0", "mod.json"),
            """{"modKey":"Example","version":"1.0.0","kind":"Client","deployable":true,"forgeModId":5,"files":[],"importedAtUtc":"2026-08-15T00:00:00Z"}""");
        try
        {
            using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(http);
            var text = await new ForgeInstaller(client).CheckUpdatesAsync(manager, "4.1.2");
            Assert.Contains("Blocked", text);
            Assert.Contains("foo", text);
        }
        finally
        {
            if (Directory.Exists(manager))
            {
                Directory.Delete(manager, recursive: true);
            }
        }
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, object> _routes = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Requests { get; } = [];

        public object this[string key]
        {
            set => _routes[key] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.ToString() ?? "");
            var path = request.RequestUri is null
                ? ""
                : request.RequestUri.IsAbsoluteUri
                    ? request.RequestUri.AbsolutePath.Trim('/')
                    : request.RequestUri.ToString().Trim('/');
            if (path.StartsWith("api/v0/", StringComparison.OrdinalIgnoreCase))
            {
                path = path["api/v0/".Length..];
            }

            var key = request.Method.Method.ToUpperInvariant() + " /" + path;
            object? matched = null;
            var matchedLength = -1;
            foreach (var pair in _routes)
            {
                if (key.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(pair.Key.Split(' ', 2).Last().TrimStart('/'), StringComparison.OrdinalIgnoreCase))
                {
                    if (pair.Key.Length > matchedLength)
                    {
                        matched = pair.Value;
                        matchedLength = pair.Key.Length;
                    }
                }
            }

            if (matched is byte[] bytes)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                    }
                });
            }

            if (matched is string json)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"success\":false}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
