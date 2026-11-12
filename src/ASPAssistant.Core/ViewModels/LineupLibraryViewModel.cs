using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public enum LineupMove { Up, Down, Top }

public partial class LineupLibraryViewModel : ObservableObject
{
    private readonly LineupStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    private readonly ObservableCollection<Lineup> _lineups = [];
    public ReadOnlyObservableCollection<Lineup> Lineups { get; }
    [ObservableProperty] private string _status = "";

    public LineupLibraryViewModel(LineupStore store)
    {
        _store = store;
        Lineups = new(_lineups);
    }

    public async Task LoadAsync()
    {
        try
        {
            var lineups = await _store.LoadAsync();
            _lineups.Clear();
            foreach (var lineup in lineups) _lineups.Add(lineup);
            _loaded = true;
            Status = "";
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            _loaded = false;
            Status = "无法读取本地阵容，原文件未修改。请检查 lineups.json 后重启：" + ex.Message;
            AppLogger.Error("Lineups", "Could not load lineups", ex);
        }
    }

    public Task<bool> SaveAsync(Lineup lineup) => SaveOrDeleteAsync(lineup, delete: false);
    public Task<bool> DeleteAsync(Lineup lineup) => SaveOrDeleteAsync(lineup, delete: true);

    public Task<bool> MoveAsync(Guid lineupId, LineupMove move, bool? isImported = null) => PersistChangeAsync(next =>
    {
        bool MatchesCategory(Lineup item) => isImported is null || item.IsImported == isImported;
        var index = next.FindIndex(item => item.Id == lineupId && MatchesCategory(item));
        if (index < 0) return null;

        var destination = move switch
        {
            LineupMove.Up when index > 0 => next.FindLastIndex(index - 1, MatchesCategory),
            LineupMove.Down => next.FindIndex(index + 1, MatchesCategory),
            LineupMove.Top => next.FindIndex(MatchesCategory),
            _ => -1
        };
        if (destination < 0 || destination == index) return null;

        var lineup = next[index];
        next.RemoveAt(index);
        next.Insert(destination, lineup);
        return () => _lineups.Move(index, destination);
    }, "阵容顺序已保存。");

    private Task<bool> SaveOrDeleteAsync(Lineup lineup, bool delete) => PersistChangeAsync(next =>
    {
        var index = next.FindIndex(x => x.Id == lineup.Id);
        if (delete) next.RemoveAll(x => x.Id == lineup.Id);
        else if (index >= 0) next[index] = lineup;
        else next.Add(lineup);

        return () =>
        {
            _lineups.Clear();
            foreach (var item in next) _lineups.Add(item);
        };
    }, delete ? "阵容已删除。" : "阵容已保存。");

    private async Task<bool> PersistChangeAsync(Func<List<Lineup>, Action?> prepareChange, string successStatus)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_loaded) { Status = "本地阵容尚未成功读取，暂不能修改，以免覆盖原文件。"; return false; }
            var next = _lineups.ToList();
            var applyChange = prepareChange(next);
            if (applyChange is null) return false;
            await _store.SaveAsync(next);
            applyChange();
            Status = successStatus;
            return true;
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            Status = "阵容未保存，请检查目录权限或磁盘空间后重试：" + ex.Message;
            AppLogger.Error("Lineups", "Could not save lineups", ex);
            return false;
        }
        finally { _gate.Release(); }
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or FormatException;
}
