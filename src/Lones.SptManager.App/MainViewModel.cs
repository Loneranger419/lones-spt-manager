using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using Lones.SptManager.Core;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Launch;
using Lones.SptManager.Core.Mapping;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Core.Update;
using Lones.SptManager.Forge;

namespace Lones.SptManager.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _gameRoot = string.Empty;
    private string _managerData = InstanceStore.DefaultManagerDataPath;
    private string _profileId = ProfilePaths.DefaultProfileId;
    private string _forgeQuery = string.Empty;
    private string _modFilter = string.Empty;
    private string _launchMode = LaunchModes.Solo;
    private string _joinUrl = string.Empty;
    private ForgeSearchHit? _selectedForgeHit;
    private ModRowViewModel? _selectedModRow;
    private string? _selectedOverwritePath;
    private string _status = "Pick an SPT 4.1.x game root (folder with EscapeFromTarkov.exe and SPT_Runtime).";
    private string _busyMessage = "Working…";
    private bool _busy;
    private bool _refreshingProfiles;
    private bool _hydratingThumbnails;
    private bool _packUpdateAvailable;
    private string _packUpdateSummary = string.Empty;
    private CancellationTokenSource? _packCheckCts;
    private bool _appUpdateAvailable;
    private string _appUpdateSummary = string.Empty;
    private string _appUpdateStatus = "On launch the app checks GitHub Releases. Click Check to run that again. App update downloads the new exe and restarts.";
    private AppUpdateInfo? _appUpdate;
    private CancellationTokenSource? _appCheckCts;
    private bool _checkingAppUpdate;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        BindCommand = new RelayCommand(Bind, () => !_busy && !string.IsNullOrWhiteSpace(GameRoot));
        ImportZipCommand = new RelayCommand(ImportZip, () => !_busy && !string.IsNullOrWhiteSpace(ManagerData));
        DeployCommand = new RelayCommand(Deploy, () => !_busy && !string.IsNullOrWhiteSpace(GameRoot) && !string.IsNullOrWhiteSpace(ManagerData));
        RepairCommand = new RelayCommand(Repair, () => !_busy && !string.IsNullOrWhiteSpace(ManagerData));
        HarvestCommand = new RelayCommand(Harvest, () => !_busy && !string.IsNullOrWhiteSpace(GameRoot) && !string.IsNullOrWhiteSpace(ManagerData));
        AddProfileCommand = new RelayCommand(AddProfile, () => !_busy && !string.IsNullOrWhiteSpace(ManagerData));
        EditProfileCommand = new RelayCommand(EditProfile, () => !_busy && !string.IsNullOrWhiteSpace(ManagerData) && !string.IsNullOrWhiteSpace(ProfileId));
        DiscardOverwriteCommand = new RelayCommand(DiscardOverwrite, () => !_busy && !string.IsNullOrWhiteSpace(ManagerData));
        SearchForgeCommand = new RelayCommand(SearchForge, () => !_busy && !string.IsNullOrWhiteSpace(ForgeQuery));
        InstallForgeCommand = new RelayCommand(InstallForge, () => !_busy && SelectedForgeHit is not null && !string.IsNullOrWhiteSpace(ManagerData));
        CheckUpdatesCommand = new RelayCommand(CheckUpdates, () => !_busy && !string.IsNullOrWhiteSpace(ManagerData));
        var canLaunch = () => !_busy && !string.IsNullOrWhiteSpace(GameRoot);
        LaunchSoloCommand = new RelayCommand(() => Launch(LaunchModes.Solo), canLaunch);
        LaunchFikaHostCommand = new RelayCommand(() => Launch(LaunchModes.FikaHost), canLaunch);
        LaunchFikaJoinCommand = new RelayCommand(() => Launch(LaunchModes.FikaClient), canLaunch);
        EnableModCommand = new RelayCommand(() => ToggleSelected(true), () => !_busy && SelectedInventoryItem is { Kind: InstallInventory.StoreKind });
        DisableModCommand = new RelayCommand(() => ToggleSelected(false), () => !_busy && SelectedInventoryItem is { Kind: InstallInventory.StoreKind, Enabled: true });
        PriorityUpCommand = new RelayCommand(() => MoveSelected(-1), () => !_busy && SelectedInventoryItem is { Kind: InstallInventory.StoreKind });
        PriorityDownCommand = new RelayCommand(() => MoveSelected(1), () => !_busy && SelectedInventoryItem is { Kind: InstallInventory.StoreKind });
        ImportLeftoverCommand = new RelayCommand(ImportLeftover, () => !_busy && SelectedInventoryItem is { Kind: InstallInventory.LeftoverKind, InstallRelative: not null } && !string.IsNullOrWhiteSpace(GameRoot));
        CopyRuntimeCommand = new RelayCommand(CopyRuntimeToProfile, () =>
            !_busy
            && SelectedInventoryItem is { Kind: InstallInventory.StoreKind, RuntimeFileCount: > 0 }
            && OtherProfileIds().Count > 0);
        DiscardSelectedOverwriteCommand = new RelayCommand(DiscardSelectedOverwrite, () => !_busy && SelectedOverwritePath is not null);
        AssignOverwriteCommand = new RelayCommand(AssignOverwrite, () => !_busy && SelectedOverwritePath is not null && SelectedInventoryItem is { Kind: InstallInventory.StoreKind, Version: not null });
        BrowseGameRootCommand = new RelayCommand(BrowseGameRoot);
        BrowseManagerDataCommand = new RelayCommand(BrowseManagerData);
        PurgeCommand = new RelayCommand(Purge, () => !_busy && !string.IsNullOrWhiteSpace(ManagerData));
        SettingsCommand = new RelayCommand(OpenSettings, () => !_busy);
        OpenPackUpdateCommand = new RelayCommand(EditProfile, () => !_busy && PackUpdateAvailable);
        OpenAppUpdateCommand = new RelayCommand(InstallAppUpdate, () => AppUpdateAvailable && !_busy);
        CheckAppUpdateCommand = new RelayCommand(CheckAppUpdateNow, () => !IsCheckingAppUpdate);
        CollectionViewSource.GetDefaultView(InventoryItems).Filter = MatchesModFilter;
        RestoreLastInstance();
        RestoreLastProfile();
        RefreshProfiles();
        LoadProfileLaunchSettings();
        RefreshInventory();
        QueuePackUpdateCheck();
        QueueAppUpdateCheck();
    }

    public string GameRoot
    {
        get => _gameRoot;
        set
        {
            if (Set(ref _gameRoot, value))
            {
                CommandManager.InvalidateRequerySuggested();
                RefreshInventory();
            }
        }
    }

    public string ManagerData
    {
        get => _managerData;
        set
        {
            if (Set(ref _managerData, value))
            {
                RefreshProfiles();
                RefreshInventory();
            }
        }
    }

    public string ProfileId
    {
        get => _profileId;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !Set(ref _profileId, value))
            {
                return;
            }

            if (_refreshingProfiles)
            {
                return;
            }

            ProfileStore.RememberLastProfile(ManagerData, _profileId);
            LoadProfileLaunchSettings();
            RefreshInventory();
            QueuePackUpdateCheck();
            _ = ApplySelectedProfileAsync();
        }
    }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                if (!value)
                {
                    BusyMessage = "Working…";
                }

                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => Set(ref _busyMessage, value);
    }

    public string ForgeQuery
    {
        get => _forgeQuery;
        set
        {
            if (Set(ref _forgeQuery, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ModFilter
    {
        get => _modFilter;
        set
        {
            if (Set(ref _modFilter, value))
            {
                CollectionViewSource.GetDefaultView(InventoryItems).Refresh();
            }
        }
    }

    public string LaunchMode
    {
        get => _launchMode;
        set => Set(ref _launchMode, value);
    }

    public string JoinUrl
    {
        get => _joinUrl;
        set => Set(ref _joinUrl, value);
    }

    public bool PackUpdateAvailable
    {
        get => _packUpdateAvailable;
        private set
        {
            if (Set(ref _packUpdateAvailable, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string PackUpdateSummary
    {
        get => _packUpdateSummary;
        private set => Set(ref _packUpdateSummary, value);
    }

    public string AppVersionLabel => ProductInfo.Name + " " + ProductInfo.Version;

    public bool AppUpdateAvailable
    {
        get => _appUpdateAvailable;
        private set
        {
            if (Set(ref _appUpdateAvailable, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string AppUpdateSummary
    {
        get => _appUpdateSummary;
        private set => Set(ref _appUpdateSummary, value);
    }

    public string AppUpdateStatus
    {
        get => _appUpdateStatus;
        private set => Set(ref _appUpdateStatus, value);
    }

    public bool IsCheckingAppUpdate
    {
        get => _checkingAppUpdate;
        private set
        {
            if (Set(ref _checkingAppUpdate, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ObservableCollection<string> ProfileIds { get; } = [];

    public ObservableCollection<ModRowViewModel> InventoryItems { get; } = [];

    public ObservableCollection<OverwriteRowViewModel> OverwriteItems { get; } = [];

    public string? SelectedOverwritePath
    {
        get => _selectedOverwritePath;
        set
        {
            if (Set(ref _selectedOverwritePath, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ModRowViewModel? SelectedModRow
    {
        get => _selectedModRow;
        set
        {
            if (Set(ref _selectedModRow, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public InventoryItem? SelectedInventoryItem => SelectedModRow?.Item;

    public ObservableCollection<ForgeSearchHit> ForgeHits { get; } = [];

    public ForgeSearchHit? SelectedForgeHit
    {
        get => _selectedForgeHit;
        set
        {
            if (Set(ref _selectedForgeHit, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public ICommand BindCommand { get; }
    public ICommand ImportZipCommand { get; }
    public ICommand DeployCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand HarvestCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand DiscardOverwriteCommand { get; }
    public ICommand SearchForgeCommand { get; }
    public ICommand InstallForgeCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand LaunchSoloCommand { get; }
    public ICommand LaunchFikaHostCommand { get; }
    public ICommand LaunchFikaJoinCommand { get; }
    public ICommand EnableModCommand { get; }
    public ICommand DisableModCommand { get; }
    public ICommand PriorityUpCommand { get; }
    public ICommand PriorityDownCommand { get; }
    public ICommand ImportLeftoverCommand { get; }
    public ICommand CopyRuntimeCommand { get; }
    public ICommand DiscardSelectedOverwriteCommand { get; }
    public ICommand AssignOverwriteCommand { get; }
    public ICommand BrowseGameRootCommand { get; }
    public ICommand BrowseManagerDataCommand { get; }
    public ICommand PurgeCommand { get; }

    public ICommand SettingsCommand { get; }
    public ICommand OpenPackUpdateCommand { get; }
    public ICommand OpenAppUpdateCommand { get; }
    public ICommand CheckAppUpdateCommand { get; }

    public void RepairOnStart()
    {
        if (string.IsNullOrWhiteSpace(ManagerData) || !Directory.Exists(ManagerData))
        {
            return;
        }

        var result = new DeployEngine().ReconcileAll(ManagerData);
        if (result.Status is DeployStatus.Recovered or DeployStatus.Failed)
        {
            Status = result.Message ?? result.Status.ToString();
        }

        RefreshProfiles();
        RefreshInventory();
    }

    private void BrowseGameRoot()
    {
        var picked = FolderPicker.Pick("Select SPT 4.1 game root", GameRoot);
        if (picked is not null)
        {
            GameRoot = picked;
        }
    }

    private void BrowseManagerData()
    {
        var picked = FolderPicker.Pick("Select manager data folder", ManagerData);
        if (picked is not null)
        {
            ManagerData = picked;
        }
    }

    private void Purge()
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        var confirm = System.Windows.MessageBox.Show(
            owner,
            "This deletes ALL manager data: the mod store, profiles, saves, BepInEx configs, Overwrite, and cache.\n\n"
            + "The SPT game folder stays. Manager junctions are removed from it.\n\n"
            + "This cannot be undone.",
            "Purge manager data",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        _hydratingThumbnails = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            ReleaseManagerFileLocks();
            var result = ManagerPurge.Run(ManagerData, string.IsNullOrWhiteSpace(GameRoot) ? null : GameRoot);
            Status = result.Message ?? (result.Success ? "Purged." : "Purge failed.");
            if (result.Success)
            {
                ResetUiAfterPurge();
            }
            else
            {
                RefreshProfiles();
                RefreshInventory();
            }

            System.Windows.MessageBox.Show(
                owner,
                Status,
                result.Success ? "Purged" : "Purge failed",
                System.Windows.MessageBoxButton.OK,
                result.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            Status = "Purge failed: " + ex.Message;
            RefreshProfiles();
            RefreshInventory();
            System.Windows.MessageBox.Show(
                owner,
                Status,
                "Purge failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            _hydratingThumbnails = false;
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ReleaseManagerFileLocks()
    {
        InventoryItems.Clear();
        SelectedModRow = null;
        ForgeHits.Clear();
        SelectedForgeHit = null;
        OverwriteItems.Clear();
        System.Windows.Application.Current?.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void ResetUiAfterPurge()
    {
        _refreshingProfiles = true;
        try
        {
            ForgeHits.Clear();
            SelectedForgeHit = null;
            OverwriteItems.Clear();
            InventoryItems.Clear();
            ProfileIds.Clear();
            ProfileIds.Add(ProfilePaths.DefaultProfileId);
            _profileId = ProfilePaths.DefaultProfileId;
            LaunchMode = LaunchModes.Solo;
            JoinUrl = string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileId)));
        }
        finally
        {
            _refreshingProfiles = false;
        }

        RefreshInventory();
    }

    private void ImportZip()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Mod archives (*.zip;*.7z)|*.zip;*.7z|All files (*.*)|*.*",
            Title = "Import mod archive into the store"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var result = new InstallMapper().ImportArchive(dialog.FileName, ManagerData);
            Status = result.Message ?? "Import finished.";
            if (result.Map.Warnings.Count > 0)
            {
                Status += Environment.NewLine + string.Join(Environment.NewLine, result.Map.Warnings);
            }

            if (result.Document is not null)
            {
                Status += Environment.NewLine + "Store: " + ModStorePath(result.Document.ModKey, result.Document.Version);
                AddImportedToProfile(result.Document);
            }

            RefreshInventory();
        }
        catch (Exception ex)
        {
            Status = "Import failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private string ModStorePath(string modKey, string version)
        => ModStore.PackageDirectory(ManagerData, modKey, version);

    private async Task ApplySelectedProfileAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(GameRoot) || string.IsNullOrWhiteSpace(ManagerData))
        {
            return;
        }

        await DeployAsync("Switching profile…").ConfigureAwait(true);
        Status = "Switched to profile " + ProfileId + "." + Environment.NewLine + Status;
    }

    private async void Deploy()
        => await DeployAsync("Deploying…").ConfigureAwait(true);

    private async Task DeployAsync(string message)
    {
        if (IsBusy)
        {
            return;
        }

        BusyMessage = message;
        IsBusy = true;
        await Task.Yield();
        try
        {
            var gameRoot = GameRoot;
            var managerData = ManagerData;
            var profileId = ProfileId;
            var result = await Task.Run(() =>
            {
                var baseline = Directory.Exists(gameRoot) ? new SptOwnedBaselineBuilder().Build(gameRoot) : null;
                return new DeployEngine().Deploy(new DeployRequest
                {
                    GameRoot = gameRoot,
                    ManagerData = managerData,
                    ProfileId = profileId,
                    Baseline = baseline
                });
            }).ConfigureAwait(true);
            Status = result.Message ?? result.Status.ToString();
            if (result.Conflicts.Count > 0)
            {
                Status += Environment.NewLine + "Overlay conflicts (higher priority won):";
                foreach (var conflict in result.Conflicts)
                {
                    Status += Environment.NewLine + $"  {conflict.CanonicalPath}: {conflict.WinnerModKey} over {conflict.LoserModKey}";
                }
            }

            RefreshInventory();
        }
        catch (Exception ex)
        {
            Status = "Deploy failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Repair()
    {
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var result = new DeployEngine().ReconcileAll(ManagerData);
            Status = result.Message ?? result.Status.ToString();
        }
        catch (Exception ex)
        {
            Status = "Repair failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async void Harvest()
        => await HarvestAsync().ConfigureAwait(true);

    private async Task HarvestAsync(bool alreadyBusy = false)
    {
        if (IsBusy && !alreadyBusy)
        {
            return;
        }

        BusyMessage = "Harvesting…";
        if (!alreadyBusy)
        {
            IsBusy = true;
            await Task.Yield();
        }

        try
        {
            var gameRoot = GameRoot;
            var managerData = ManagerData;
            var profileId = ProfileId;
            var result = await Task.Run(() =>
            {
                var baseline = Directory.Exists(gameRoot) ? new SptOwnedBaselineBuilder().Build(gameRoot) : null;
                return new HarvestEngine().Harvest(gameRoot, managerData, profileId, baseline);
            }).ConfigureAwait(true);
            Status = result.Message ?? result.Status.ToString();
            var listed = HarvestEngine.ListOverwrite(ManagerData, ProfileId);
            RefreshOverwrite();
            if (listed.Count > 0)
            {
                Status += Environment.NewLine + "Overwrite:";
                foreach (var path in listed.Take(20))
                {
                    Status += Environment.NewLine + "  " + path;
                }
            }
        }
        catch (Exception ex)
        {
            Status = "Harvest failed: " + ex.Message;
        }
        finally
        {
            if (!alreadyBusy)
            {
                IsBusy = false;
            }
        }
    }

    private void AddProfile()
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is null)
        {
            return;
        }

        var result = ProfileDialog.ShowAdd(owner, ProfilePaths.ListProfileIds(ManagerData));
        if (result.Action != ProfileDialogAction.Accept || string.IsNullOrWhiteSpace(result.Name))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.PackSource))
        {
            _ = InstallPackProfileAsync(result.Name, result.PackSource);
            return;
        }

        if (result.CopyFromId is not null)
        {
            var copied = ProfileCopier.Copy(ManagerData, result.CopyFromId, result.Name, result.Options);
            Status = copied.Message ?? (copied.Success ? "Copied." : "Copy failed.");
            if (!copied.Success || copied.DestinationId is null)
            {
                return;
            }

            RefreshProfiles();
            ProfileId = copied.DestinationId;
            return;
        }

        new ProfileStore().LoadOrCreate(ManagerData, result.Name);
        RefreshProfiles();
        ProfileId = ProfilePaths.Sanitize(result.Name);
        Status = "Created profile " + ProfileId + ".";
    }

    private async Task InstallPackProfileAsync(string name, string packSource)
    {
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        var created = new ProfileStore().LoadOrCreate(ManagerData, name);
        var id = created.ProfileId;
        var owner = System.Windows.Application.Current?.MainWindow;
        var dialog = owner is null ? null : new ProgressDialog("Installing pack — " + id) { Owner = owner };
        using var cts = new CancellationTokenSource();
        if (dialog is not null)
        {
            dialog.CancelRequested += () => cts.Cancel();
            dialog.Show();
        }

        try
        {
            var stored = TryNormalizePackSource(packSource) ?? packSource.Trim();
            new ProfileStore().Save(ManagerData, id, created.Enabled, created.LaunchMode, created.JoinUrl, stored);
            Status = "Installing pack into " + id + "…";
            using var client = new ForgeClient();
            using var installer = new ModPackInstaller(client);
            var progress = new Progress<ModPackProgress>(update =>
            {
                Status = update.Message;
                dialog?.Update(update);
            });
            var result = await installer.InstallAsync(packSource, ManagerData, id, progress: progress, cancellationToken: cts.Token)
                .ConfigureAwait(true);
            var packStatus = result.Message ?? (result.Success ? "Pack installed." : "Pack install failed.");
            if (result.Warnings.Count > 0)
            {
                packStatus += Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }

            SelectProfileWithoutDeploy(id);
            if (!string.IsNullOrWhiteSpace(GameRoot) && Directory.Exists(GameRoot))
            {
                Deploy();
                Status = packStatus + Environment.NewLine + Status;
            }
            else
            {
                RefreshInventory();
                Status = packStatus;
            }
        }
        catch (Exception ex)
        {
            SelectProfileWithoutDeploy(id);
            RefreshInventory();
            Status = "Pack install failed: " + ex.Message;
        }
        finally
        {
            if (dialog is not null)
            {
                try
                {
                    dialog.MarkFinished();
                    if (dialog.IsVisible)
                    {
                        dialog.Close();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already closed by Cancel.
                }
            }

            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
            RefreshInventory();
            QueuePackUpdateCheck();
        }
    }

    private void QueuePackUpdateCheck()
    {
        _packCheckCts?.Cancel();
        _packCheckCts?.Dispose();
        _packCheckCts = new CancellationTokenSource();
        PackUpdateAvailable = false;
        PackUpdateSummary = string.Empty;
        _ = CheckPackUpdatesAsync(ManagerData, ProfileId, _packCheckCts.Token);
    }

    private async Task CheckPackUpdatesAsync(string managerData, string profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(managerData) || string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        try
        {
            var profile = new ProfileStore().TryRead(managerData, profileId);
            if (string.IsNullOrWhiteSpace(profile?.PackSource))
            {
                return;
            }

            var pack = await ModPackSource.LoadAsync(profile.PackSource, cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            var report = ModPackUpdateCheck.Compare(pack, profile.Enabled, ModStore.List(managerData));
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            PackUpdateAvailable = report.HasUpdates;
            PackUpdateSummary = report.Summary;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                PackUpdateAvailable = false;
                PackUpdateSummary = string.Empty;
            }
        }
    }

    private void QueueAppUpdateCheck()
    {
        _appCheckCts?.Cancel();
        _appCheckCts?.Dispose();
        _appCheckCts = new CancellationTokenSource();
        _ = CheckAppUpdatesAsync(notifyWhenCurrent: false, _appCheckCts.Token);
    }

    private void CheckAppUpdateNow()
        => _ = CheckAppUpdatesAsync(notifyWhenCurrent: true, CancellationToken.None);

    private async Task CheckAppUpdatesAsync(bool notifyWhenCurrent, CancellationToken cancellationToken)
    {
        if (IsCheckingAppUpdate)
        {
            return;
        }

        IsCheckingAppUpdate = true;
        if (notifyWhenCurrent)
        {
            AppUpdateStatus = "Checking GitHub…";
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var result = await AppUpdateCheck.CheckLatestAsync(http, cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ApplyAppUpdate(result, notifyWhenCurrent);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ApplyAppUpdate(AppUpdateCheckResult.Unavailable, notifyWhenCurrent);
            }
        }
        finally
        {
            IsCheckingAppUpdate = false;
        }
    }

    private void ApplyAppUpdate(AppUpdateCheckResult result, bool notifyWhenCurrent)
    {
        var info = result.Update;
        AppUpdateAvailable = result.Status == AppUpdateCheckStatus.UpdateAvailable && info is not null;
        AppUpdateSummary = info?.Summary ?? string.Empty;
        _appUpdate = info;
        if (AppUpdateAvailable)
        {
            AppUpdateStatus = info!.CanInstall
                ? info.Summary + " Click App update to download it and restart."
                : info.Summary + " Open the GitHub Release to download the zip.";
            return;
        }

        if (!notifyWhenCurrent)
        {
            return;
        }

        AppUpdateStatus = result.Status == AppUpdateCheckStatus.Current
            ? "You already have the latest GitHub Release (" + ProductInfo.Version + ")."
            : "Could not reach GitHub. Try again later, or open " + ProductInfo.ReleasesUrl + ".";
    }

    private void InstallAppUpdate()
        => _ = InstallAppUpdateAsync();

    private async Task InstallAppUpdateAsync()
    {
        var update = _appUpdate;
        if (update is null || IsBusy)
        {
            return;
        }

        foreach (var window in System.Windows.Application.Current.Windows.OfType<SettingsDialog>().ToList())
        {
            window.Close();
        }

        if (!update.CanInstall)
        {
            OpenReleasePage(update.ReleaseUrl, "This release has no zip the app can install. Open the download page instead?");
            return;
        }

        var targetDirectory = AppUpdateApply.TryGetInstallDirectory(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            OpenReleasePage(update.ReleaseUrl, "This build is not running as LonesSptManager.exe, so it cannot replace itself. Open the download page instead?");
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            update.Summary
            + Environment.NewLine
            + Environment.NewLine
            + "Download the GitHub Release and restart this app? Manager data and your SPT install stay put.",
            ProductInfo.Name,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Information);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppUpdateApply.EnsureInstallFolderWritable(targetDirectory);
        }
        catch (Exception ex)
        {
            OpenReleasePage(
                update.ReleaseUrl,
                "This folder is not writable (" + ex.Message + "). Open the download page and replace the exe yourself?");
            return;
        }

        IsBusy = true;
        BusyMessage = "Updating Lone's SPT Manager…";
        CommandManager.InvalidateRequerySuggested();
        var owner = System.Windows.Application.Current?.MainWindow;
        var dialog = owner is null ? null : new ProgressDialog("Updating — " + update.LatestVersion) { Owner = owner };
        using var cts = new CancellationTokenSource();
        if (dialog is not null)
        {
            dialog.CancelRequested += () => cts.Cancel();
            dialog.Show();
        }

        var work = Path.Combine(Path.GetTempPath(), "LonesSptManager-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var downloadPath = Path.Combine(work, "payload-" + update.AssetName);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var progress = new Progress<AppUpdateProgress>(item =>
            {
                Status = item.Message;
                dialog?.Update(item.Message, item.Current, item.Total);
            });
            await AppUpdateApply.DownloadAsync(http, update, downloadPath, progress, cts.Token).ConfigureAwait(true);
            cts.Token.ThrowIfCancellationRequested();
            dialog?.Update("Preparing the new exe…", 1, 1);
            AppUpdateApply.UnpackRelease(downloadPath, work);
            File.Delete(downloadPath);
            var plan = AppUpdateApply.WriteApplyScript(Environment.ProcessId, work, targetDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = plan.ScriptPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Status = "Restarting to finish the update…";
            System.Windows.Application.Current?.Shutdown();
        }
        catch (OperationCanceledException)
        {
            Status = "Update cancelled.";
            TryDeleteDirectory(work);
        }
        catch (Exception ex)
        {
            Status = "Update failed: " + ex.Message;
            TryDeleteDirectory(work);
            System.Windows.MessageBox.Show(
                "Could not install the update: " + ex.Message,
                ProductInfo.Name,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            if (dialog is not null)
            {
                try
                {
                    dialog.MarkFinished();
                    if (dialog.IsVisible)
                    {
                        dialog.Close();
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OpenReleasePage(string? url, string prompt)
    {
        var target = string.IsNullOrWhiteSpace(url) ? ProductInfo.ReleasesUrl : url;
        var confirm = System.Windows.MessageBox.Show(
            prompt,
            ProductInfo.Name,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Information);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Status = "Could not open the release page: " + ex.Message;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
        }
    }

    private static string? TryNormalizePackSource(string packSource)
    {
        try
        {
            return ModPackSource.Normalize(packSource);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void SelectProfileWithoutDeploy(string id)
    {
        _refreshingProfiles = true;
        try
        {
            var ids = ProfilePaths.ListProfileIds(ManagerData).ToList();
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(id);
            }

            ProfileIds.Clear();
            foreach (var item in ids.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                ProfileIds.Add(item);
            }

            _profileId = id;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileId)));
        }
        finally
        {
            _refreshingProfiles = false;
        }
    }

    private void EditProfile()
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is null)
        {
            return;
        }

        var packSource = new ProfileStore().TryRead(ManagerData, ProfileId)?.PackSource;
        var result = ProfileDialog.ShowEdit(owner, ProfileId, packSource);
        if (result.Action == ProfileDialogAction.Delete)
        {
            DeleteCurrentProfile();
            return;
        }

        if (result.Action == ProfileDialogAction.Update)
        {
            if (string.IsNullOrWhiteSpace(packSource))
            {
                Status = "This profile has no saved pack link to update from.";
                return;
            }

            _ = InstallPackProfileAsync(ProfileId, packSource);
            return;
        }

        if (result.Action is ProfileDialogAction.Cancel || string.IsNullOrWhiteSpace(result.Name))
        {
            return;
        }

        if (result.Action == ProfileDialogAction.Copy)
        {
            var copied = ProfileCopier.Copy(ManagerData, ProfileId, result.Name);
            Status = copied.Message ?? (copied.Success ? "Copied." : "Copy failed.");
            if (copied.Success && copied.DestinationId is not null)
            {
                RefreshProfiles();
                ProfileId = copied.DestinationId;
            }

            return;
        }

        var renamed = ProfileStore.Rename(ManagerData, ProfileId, result.Name);
        Status = renamed.Message ?? (renamed.Success ? "Renamed." : "Rename failed.");
        if (renamed.Success && renamed.DestinationId is not null)
        {
            RefreshProfiles();
            ProfileId = renamed.DestinationId;
        }
    }

    private void DeleteCurrentProfile()
    {
        var doomed = ProfileId;
        var others = ProfileIds
            .Where(id => !id.Equals(doomed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (others.Count == 0)
        {
            Status = "Can't delete the last profile.";
            return;
        }

        var owner = System.Windows.Application.Current?.MainWindow;
        var confirm = System.Windows.MessageBox.Show(
            owner,
            $"Delete profile '{doomed}'?\n\nThis removes that profile's saves, BepInEx configs, Overwrite, and generated files. Mods in the store stay.",
            "Delete profile",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var running = new SptProcessLock().RunningSptProcesses();
        if (running.Count > 0)
        {
            Status = "Can't delete a profile while SPT is running: " + string.Join(", ", running);
            return;
        }

        var fallback = others[0];
        SelectProfileWithoutDeploy(fallback);
        if (!string.IsNullOrWhiteSpace(GameRoot) && Directory.Exists(GameRoot))
        {
            Deploy();
        }

        var deleted = ProfileStore.Delete(ManagerData, doomed);
        Status = deleted.Message ?? (deleted.Success ? "Deleted." : "Delete failed.");
        RefreshProfiles();
        if (deleted.Success)
        {
            ProfileId = fallback;
        }
        else
        {
            RefreshInventory();
        }
    }

    private void CopyRuntimeToProfile()
    {
        if (SelectedInventoryItem is not { Kind: InstallInventory.StoreKind, RuntimeFileCount: > 0 } item)
        {
            return;
        }

        var others = OtherProfileIds();
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is null || others.Count == 0)
        {
            return;
        }

        var dest = ChooseProfileDialog.Show(
            owner,
            "Copy generated files",
            "Copy " + item.Key + " generated files from " + ProfileId + " to:",
            others);
        if (string.IsNullOrWhiteSpace(dest))
        {
            return;
        }

        var copied = ProfileCopier.CopyRuntimeMod(ManagerData, ProfileId, dest, item.Key);
        Status = copied.Message ?? (copied.Success ? "Copied." : "Copy failed.");
    }

    private IReadOnlyList<string> OtherProfileIds()
        => ProfilePaths.ListProfileIds(ManagerData)
            .Where(id => !id.Equals(ProfileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private void DiscardOverwrite()
    {
        HarvestEngine.DiscardAll(ManagerData, ProfileId);
        Status = "Cleared Overwrite for profile " + ProfilePaths.Sanitize(ProfileId) + ".";
        RefreshOverwrite();
    }

    private void DiscardSelectedOverwrite()
    {
        if (SelectedOverwritePath is null)
        {
            return;
        }

        HarvestEngine.DiscardPaths(ManagerData, ProfileId, [SelectedOverwritePath]);
        Status = "Discarded Overwrite file " + SelectedOverwritePath + ".";
        RefreshOverwrite();
    }

    private void AssignOverwrite()
    {
        if (SelectedOverwritePath is null
            || SelectedInventoryItem is not { Kind: InstallInventory.StoreKind, Version: not null } item)
        {
            return;
        }

        try
        {
            var assigned = HarvestEngine.AssignToMod(
                ManagerData,
                ProfileId,
                item.Key,
                item.Version,
                [SelectedOverwritePath]);
            Status = $"Assigned {assigned.AssignedCount} Overwrite file(s) onto {assigned.ModKey} for this profile. Deploy to apply.";
            RefreshInventory();
            RefreshOverwrite();
        }
        catch (Exception ex)
        {
            Status = "Assign Overwrite failed: " + ex.Message;
        }
    }

    private void ImportLeftover()
    {
        if (SelectedInventoryItem is not { Kind: InstallInventory.LeftoverKind, InstallRelative: not null } item)
        {
            return;
        }

        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var result = new InstallMapper().ImportInstallTree(GameRoot, item.InstallRelative, ManagerData);
            Status = result.Message ?? "Leftover import finished.";
            if (result.Map.Warnings.Count > 0)
            {
                Status += Environment.NewLine + string.Join(Environment.NewLine, result.Map.Warnings);
            }

            if (result.Document is not null)
            {
                AddImportedToProfile(result.Document);
            }

            RefreshInventory();
        }
        catch (Exception ex)
        {
            Status = "Leftover import failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void AddImportedToProfile(ModDocument document)
    {
        if (!document.Deployable)
        {
            return;
        }

        InstallInventory.AddToLoadOrder(ManagerData, ProfileId, document.ModKey, document.Version);
        Status += Environment.NewLine + "Enabled on profile " + ProfilePaths.Sanitize(ProfileId) + " (end of load order).";
    }

    private void SearchForge()
    {
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            using var client = new ForgeClient();
            var mods = client.ListModsAsync(ForgeQuery).GetAwaiter().GetResult();
            ForgeHits.Clear();
            foreach (var hit in ForgeClient.ToSearchHits(mods))
            {
                ForgeHits.Add(hit);
            }

            Status = ForgeHits.Count == 0
                ? "No Forge mods matched."
                : $"Forge returned {ForgeHits.Count} mod(s). Select one and Install.";
            _ = CacheForgeThumbnailsAsync(ForgeHits.Select(hit => hit.Thumbnail).ToArray());
        }
        catch (Exception ex)
        {
            Status = "Forge search failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void InstallForge()
    {
        if (SelectedForgeHit is null)
        {
            return;
        }

        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            using var client = new ForgeClient();
            var result = new ForgeInstaller(client).InstallAsync(
                    SelectedForgeHit.ModId,
                    ManagerData,
                    ProfileId,
                    requestedVersion: SelectedForgeHit.Version)
                .GetAwaiter()
                .GetResult();
            Status = result.Message ?? (result.Success ? "Installed." : "Forge install failed.");
            if (result.Warnings.Count > 0)
            {
                Status += Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }

            RefreshInventory();
        }
        catch (Exception ex)
        {
            Status = "Forge install failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CheckUpdates()
    {
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            using var client = new ForgeClient();
            Status = new ForgeInstaller(client).CheckUpdatesAsync(ManagerData, "4.1.2").GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Status = "Forge update check failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async void Launch(string mode)
    {
        LaunchMode = mode;
        await LaunchAsync().ConfigureAwait(true);
    }

    private async Task LaunchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        BusyMessage = LaunchModes.StartsServer(LaunchMode)
            ? "Starting SPT.Server…"
            : "Starting SPT.Launcher…";
        IsBusy = true;
        await Task.Yield();
        try
        {
            var request = new LaunchRequest
            {
                GameRoot = GameRoot,
                ManagerData = ManagerData,
                ProfileId = ProfileId,
                Mode = LaunchMode,
                JoinUrl = string.IsNullOrWhiteSpace(JoinUrl) ? null : JoinUrl
            };
            var engine = new LaunchEngine();
            var progress = new Progress<string>(text => BusyMessage = text);
            var result = await Task.Run(() => engine.Launch(request, progress)).ConfigureAwait(true);
            Status = result.Message ?? (result.Success ? "Launched." : "Launch failed.");
            if (result.Warnings.Count > 0)
            {
                Status += Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }

            if (!result.Success)
            {
                return;
            }

            BusyMessage = result.StartedServer
                ? "Waiting for server and client to quit…"
                : "Waiting for the client to quit…";
            await engine.WaitUntilIdleAsync().ConfigureAwait(true);
            await HarvestAsync(alreadyBusy: true).ConfigureAwait(true);
            Status = "SPT has quit." + Environment.NewLine + Status;
        }
        catch (Exception ex)
        {
            Status = "Launch failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshInventory()
    {
        var selectedKey = SelectedModRow?.Item.Key;
        var selectedVersion = SelectedModRow?.Item.Version;
        if (string.IsNullOrWhiteSpace(ManagerData))
        {
            InventoryItems.Clear();
            return;
        }

        var promoted = 0;
        if (!string.IsNullOrWhiteSpace(ProfileId))
        {
            try
            {
                promoted = HarvestEngine.PromoteOverwriteConfigs(ManagerData, ProfileId).Count;
            }
            catch (Exception)
            {
                // Listing / attach must not break the mod list.
            }
        }

        List<ModRowViewModel> rows;
        try
        {
            var snap = InstallInventory.Scan(
                string.IsNullOrWhiteSpace(GameRoot) ? null : GameRoot,
                ManagerData,
                ProfileId);
            rows = OrderForModList(snap.Items)
                .Select(item => new ModRowViewModel(item, ManagerData, OnRowToggled))
                .ToList();
        }
        catch (Exception ex)
        {
            Status = "Could not refresh the mod list: " + ex.Message;
            return;
        }

        InventoryItems.Clear();
        foreach (var row in rows)
        {
            InventoryItems.Add(row);
        }

        SelectedModRow = InventoryItems.FirstOrDefault(row =>
            row.Item.Key == selectedKey
            && string.Equals(row.Item.Version, selectedVersion, StringComparison.OrdinalIgnoreCase));

        RefreshOverwrite();
        if (promoted > 0)
        {
            Status = $"Attached {promoted} config file(s) to their mods. Generated state stayed in Overwrite.";
        }

        if (!_hydratingThumbnails && !_busy)
        {
            _ = HydrateMissingThumbnailsAsync();
        }
    }

    private void RefreshProfiles()
    {
        _refreshingProfiles = true;
        try
        {
            var current = ProfileId;
            var ids = ProfilePaths.ListProfileIds(ManagerData).ToList();
            if (!string.IsNullOrWhiteSpace(current)
                && !ids.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(current);
            }

            if (ids.Count == 0)
            {
                ids.Add(ProfilePaths.DefaultProfileId);
            }

            ProfileIds.Clear();
            foreach (var id in ids.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                ProfileIds.Add(id);
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                _profileId = current;
            }
        }
        finally
        {
            _refreshingProfiles = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileId)));
        }
    }

    private static IEnumerable<InventoryItem> OrderForModList(IReadOnlyList<InventoryItem> items)
    {
        var store = items
            .Where(item => item.Kind == InstallInventory.StoreKind)
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var leftovers = items.Where(item => item.Kind == InstallInventory.LeftoverKind);
        return store.Concat(leftovers);
    }

    private async Task HydrateMissingThumbnailsAsync()
    {
        var missing = InventoryItems
            .Select(row => row.Item)
            .Where(item => item.Kind == InstallInventory.StoreKind && item.ForgeModId is > 0)
            .Where(NeedsThumbnailHydration)
            .GroupBy(item => item.ForgeModId!.Value)
            .Select(group => group.First())
            .ToArray();
        if (missing.Length == 0 || string.IsNullOrWhiteSpace(ManagerData) || _hydratingThumbnails)
        {
            return;
        }

        _hydratingThumbnails = true;
        try
        {
            using var client = new ForgeClient();
            var changed = false;
            foreach (var item in missing)
            {
                try
                {
                    var thumb = ThumbnailCache.IsAllowedUrl(item.ThumbnailUrl) ? item.ThumbnailUrl : null;
                    if (string.IsNullOrWhiteSpace(thumb) || string.IsNullOrWhiteSpace(item.DisplayName))
                    {
                        var details = await client.GetModAsync(item.ForgeModId!.Value).ConfigureAwait(true);
                        thumb = ThumbnailCache.IsAllowedUrl(details?.Thumbnail) ? details!.Thumbnail : thumb;
                        var name = string.IsNullOrWhiteSpace(details?.Name) ? null : details!.Name;
                        if (thumb is not null || name is not null)
                        {
                            foreach (var document in ModStore.List(ManagerData)
                                         .Where(doc => doc.ForgeModId == item.ForgeModId))
                            {
                                ThumbnailCache.WriteModJsonForgeInfo(ManagerData, document, name, thumb);
                                changed = true;
                            }
                        }
                    }

                    if (thumb is not null && ThumbnailCache.TryLocalPath(ManagerData, thumb) is null)
                    {
                        await CacheOneThumbnailAsync(client, thumb).ConfigureAwait(true);
                        changed = true;
                    }

                    await Task.Delay(250).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // One missing catalogue row must not stop the rest.
                }
            }

            if (changed)
            {
                RefreshInventory();
            }
        }
        catch (Exception)
        {
            // Catalogue lookup is best-effort.
        }
        finally
        {
            _hydratingThumbnails = false;
        }
    }

    private bool NeedsThumbnailHydration(InventoryItem item)
        => string.IsNullOrWhiteSpace(item.DisplayName)
           || string.IsNullOrWhiteSpace(item.ThumbnailUrl)
           || (ThumbnailCache.IsAllowedUrl(item.ThumbnailUrl)
               && ThumbnailCache.TryLocalPath(ManagerData, item.ThumbnailUrl) is null);

    private async Task CacheForgeThumbnailsAsync(IReadOnlyList<string?> urls)
    {
        if (string.IsNullOrWhiteSpace(ManagerData))
        {
            return;
        }

        try
        {
            using var client = new ForgeClient();
            foreach (var url in urls.Where(ThumbnailCache.IsAllowedUrl).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await CacheOneThumbnailAsync(client, url!).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // Search still works without cached thumbs.
        }
    }

    private async Task CacheOneThumbnailAsync(ForgeClient client, string url)
    {
        var dest = ThumbnailCache.LocalPathFor(ManagerData, url);
        if (File.Exists(dest))
        {
            return;
        }

        await client.DownloadAsync(url, dest, expectedContentLength: null).ConfigureAwait(true);
    }

    private void OnRowToggled(ModRowViewModel row, bool enabled)
    {
        if (row.Item.Version is null)
        {
            row.SyncChecked(!enabled);
            return;
        }

        SelectedModRow = row;
        ToggleSelected(enabled);
    }

    private void RefreshOverwrite()
    {
        OverwriteItems.Clear();
        if (string.IsNullOrWhiteSpace(ManagerData) || string.IsNullOrWhiteSpace(ProfileId))
        {
            return;
        }

        try
        {
            foreach (var path in HarvestEngine.ListOverwrite(ManagerData, ProfileId))
            {
                OverwriteItems.Add(new OverwriteRowViewModel(path));
            }
        }
        catch (Exception)
        {
            // Listing must not break harvest.
        }
    }

    private void RestoreLastInstance()
    {
        if (string.IsNullOrWhiteSpace(ManagerData) || !string.IsNullOrWhiteSpace(GameRoot))
        {
            return;
        }

        var latest = InstanceStore.TryLatest(ManagerData);
        if (latest is not null && Directory.Exists(latest.GameRoot))
        {
            _gameRoot = latest.GameRoot;
        }
    }

    private void RestoreLastProfile()
    {
        if (string.IsNullOrWhiteSpace(ManagerData))
        {
            return;
        }

        var last = ProfileStore.TryLastUsedProfileId(ManagerData);
        if (!string.IsNullOrWhiteSpace(last))
        {
            _profileId = last;
        }
    }

    private void ToggleSelected(bool enabled)
    {
        if (SelectedInventoryItem is not { Kind: InstallInventory.StoreKind, Version: not null } item)
        {
            return;
        }

        InstallInventory.SetEnabled(ManagerData, ProfileId, item.Key, item.Version, enabled);
        RefreshInventory();
        Status = (enabled ? "Enabled " : "Disabled ") + item.Key + " " + item.Version + ". Deploy to apply.";
    }

    private void MoveSelected(int delta)
    {
        if (SelectedInventoryItem is not { Kind: InstallInventory.StoreKind, Version: not null } item)
        {
            return;
        }

        InstallInventory.MovePriority(ManagerData, ProfileId, item.Key, item.Version, delta);
        RefreshInventory();
        Status = "Changed load order for " + item.Key + ". Deploy to apply.";
    }

    public void ReorderLoadOrder(ModRowViewModel source, ModRowViewModel target, bool after)
    {
        if (_busy
            || source.Item.Kind != InstallInventory.StoreKind
            || source.Item.Version is null
            || string.IsNullOrWhiteSpace(ManagerData))
        {
            return;
        }

        var dest = target;
        var insertAfter = after;
        if (target.Item.Kind != InstallInventory.StoreKind || target.Item.Version is null)
        {
            dest = InventoryItems.LastOrDefault(row => row.Item.Kind == InstallInventory.StoreKind);
            insertAfter = true;
        }

        if (dest is null || dest.Item.Version is null || ReferenceEquals(source, dest))
        {
            return;
        }

        InstallInventory.MoveTo(
            ManagerData,
            ProfileId,
            source.Item.Key,
            source.Item.Version,
            dest.Item.Key,
            dest.Item.Version,
            insertAfter);
        RefreshInventory();
        Status = "Changed load order for " + source.Item.Key + ". Deploy to apply.";
    }

    private void LoadProfileLaunchSettings()
    {
        if (string.IsNullOrWhiteSpace(ManagerData) || string.IsNullOrWhiteSpace(ProfileId))
        {
            return;
        }

        var profile = new ProfileStore().TryRead(ManagerData, ProfileId);
        if (profile is null)
        {
            return;
        }

        LaunchMode = profile.LaunchMode;
        if (!string.IsNullOrWhiteSpace(profile.JoinUrl))
        {
            JoinUrl = profile.JoinUrl;
        }
    }

    private void Bind()
    {
        IsBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var binder = new SptInstanceBinder();
            var result = binder.Bind(GameRoot);
            if (!result.IsSuccess)
            {
                Status = result.Message ?? result.Status.ToString();
                if (result.MissingPaths.Count > 0)
                {
                    Status += Environment.NewLine + "Missing: " + string.Join(", ", result.MissingPaths);
                }

                return;
            }

            var baseline = new SptOwnedBaselineBuilder().Build(result.GameRoot);
            var document = new InstanceStore().Save(ManagerData, result, baseline);
            Status = result.Message
                     + Environment.NewLine
                     + $"Instance {document.InstanceId} saved. SPT-owned baseline files: {baseline.Files.Count}."
                     + Environment.NewLine
                     + $"user\\mods present: {result.HasUserModsDirectory}; user\\launcher\\config.json present: {result.HasUserLauncherConfig}.";
            if (!string.Equals(document.GameRootVolumeId, document.ManagerDataVolumeId, StringComparison.OrdinalIgnoreCase)
                && document.GameRootVolumeId is not null
                && document.ManagerDataVolumeId is not null)
            {
                Status += Environment.NewLine
                          + "Store and game are on different volumes. Directory overlays still use junctions; any leftover loose files are copied.";
            }

            RefreshInventory();
        }
        catch (Exception ex)
        {
            Status = "Bind failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OpenSettings()
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        var dialog = new SettingsDialog(this);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }

    private bool MatchesModFilter(object item)
        => item is ModRowViewModel row && row.MatchesFilter(_modFilter);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
