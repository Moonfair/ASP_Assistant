using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace ASPAssistant.Mac;

internal static class AssetImages
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Image Create(string dataDirectory, string? relativePath, int size = 48)
    {
        Bitmap? bitmap = null;
        if (!string.IsNullOrWhiteSpace(relativePath) && !Path.IsPathRooted(relativePath))
        {
            var path = Path.GetFullPath(Path.Combine(dataDirectory, relativePath));
            if (path.StartsWith(Path.GetFullPath(dataDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                lock (Cache)
                {
                    if (!Cache.TryGetValue(path, out bitmap))
                    {
                        try { bitmap = File.Exists(path) ? new Bitmap(path) : null; }
                        catch (Exception ex) when (ex is IOException or ArgumentException) { bitmap = null; }
                        Cache[path] = bitmap;
                    }
                }
            }
        }
        return new Image { Source = bitmap, Width = size, Height = size,
            Stretch = Avalonia.Media.Stretch.UniformToFill };
    }
}
