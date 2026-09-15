using System.Windows;
using System.Windows.Controls;

namespace Stats.App.Controls;

/// <summary>Tile-resize-by-drag (owner decision assumed §2): the Free/Snap dashboard canvas's tile
/// <see cref="ItemsControl"/>, whose generated containers are plain <see cref="ContentControl"/>s instead of the
/// default <see cref="ContentPresenter"/> — so <c>DashboardWindow</c>'s <c>ItemContainerStyle</c> can give each
/// one a <see cref="System.Windows.Controls.ControlTemplate"/> (a <see cref="ContentPresenter"/> plus the resize
/// grip) without touching any of the five tile <c>DataTemplate</c>s or the Auto <see cref="ItemsControl"/>, both
/// of which stay byte-identical. Nothing else about item generation changes: <see cref="ItemsSource"/>,
/// <see cref="ItemsControl.ItemTemplateSelector"/>, and the <c>Canvas</c> <c>ItemsPanel</c> are all set the same
/// way they were on the plain <see cref="ItemsControl"/> this replaces.</summary>
public sealed class TileCanvasItemsControl : ItemsControl
{
    protected override DependencyObject GetContainerForItemOverride() => new ContentControl();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is ContentControl;
}
