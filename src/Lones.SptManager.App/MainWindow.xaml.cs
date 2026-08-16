using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lones.SptManager.Core.Inventory;

namespace Lones.SptManager.App;

public partial class MainWindow : System.Windows.Window
{
    private System.Windows.Point _dragStart;
    private ModRowViewModel? _dragRow;
    private ListBoxItem? _dropHint;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.RepairOnStart();
    }

    private void ModList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>(source);
        if (item is not null)
        {
            item.IsSelected = true;
        }
    }

    private void ModList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragRow = null;
        if (e.OriginalSource is not DependencyObject source
            || FindAncestor<System.Windows.Controls.CheckBox>(source) is not null
            || FindAncestor<ListBoxItem>(source) is not { DataContext: ModRowViewModel row }
            || row.Item.Kind != InstallInventory.StoreKind)
        {
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragRow = row;
    }

    private void ModList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragRow is null
            || e.LeftButton != MouseButtonState.Pressed
            || DataContext is not MainViewModel)
        {
            return;
        }

        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var row = _dragRow;
        _dragRow = null;
        System.Windows.DragDrop.DoDragDrop(ModList, row, System.Windows.DragDropEffects.Move);
        ClearDropHint();
    }

    private void ModList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => _dragRow = null;

    private void ModList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDragRow(e, out _))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            ClearDropHint();
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Move;
        if (RowFromPoint(e.GetPosition(ModList)) is { } target)
        {
            SetDropHint(target.Item, AfterHalf(target.Item, e.GetPosition(target.Item)));
        }
        else if (LastStoreItem() is { } last)
        {
            SetDropHint(last, after: true);
        }

        e.Handled = true;
    }

    private void ModList_DragLeave(object sender, System.Windows.DragEventArgs e)
        => ClearDropHint();

    private void ModList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ClearDropHint();
        if (DataContext is not MainViewModel viewModel || !TryGetDragRow(e, out var source))
        {
            return;
        }

        if (RowFromPoint(e.GetPosition(ModList)) is { } target)
        {
            viewModel.ReorderLoadOrder(source, target.Row, AfterHalf(target.Item, e.GetPosition(target.Item)));
        }
        else if (viewModel.InventoryItems.LastOrDefault(row => row.Item.Kind == InstallInventory.StoreKind) is { } last)
        {
            viewModel.ReorderLoadOrder(source, last, after: true);
        }

        e.Handled = true;
    }

    private (ModRowViewModel Row, ListBoxItem Item)? RowFromPoint(System.Windows.Point point)
    {
        if (VisualTreeHelper.HitTest(ModList, point)?.VisualHit is not DependencyObject hit
            || FindAncestor<ListBoxItem>(hit) is not { DataContext: ModRowViewModel row } item)
        {
            return null;
        }

        return (row, item);
    }

    private static bool TryGetDragRow(System.Windows.DragEventArgs e, out ModRowViewModel row)
    {
        row = null!;
        if (!e.Data.GetDataPresent(typeof(ModRowViewModel))
            || e.Data.GetData(typeof(ModRowViewModel)) is not ModRowViewModel found
            || found.Item.Kind != InstallInventory.StoreKind)
        {
            return false;
        }

        row = found;
        return true;
    }

    private static bool AfterHalf(ListBoxItem item, System.Windows.Point point)
        => point.Y > item.ActualHeight / 2;

    private void SetDropHint(ListBoxItem item, bool after)
    {
        if (!ReferenceEquals(_dropHint, item))
        {
            ClearDropHint();
            _dropHint = item;
        }

        item.BorderBrush = TryFindResource("AccentBrush") as System.Windows.Media.Brush
                           ?? System.Windows.Media.Brushes.DodgerBlue;
        item.BorderThickness = after ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
    }

    private void ClearDropHint()
    {
        if (_dropHint is null)
        {
            return;
        }

        _dropHint.BorderThickness = new Thickness(0);
        _dropHint.BorderBrush = System.Windows.Media.Brushes.Transparent;
        _dropHint = null;
    }

    private ListBoxItem? LastStoreItem()
    {
        for (var i = ModList.Items.Count - 1; i >= 0; i--)
        {
            if (ModList.Items[i] is ModRowViewModel { Item.Kind: var kind }
                && kind == InstallInventory.StoreKind
                && ModList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                return item;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
