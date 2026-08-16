using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Forge;

namespace Lones.SptManager.Tests;

public sealed class ModPackInstallerTests
{
    [Fact]
    public void Parse_ReadsCampdegenShape()
    {
        var manifest = ModPackManifest.Parse(
            """
            {
              "mods": [
                {
                  "id": 2523,
                  "name": "Bosses Have Gp Coins",
                  "slug": "bosses-have-gp-coins",
                  "side": "server",
                  "installedVersion": "1.1.0",
                  "description": "drops",
                  "settingsNotes": ""
                },
                { "id": 0, "name": "skip", "installedVersion": "1.0.0" },
                { "id": 2523, "name": "dup", "installedVersion": "9.9.9" },
                { "id": 1090, "name": "Color Converter API", "version": "1.1.1" }
              ]
            }
            """);

        var listed = manifest.ListedMods();
        Assert.Equal(2, listed.Count);
        Assert.Equal(2523, listed[0].Id);
        Assert.Equal("1.1.0", listed[0].RequestedVersion);
        Assert.Equal("Bosses Have Gp Coins", listed[0].DisplayName);
        Assert.Equal(1090, listed[1].Id);
        Assert.Equal("1.1.1", listed[1].RequestedVersion);
    }

    [Fact]
    public void Normalize_HttpsAndLocalAndRejectsHttp()
    {
        var url = ModPackSource.Normalize("https://campdegen.com/spt-pack/data/mods.json");
        Assert.Equal("https://campdegen.com/spt-pack/data/mods.json", url);
        Assert.Equal(
            "https://campdegen.com/spt-pack/data/mods.json",
            ModPackSource.Normalize("campdegen.com/spt-pack/data/mods.json"));
        Assert.Throws<InvalidOperationException>(() => ModPackSource.Normalize("http://campdegen.com/mods.json"));

        var file = Path.Combine(Path.GetTempPath(), "lones-pack-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, """{"mods":[]}""");
        try
        {
            Assert.Equal(Path.GetFullPath(file), ModPackSource.Normalize(file));
            Assert.Equal(Path.GetFullPath(file), ModPackSource.Normalize("\"" + file + "\""));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void VersionEquals_IgnoresVPrefix()
    {
        Assert.True(ModPackInstaller.VersionEquals("v1.1.0", "1.1.0"));
        Assert.False(ModPackInstaller.VersionEquals("1.1.0", "1.1.1"));
    }

    [Fact]
    public async Task Install_ReusesStore_DownloadsMissing_ContinuesOnFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        PutStoreMod(root, "Already", "1.0.0", forgeModId: 10);
        var zip = ZipFixture.WriteZip(
            Path.Combine(root, "fresh.zip"),
            [("BepInEx/plugins/Fresh/Fresh.dll", "dll")]);
        var bytes = File.ReadAllBytes(zip);
        var pack = Path.Combine(root, "mods.json");
        File.WriteAllText(
            pack,
            """
            {
              "mods": [
                { "id": 10, "name": "Already", "installedVersion": "1.0.0" },
                { "id": 20, "name": "Fresh", "installedVersion": "2.0.0" },
                { "id": 30, "name": "Missing", "installedVersion": "9.9.9" }
              ]
            }
            """);

        var handler = new RouteHandler
        {
            ["GET /mod/20/versions"] =
                "{\"success\":true,\"data\":[{\"id\":2,\"version\":\"2.0.0\",\"link\":\"https://sp-mod.com/mod/download/20/fresh/2.0.0\",\"content_length\":"
                + bytes.Length
                + ",\"spt_version_constraint\":\"~4.1\",\"fika_compatibility\":\"unknown\"}]}",
            ["GET /mod/30/versions"] =
                """{"success":true,"data":[{"id":3,"version":"1.0.0","link":"https://sp-mod.com/mod/download/30/missing/1.0.0","content_length":4,"spt_version_constraint":"~4.1","fika_compatibility":"unknown"}]}""",
            ["GET /mods/dependencies"] = """{"success":true,"data":{"20:2.0.0":[]}}""",
            ["GET /mod/download/20/fresh/2.0.0"] = bytes
        };

        try
        {
            using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(http);
            using var installer = new ModPackInstaller(client);
            new ProfileStore().LoadOrCreate(root, "camp");
            var result = await installer.InstallAsync(pack, root, "camp");
            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Installed);
            Assert.Equal(1, result.Reused);
            Assert.Equal(1, result.Failed);
            Assert.Contains("Missing", string.Join(" ", result.Warnings), StringComparison.OrdinalIgnoreCase);

            var enabled = new ProfileStore().TryRead(root, "camp")!.Enabled
                .OrderBy(item => item.Priority)
                .ToArray();
            Assert.Equal(2, enabled.Length);
            Assert.Equal("Already", enabled[0].ModKey);
            Assert.Equal("1.0.0", enabled[0].Version);
            Assert.Equal(0, enabled[0].Priority);
            Assert.Equal("Fresh", enabled[1].ModKey);
            Assert.Equal("2.0.0", enabled[1].Version);
            Assert.NotNull(ModStore.TryRead(root, "Fresh", "2.0.0"));
            Assert.Equal("Fresh", ModStore.TryRead(root, "Fresh", "2.0.0")!.DisplayName);
            Assert.DoesNotContain(InstallInventory.Scan(null, root, "camp").Items, item => item.Key == "Missing");
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
    public async Task Install_UsesPackNameAndForgeThumbnail()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-pack-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zip = ZipFixture.WriteZip(
            Path.Combine(root, "wtt.zip"),
            [("BepInEx/plugins/WTTContentBackport/WTT.dll", "dll")]);
        var bytes = File.ReadAllBytes(zip);
        var pack = Path.Combine(root, "mods.json");
        File.WriteAllText(
            pack,
            """{"mods":[{"id":2512,"name":"WTT - Content Backport","installedVersion":"2.0.0"}]}""");
        var handler = new RouteHandler
        {
            ["GET /mod/2512/versions"] =
                "{\"success\":true,\"data\":[{\"id\":1,\"version\":\"2.0.0\",\"link\":\"https://sp-mod.com/mod/download/2512/wtt/2.0.0\",\"content_length\":"
                + bytes.Length
                + ",\"spt_version_constraint\":\"~4.1\",\"fika_compatibility\":\"unknown\"}]}",
            ["GET /mods/dependencies"] = """{"success":true,"data":{"2512:2.0.0":[]}}""",
            ["GET /mod/2512"] =
                """{"success":true,"data":{"id":2512,"name":"WTT - Content Backport","thumbnail":"https://files.sp-mod.com/mods/2512.png"}}""",
            ["GET /mod/download/2512/wtt/2.0.0"] = bytes
        };
        try
        {
            using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(http);
            using var installer = new ModPackInstaller(client);
            new ProfileStore().LoadOrCreate(root, "pack");
            var result = await installer.InstallAsync(pack, root, "pack");
            Assert.True(result.Success, result.Message);
            var document = ModStore.TryRead(root, "WTT - Content Backport", "2.0.0");
            Assert.NotNull(document);
            Assert.Equal("WTT - Content Backport", document!.DisplayName);
            Assert.Equal(2512, document.ForgeModId);
            Assert.Equal("https://files.sp-mod.com/mods/2512.png", document.ThumbnailUrl);
            var row = Assert.Single(InstallInventory.Scan(null, root, "pack").Items, item => item.Kind == InstallInventory.StoreKind);
            Assert.Equal("WTT - Content Backport", row.DisplayName);
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
    public async Task Install_FetchesHttpsJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-pack-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        PutStoreMod(root, "Already", "1.0.0", forgeModId: 10);
        var packJson = """{"mods":[{"id":10,"name":"Already","installedVersion":"1.0.0"}]}""";
        var handler = new RouteHandler
        {
            ["GET https://campdegen.com/spt-pack/data/mods.json"] = packJson
        };
        try
        {
            using var packHttp = new HttpClient(handler);
            using var forgeHttp = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(forgeHttp);
            using var installer = new ModPackInstaller(client, packHttp);
            new ProfileStore().LoadOrCreate(root, "from-url");
            var result = await installer.InstallAsync(
                "https://campdegen.com/spt-pack/data/mods.json",
                root,
                "from-url");
            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Reused);
            Assert.Equal(0, result.Installed);
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
    public async Task Install_OmitsSptModManager()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-pack-omit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        PutStoreMod(root, "Already", "1.0.0", forgeModId: 10);
        var pack = Path.Combine(root, "mods.json");
        File.WriteAllText(
            pack,
            """
            {
              "mods": [
                { "id": 10, "name": "Already", "installedVersion": "1.0.0" },
                { "id": 2851, "name": "SPT Mod Manager", "slug": "spt-mod-manager", "installedVersion": "0.4.4" }
              ]
            }
            """);
        try
        {
            using var http = new HttpClient(new RouteHandler()) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(http);
            using var installer = new ModPackInstaller(client);
            new ProfileStore().LoadOrCreate(root, "omit");
            var result = await installer.InstallAsync(pack, root, "omit");
            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.Reused);
            Assert.Equal(1, result.Omitted);
            Assert.Equal(0, result.Failed);
            Assert.Contains("incompatible", string.Join(" ", result.Warnings), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(InstallInventory.Scan(null, root, "omit").Items, item =>
                item.Key.Contains("SPT Mod Manager", StringComparison.OrdinalIgnoreCase)
                || item.ForgeModId == 2851);
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
    public async Task Install_EmptyPackFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-pack-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pack = Path.Combine(root, "mods.json");
        File.WriteAllText(pack, """{"mods":[]}""");
        try
        {
            using var http = new HttpClient(new RouteHandler()) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
            using var client = new ForgeClient(http);
            using var installer = new ModPackInstaller(client);
            var result = await installer.InstallAsync(pack, root, "empty");
            Assert.False(result.Success);
            Assert.Contains("no mods", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void PutStoreMod(string managerData, string key, string version, int forgeModId)
    {
        var dir = ModStore.PackageDirectory(managerData, key, version);
        Directory.CreateDirectory(Path.Combine(dir, "files", "BepInEx", "plugins", key));
        File.WriteAllText(Path.Combine(dir, "files", "BepInEx", "plugins", key, key + ".dll"), "dll");
        var document = new ModDocument
        {
            ModKey = key,
            Version = version,
            Kind = "Client",
            Deployable = true,
            ForgeModId = forgeModId,
            Files =
            [
                new ModFileRecord
                {
                    CanonicalPath = "BepInEx/plugins/" + key + "/" + key + ".dll",
                    Sha256 = "00"
                }
            ],
            ImportedAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            Path.Combine(dir, "mod.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, object> _routes = new(StringComparer.OrdinalIgnoreCase);

        public object this[string key]
        {
            set => _routes[key] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            var absolute = uri?.IsAbsoluteUri == true ? uri.ToString() : "";
            var path = uri is null
                ? ""
                : uri.IsAbsoluteUri
                    ? uri.AbsolutePath.Trim('/')
                    : uri.ToString().Trim('/');
            if (path.StartsWith("api/v0/", StringComparison.OrdinalIgnoreCase))
            {
                path = path["api/v0/".Length..];
            }

            var key = request.Method.Method.ToUpperInvariant() + " /" + path;
            var absoluteKey = request.Method.Method.ToUpperInvariant() + " " + absolute;
            foreach (var pair in _routes)
            {
                if (absoluteKey.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(pair.Key.Split(' ', 2).Last().TrimStart('/'), StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToResponse(pair.Value));
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"success\":false}", System.Text.Encoding.UTF8, "application/json")
            });
        }

        private static HttpResponseMessage ToResponse(object value)
        {
            if (value is byte[] bytes)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                    }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent((string)value, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
