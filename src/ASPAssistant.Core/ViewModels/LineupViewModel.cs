using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>
/// 已保存阵容列表 + 增删改 + 激活盟约统计访问。
/// </summary>
public partial class LineupViewModel : ObservableObject
{
    private readonly LineupStore _store;
    public CovenantCalculator Calculator { get; }

    [ObservableProperty]
    private ObservableCollection<Lineup> _lineups = [];

    public LineupViewModel(LineupStore store, CovenantCalculator calculator)
    {
        _store = store;
        Calculator = calculator;
    }

    public void LoadLineups(IEnumerable<Lineup> lineups)
    {
        Lineups = new ObservableCollection<Lineup>(
            lineups.OrderByDescending(l => l.UpdatedAt));
    }

    /// <summary>新增或更新阵容，并落盘。</summary>
    public async Task AddOrUpdateAsync(Lineup lineup)
    {
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        await _store.SaveAsync(lineup);

        var existing = Lineups.FirstOrDefault(l => l.Id == lineup.Id);
        if (existing is not null)
        {
            int idx = Lineups.IndexOf(existing);
            Lineups[idx] = lineup;
        }
        else
        {
            Lineups.Insert(0, lineup);
        }
    }

    public async Task DeleteAsync(Lineup lineup)
    {
        await _store.DeleteAsync(lineup.Id);
        Lineups.Remove(lineup);
    }

    public int CountActivated(Lineup lineup) => Calculator.CountActivated(lineup);

    public IReadOnlyList<CovenantStat> ComputeStats(Lineup lineup) => Calculator.Compute(lineup);
}
