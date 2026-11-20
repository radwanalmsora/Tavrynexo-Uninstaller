using System.Windows;
using Tavrynexo.Core;

namespace Tavrynexo.Views;

public static class Dialogs
{
    private static MessageBoxOptions Opt =>
        L.IsArabic ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;

    private static MessageBoxResult Show(Window? owner, string text, MessageBoxButton b, MessageBoxImage i)
    {
        var caption = L.Get("AppTitle");
        return owner == null
            ? MessageBox.Show(text, caption, b, i, MessageBoxResult.None, Opt)
            : MessageBox.Show(owner, text, caption, b, i, MessageBoxResult.None, Opt);
    }

    public static void Info(Window? owner, string text) => Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Information);
    public static void Warn(Window? owner, string text) => Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Warning);
    public static void Error(Window? owner, string text) => Show(owner, text, MessageBoxButton.OK, MessageBoxImage.Error);

    public static bool Confirm(Window? owner, string text, bool warning = false) =>
        Show(owner, text, MessageBoxButton.YesNo, warning ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes;
}
