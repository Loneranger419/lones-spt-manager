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

    public string Title => Item.Key;

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

    public string PriorityLabel => CanToggle && Item.Priority != int.MaxValue ? Item.Priority.ToString() : "—";

    public string Initial => string.IsNullOrWhiteSpace(Title) ? "?" : char.ToUpperInvariant(Title.Trim()[0]).ToString();

    public object? ThumbnailSource
    {
        get
        {
            var local = ThumbnailCache.TryLocalPath(ManagerData, Item.ThumbnailUrl);
            return local is null ? Item.ThumbnailUrl : LoadUnlocked(local);
        }
    }

    private static ImageSource? LoadUnlocked(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            using (var stream = File.OpenRead(path))
            {
                image.StreamSource = stream;
                image.EndInit();
            }

            image.Freeze();
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
            }
        }
        finally
        {
            _suppress = false;
        }
    }
}
