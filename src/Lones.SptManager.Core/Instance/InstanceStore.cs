using System.Text.Json;
using System.Text.Json.Serialization;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Instance;

public sealed class InstanceDocument
{
    public int ManifestVersion { get; init; } = ProductInfo.ManifestVersion;
    public required string InstanceId { get; init; }
    public required string GameRoot { get; init; }
    public string? GameRootVolumeId { get; init; }
    public string? ManagerDataVolumeId { get; init; }
    public string? SptFileVersion { get; init; }
    public string? EftFileVersion { get; init; }
    public DateTimeOffset BoundAtUtc { get; init; }
    public IReadOnlyList<BaselineFile> Baseline { get; init; } = [];
}

public sealed class InstanceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IVolumeIdReader _volumes;

    public InstanceStore()
        : this(new VolumeIdReader())
    {
    }

    public InstanceStore(IVolumeIdReader volumes)
    {
        _volumes = volumes;
    }

    public static string DefaultManagerDataPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductInfo.DefaultManagerFolderName);

    public InstanceDocument Save(string managerData, BindResult bind, SptOwnedBaseline baseline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        if (!bind.IsSuccess)
        {
            throw new InvalidOperationException("Refusing to save a failed bind.");
        }

        var existing = TryFindByGameRoot(managerData, bind.GameRoot);
        var id = existing?.InstanceId ?? Guid.NewGuid().ToString("N");
        var document = new InstanceDocument
        {
            InstanceId = id,
            GameRoot = bind.GameRoot,
            GameRootVolumeId = bind.GameRootVolumeId,
            ManagerDataVolumeId = TryVolume(managerData),
            SptFileVersion = bind.SptFileVersion,
            EftFileVersion = bind.EftFileVersion,
            BoundAtUtc = DateTimeOffset.UtcNow,
            Baseline = baseline.Files
        };

        var instanceDir = Path.Combine(managerData, "instances", id);
        Directory.CreateDirectory(instanceDir);
        Directory.CreateDirectory(Path.Combine(managerData, "store"));
        Directory.CreateDirectory(Path.Combine(managerData, "cache", "forge"));
        Directory.CreateDirectory(Path.Combine(managerData, "profiles"));

        var path = Path.Combine(instanceDir, "instance.json");
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
        return document;
    }

    public static IReadOnlyList<InstanceDocument> List(string managerData)
    {
        var root = Path.Combine(managerData, "instances");
        if (!Directory.Exists(root))
        {
            return [];
        }

        var list = new List<InstanceDocument>();
        foreach (var file in Directory.EnumerateFiles(root, "instance.json", SearchOption.AllDirectories))
        {
            try
            {
                var document = JsonSerializer.Deserialize<InstanceDocument>(File.ReadAllText(file), JsonOptions);
                if (document is not null)
                {
                    list.Add(document);
                }
            }
            catch (JsonException)
            {
                // Skip unreadable instance files from older or corrupt writes.
            }
        }

        return list.OrderByDescending(item => item.BoundAtUtc).ToArray();
    }

    public static InstanceDocument? TryLatest(string managerData)
        => List(managerData).FirstOrDefault();

    public static InstanceDocument? TryFindByGameRoot(string managerData, string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(managerData))
        {
            return null;
        }

        var full = Path.GetFullPath(gameRoot);
        return List(managerData).FirstOrDefault(item =>
            string.Equals(Path.GetFullPath(item.GameRoot), full, StringComparison.OrdinalIgnoreCase));
    }

    private string? TryVolume(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return _volumes.GetVolumeId(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
