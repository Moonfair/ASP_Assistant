using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ASPAssistant.App.Converters;

/// <summary>
/// Converts a relative <c>data/</c>-directory path string (e.g.
/// <c>icons/enemies/enemy_1041_lazerd.png</c>) to a WPF <see cref="BitmapImage"/>.
///
/// The full path is resolved as
/// <c>AppContext.BaseDirectory + "data" + iconPath</c>.
/// Returns <c>null</c> when the path is empty or the file does not exist.
/// </summary>
[ValueConversion(typeof(string), typeof(BitmapImage))]
public sealed class LocalPathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string iconPath || string.IsNullOrEmpty(iconPath))
            return null;

        var fullPath = Path.Combine(AppContext.BaseDirectory, "data", iconPath);
        if (!File.Exists(fullPath))
            return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(fullPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
