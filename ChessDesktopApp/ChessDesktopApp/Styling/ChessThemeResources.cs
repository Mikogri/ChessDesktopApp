using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChessDesktopApp.Styling;

/// <summary>
/// Provides theme values to code-driven UI while keeping the source of truth in ChessTheme.xaml.
/// The fallback is used only when view models are created without an App instance, for example in tests.
/// </summary>
public static class ChessThemeResources
{
    public static SolidColorBrush Brush(string resourceKey, Color fallback)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true &&
                resource is SolidColorBrush brush)
            {
                return brush;
            }
        }
        catch (InvalidOperationException)
        {
            // WinUI resources are unavailable in headless tests.
        }

        return new SolidColorBrush(fallback);
    }

    public static Color Color(string resourceKey, Color fallback)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true)
            {
                if (resource is Color color) return color;
                if (resource is SolidColorBrush brush) return brush.Color;
            }
        }
        catch (InvalidOperationException)
        {
            // WinUI resources are unavailable in headless tests.
        }

        return fallback;
    }
}
