using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace ASPAssistant.App.Services;

internal sealed partial class LineupSiteClient
{
    private const int FeedbackLogBytes = 8 * 1024 * 1024;
    private static readonly Regex DailyLogName = new(@"\Aaspa-[0-9]{4}-[0-9]{2}-[0-9]{2}\.log\z");

    internal async Task<string> SendFeedbackAsync()
    {
        var archive = await Task.Run(CreateFeedbackArchive);
        var content = new ByteArrayContent(archive);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var result = await SendContentAsync<Identifier>(HttpMethod.Post, "api/desktop/feedback", content,
            CancellationToken.None, timeout: TimeSpan.FromSeconds(150));
        if (!Guid.TryParseExact(result.Id, "D", out _))
            throw new LineupSiteException("阵容库返回的反馈编号无效，请稍后重试。");
        return result.Id;
    }

    private byte[] CreateFeedbackArchive()
    {
        var remaining = FeedbackLogBytes;
        using var output = new MemoryStream();
        try
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var directory = new DirectoryInfo(_logDirectory);
            if (directory.Exists)
            {
                var logs = directory.EnumerateFiles("*.log")
                    .Where(file => (file.Name == "update-errors.log" || DailyLogName.IsMatch(file.Name))
                        && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderByDescending(file => file.LastWriteTimeUtc);
                foreach (var log in logs)
                {
                    using var input = log.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var length = input.Length;
                    var count = (int)Math.Min(length, remaining);
                    if (count == 0) continue;

                    // Snapshot the length so ongoing writes cannot grow the upload.
                    input.Position = length - count;
                    var bytes = new byte[count];
                    input.ReadExactly(bytes);
                    using var entry = archive.CreateEntry(log.Name).Open();
                    entry.Write(bytes);
                    remaining -= count;
                    if (remaining == 0) break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LineupSiteException("无法读取本地日志，请稍后重试。");
        }

        if (remaining == FeedbackLogBytes)
            throw new LineupSiteException("没有可发送的本地日志。");
        return output.ToArray();
    }
}
