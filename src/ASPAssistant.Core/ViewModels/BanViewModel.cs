namespace ASPAssistant.Core.ViewModels;

public class BanViewModel
{
    private readonly HashSet<string> _bannedNames = [];

    public event Action? BansChanged;

    public bool IsBanned(string name) => _bannedNames.Contains(name);

    /// <summary>
    /// Replaces the current ban list with the provided names.
    /// Used both on initial load and when a new match ban screen is detected
    /// (auto-clears the previous run's bans before applying the new set).
    /// </summary>
    public void SetBans(IEnumerable<string> names)
    {
        _bannedNames.Clear();
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name))
                _bannedNames.Add(name);
        BansChanged?.Invoke();
    }

    public void ToggleBan(string name)
    {
        if (!_bannedNames.Remove(name))
            _bannedNames.Add(name);
        BansChanged?.Invoke();
    }

    public void ClearBans()
    {
        if (_bannedNames.Count == 0) return;
        _bannedNames.Clear();
        BansChanged?.Invoke();
    }
}
