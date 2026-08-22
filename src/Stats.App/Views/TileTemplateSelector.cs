using System.Windows;
using System.Windows.Controls;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

/// <summary>Picks a tile DataTemplate by Size (S → compact) then Kind. Templates are assigned from XAML resources.</summary>
public sealed class TileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Compact { get; set; }
    public DataTemplate? Sparkline { get; set; }
    public DataTemplate? Gauge { get; set; }
    public DataTemplate? Bar { get; set; }
    public DataTemplate? Value { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not MetricTileViewModel tile) return null;
        if (tile.Size == TileSize.S) return Compact;
        return tile.Kind switch
        {
            TileKind.Gauge => Gauge,
            TileKind.Bar => Bar,
            TileKind.Value => Value,
            _ => Sparkline,
        };
    }
}
