using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

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

    public Task<bool> SaveAsync(Lineup lineup) => ChangeAsync(lineup, delete: false);
    public Task<bool> DeleteAsync(Lineup lineup) => ChangeAsync(lineup, delete: true);

    private async Task<bool> ChangeAsync(Lineup lineup, bool delete)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_loaded) { Status = "本地阵容尚未成功读取，暂不能保存或删除，以免覆盖原文件。"; return false; }
            var next = _lineups.ToList();
            var index = next.FindIndex(x => x.Id == lineup.Id);
            if (delete) next.RemoveAll(x => x.Id == lineup.Id);
            else if (index >= 0) next[index] = lineup;
            else next.Add(lineup);
            await _store.SaveAsync(next);
            _lineups.Clear();
            foreach (var item in next) _lineups.Add(item);
            Status = delete ? "阵容已删除。" : "阵容已保存。";
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
