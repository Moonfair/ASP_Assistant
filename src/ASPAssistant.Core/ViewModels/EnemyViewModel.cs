using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ASPAssistant.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ASPAssistant.Core.ViewModels;

public partial class EnemyViewModel : ObservableObject
{
    // ── OCR fields ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _bossName = "";

    [ObservableProperty]
    private ObservableCollection<string> _enemyTypes = [];

    // ── Boss detail fields (populated from BossDataService) ───────────────────

    /// <summary>
    /// Relative icon path (e.g. <c>icons/bosses/enemy_9006_actoxi.png</c>),
    /// resolved at view layer via <c>AppContext.BaseDirectory + "data"</c>.
    /// <c>null</c> when no boss info is available.
    /// </summary>
    [ObservableProperty]
    private string? _avatarPath;

    /// <summary>
    /// <c>true</c> when <see cref="AvatarPath"/> points to an existing file under <c>data/</c>.
    /// Drives portrait vs placeholder when JSON path is set but PNG was never shipped.
    /// </summary>
    [ObservableProperty]
    private bool _bossAvatarImageAvailable;

    /// <summary>Display index from <c>bosses.json</c>, e.g. <c>SP01</c>.</summary>
    [ObservableProperty]
    private string _bossIndex = "";

    /// <summary>Short Chinese summary of <see cref="BossInfo.DamageTypes"/>.</summary>
    [ObservableProperty]
    private string _bossDamageSummary = "";

    [ObservableProperty]
    private string _bossDescription = "";

    [ObservableProperty]
    private ObservableCollection<string> _bossAbilities = [];

    // ── Enemy type detail cards (populated from EnemyTypeDataService) ──────────

    /// <summary>
    /// Enriched data for each OCR-detected special-enemy type.
    /// Bound to the type-card list in the view.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<EnemyTypeEntry> _enemyTypeDetails = [];

    // ── Public methods ────────────────────────────────────────────────────────

    /// <summary>Clears all enemy info before a new recognition run.</summary>
    public void Clear()
    {
        BossName           = "";
        AvatarPath         = null;
        BossIndex          = "";
        BossDamageSummary  = "";
        BossDescription    = "";
        EnemyTypes.Clear();
        BossAbilities.Clear();
        EnemyTypeDetails.Clear();
    }

    /// <summary>
    /// Populates the view model from a completed recognition result.
    /// Both <paramref name="bossInfo"/> and <paramref name="typeEntries"/> may be
    /// <c>null</c> when the backing services have not finished loading; in that
    /// case only the raw OCR text fields are populated.
    /// </summary>
    public void Update(
        EnemyResult result,
        BossInfo? bossInfo = null,
        IEnumerable<EnemyTypeEntry?>? typeEntries = null)
    {
        BossName = result.BossName;

        EnemyTypes.Clear();
        foreach (var t in result.EnemyTypes)
            EnemyTypes.Add(t);

        // Boss details
        BossDescription = bossInfo?.Description ?? "";
        BossIndex       = bossInfo?.EnemyIndex ?? "";
        BossDamageSummary = FormatDamageTypes(bossInfo?.DamageTypes);
        BossAbilities.Clear();
        if (bossInfo is not null)
            foreach (var a in bossInfo.AbilityList)
                BossAbilities.Add(a);
        AvatarPath = string.IsNullOrWhiteSpace(bossInfo?.IconPath)
            ? null
            : bossInfo!.IconPath.Trim();

        // Enemy type cards
        EnemyTypeDetails.Clear();
        if (typeEntries is not null)
            foreach (var e in typeEntries)
                if (e is not null)
                    EnemyTypeDetails.Add(e);
    }

    private static string FormatDamageTypes(IReadOnlyList<string>? types)
    {
        if (types is null || types.Count == 0)
            return "";

        static string Map(string code) => code.ToUpperInvariant() switch
        {
            "MAGIC"   => "法术",
            "PHYSIC"  => "物理",
            "TRUE"    => "真实",
            _         => code
        };

        return string.Join(" · ", types.Distinct(StringComparer.OrdinalIgnoreCase).Select(Map));
    }

    partial void OnAvatarPathChanged(string? value) =>
        BossAvatarImageAvailable = IsBossAvatarFilePresent(value);

    private static bool IsBossAvatarFilePresent(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var trimmed = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
        var full    = Path.Combine(AppContext.BaseDirectory, "data", trimmed);
        return File.Exists(full);
    }
}
