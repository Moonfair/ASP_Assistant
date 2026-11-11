using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ASPAssistant.App.Services;

internal static class LineupProtocol
{
    private const string Argument = "--lineup-uri";
    private const string Prefix = "aspassistant://import-lineup/?sha256=";

    internal static void Register()
    {
        var executable = Environment.ProcessPath;
        if (!string.Equals(Path.GetFileName(executable), "ASPAssistant.App.exe", StringComparison.OrdinalIgnoreCase))
            return;

        using var protocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\aspassistant");
        protocol.SetValue("", "URL:ASP Assistant");
        protocol.SetValue("URL Protocol", "");
        using var command = protocol.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{executable}\" {Argument} \"%1\"");
    }

    internal static string? ParseImport(string[] args)
    {
        if (args.Length != 2 || args[0] != Argument || !args[1].StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var hash = args[1][Prefix.Length..];
        return IsHash(hash) ? hash : null;
    }

    internal static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    internal static string RequireMatchingShare(string text, string expectedHash)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("剪贴板中的内容已更改，请回到网页重新点击「去使用」。");

        return normalized;
    }
}
