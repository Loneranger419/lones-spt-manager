using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.App;

public sealed class ModRowViewModel : INotifyPropertyChanged
{
    private bool _isChecked;
    private bool _suppress;
    private readonly Action<ModRowViewModel, bool>? _onToggle;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModRowViewModel(InventoryItem item, string managerData, Action<ModRowViewModel, bool>? onToggle)
    {
        Item = item;
        ManagerData = managerData;
        _isChecked = item.Enabled;
        _onToggle = onToggle;
    }

    public InventoryItem Item { get; }

    public string ManagerData { get; }

    public bool CanToggle => Item.Kind == InstallInventory.StoreKind;

    public bool IsLeftover => Item.Kind == InstallInventory.LeftoverKind;

    public bool IsDimmed => CanToggle && !_isChecked;

    public string Title => string.IsNullOrWhiteSpace(Item.DisplayName) ? Item.Key : Item.DisplayName;

    public string Subtitle
    {
        get
        {
            if (IsLeftover)
            {
                return Item.InstallRelative ?? "On disk, not in the store";
            }

            var kind = string.IsNullOrWhiteSpace(Item.PackageKind) ? "mod" : Item.PackageKind;
            var text = string.IsNullOrWhiteSpace(Item.Version) ? kind : Item.Version + "  ·  " + kind;
            if (Item.RuntimeFileCount > 0 && !HarvestRules.IsRuntimeVersion(Item.Version))
            {
                text += "  ·  +" + Item.RuntimeFileCount + " generated";
            }

            return text;
        }
    }

    public bool MatchesFilter(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var needle = query.Trim();
        return Contains(Title)
               || Contains(Item.Key)
               || Contains(Item.DisplayName)
               || Contains(Item.Version)
               || Contains(Item.PackageKind)
               || Contains(Item.InstallRelative)
               || Contains(Subtitle);

        bool Contains(string? value)
            => !string.IsNullOrWhiteSpace(value)
               && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public string PriorityLabel => CanToggle && Item.Priority != int.MaxValue ? Item.Priority.ToString() : "—";

    public string Initial => string.IsNullOrWhiteSpace(Title) ? "?" : char.ToUpperInvariant(Title.Trim()[0]).ToString();

    public object? ThumbnailSource
    {
        get
        {
            var local = ThumbnailCache.TryLocalPath(ManagerData, Item.ThumbnailUrl);
            if (local is not null && LoadUnlocked(local) is { } image)
            {
                return image;
            }

            return Item.ThumbnailUrl;
        }
    }

    private static readonly ConcurrentDictionary<string, ImageSource> ThumbnailImages = new(StringComparer.OrdinalIgnoreCase);

    private static ImageSource? LoadUnlocked(string path)
    {
        if (ThumbnailImages.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 24)
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 72;
            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
            {
                image.StreamSource = stream;
                image.EndInit();
                if (image.CanFreeze)
                {
                    image.Freeze();
                }
            }

            if (image.PixelWidth <= 0)
            {
                return null;
            }

            ThumbnailImages[path] = image;
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value || _suppress || !CanToggle)
            {
                return;
            }

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDimmed)));
            _onToggle?.Invoke(this, value);
        }
    }

    public void SyncChecked(bool enabled)
    {
        _suppress = true;
        try
        {
            if (_isChecked != enabled)
            {
                _isChecked = enabled;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDimmed)));
            }
        }
        finally
        {
            _suppress = false;
        }
    }
}
