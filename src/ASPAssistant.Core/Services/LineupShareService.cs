using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// 阵容分享/导入：通过 dpaste.com 匿名 API 把阵容 JSON 上传成短链接。
/// 文档：https://dpaste.com/api/v2/
/// </summary>
public class LineupShareService
{
    private const string DpasteEndpoint = "https://dpaste.com/api/v2/";
    private const int ExpiryDays = 365; // dpaste 上限

    private static readonly Regex DpasteUrlRegex =
        new(@"https?://dpaste\.com/[A-Za-z0-9]+", RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public LineupShareService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ASPAssistant-LineupShare/1.0");
        }
    }

    /// <summary>把阵容序列化后上传到 dpaste，返回短链接 URL（如 https://dpaste.com/XXXXX）。</summary>
    public async Task<string> ExportAsync(Lineup lineup, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(lineup, JsonOptions);

        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("content", json),
            new KeyValuePair<string, string>("syntax", "json"),
            new KeyValuePair<string, string>("expiry_days", ExpiryDays.ToString()),
        });

        using var response = await _http.PostAsync(DpasteEndpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
        var match = DpasteUrlRegex.Match(body);
        if (!match.Success)
            throw new InvalidOperationException($"dpaste 响应不含可识别的 URL：{body}");
        return match.Value;
    }

    /// <summary>
    /// 从 dpaste 短链接拉取并反序列化阵容；URL 不合法或非 dpaste 链接时抛出。
    /// 也支持直接传入 JSON 字符串，方便用户手动粘贴。
    /// </summary>
    public async Task<Lineup> ImportAsync(string urlOrJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrJson))
            throw new ArgumentException("导入内容为空", nameof(urlOrJson));

        urlOrJson = urlOrJson.Trim();

        string json;
        var match = DpasteUrlRegex.Match(urlOrJson);
        if (match.Success)
        {
            // 取 raw 文本视图
            var rawUrl = match.Value.TrimEnd('/') + ".txt";
            json = await _http.GetStringAsync(rawUrl, ct);
        }
        else if (urlOrJson.StartsWith("{"))
        {
            json = urlOrJson;
        }
        else
        {
            throw new ArgumentException("导入内容必须是 dpaste.com 短链接或阵容 JSON 字符串");
        }

        var lineup = JsonSerializer.Deserialize<Lineup>(json, JsonOptions)
                     ?? throw new InvalidOperationException("无法解析阵容 JSON");
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        return lineup;
    }
}
