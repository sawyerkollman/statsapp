using System.Windows;
using System.Windows.Media;

namespace Stats.UiPreview.Views;

/// <summary>Small visual-tree search helpers used to drive substates that need a real popup/menu/dropdown to
/// open (theme dropdown, tile context menu, the Alerts tab) — driven through the same routed-event/property paths
/// a mouse click or ComboBox selection would use, never through reflection into private members.</summary>
internal static class VisualTreeUtil
{
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var grandchild in Descendants<T>(child)) yield return grandchild;
        }
    }

    public static T? FirstDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject =>
        Descendants<T>(root).FirstOrDefault(t => predicate is null || predicate(t));

    public static T? FirstAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
