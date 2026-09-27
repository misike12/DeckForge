using System.ComponentModel;

namespace DeckForge.Core.Plugins;

/// <summary>One checkable permission with its explanation.</summary>
/// <remarks>
/// This lives in Core rather than beside the Manifest page because it is a plain model with no
/// WPF dependency, and it is where the interesting behaviour is: the checkbox binds one way, so
/// the row has to be the thing that records a click. Getting that wrong is not visible in a
/// screenshot - it showed up as a counter that grew by one every time the page was opened.
/// </remarks>
public sealed class PermissionRow : INotifyPropertyChanged
{
    private Action<string, bool> _toggle;
    private bool _enabled;

    public PermissionRow(string name, bool enabled, Action<string, bool> toggle, bool isCustom = false)
    {
        Name = name;
        _enabled = enabled;
        _toggle = toggle;
        IsCustom = isCustom;
        Explanation = PermissionCatalog.ExplanationOf(name)
            ?? (isCustom
                ? "Custom permission - not in the known vocabulary. The host shows it to the user "
                  + "but ignores it (unknown-permission warning at validate)."
                : "");
    }

    public string Name { get; }
    public string Explanation { get; }
    public bool IsCustom { get; }

    public bool Enabled
    {
        get => _enabled;
        private set
        {
            if (_enabled != value)
            {
                _enabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Rebinds the row to the current manifest state without firing the toggle.</summary>
    public void Rebind(bool enabled, Action<string, bool> toggle)
    {
        _toggle = toggle;
        Enabled = enabled;
    }

    /// <summary>Called by the page's click handler after the user changes the checkbox.</summary>
    /// <remarks>
    /// The checkbox binds one-way, so the row's own state has to be updated here or the binding
    /// would snap back on the next re-render. That is also why the group counter used to be
    /// incremented by hand: nothing had told the row it had changed.
    /// </remarks>
    public void SetEnabled(bool value)
    {
        Enabled = value;
        _toggle(Name, value);
    }
}

/// <summary>One grouped permission section: title, summary, its rows, and how many are enabled.</summary>
public sealed class PermissionGroupVM : INotifyPropertyChanged
{
    private int _enabledCount;

    public PermissionGroupVM(PermissionGroup group)
    {
        Group = group;
        Title = PermissionCatalog.Title(group);
        Summary = PermissionCatalog.GroupSummary(group);
        Rows = [.. PermissionCatalog.OfGroup(group)
            .Select(p => new PermissionRow(p.Name, false, (_, _) => { }))];
    }

    public PermissionGroup Group { get; }
    public string Title { get; }
    public string Summary { get; }

    public IReadOnlyList<PermissionRow> Rows { get; } = [];

    public int EnabledCount
    {
        get => _enabledCount;
        private set
        {
            if (_enabledCount != value)
            {
                _enabledCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnabledCount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountLabel)));
            }
        }
    }

    public string CountLabel => EnabledCount == 0 ? "" : $"({EnabledCount} enabled)";

    public string GroupBrushKey => Group switch
    {
        PermissionGroup.Host => "Liquid.AccentBrush",
        PermissionGroup.Publishing => "Liquid.SuccessBrush",
        _ => "Liquid.WarningBrush",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Rebuilds the checked state of every row against the manifest's permission set.</summary>
    public void Refresh(IReadOnlySet<string> enabled, Action<string, bool> toggle)
    {
        foreach (var row in Rows)
        {
            row.Rebind(enabled.Contains(row.Name), toggle);
        }

        Recount();
    }

    /// <summary>
    /// Recomputes the counter from the rows.
    /// </summary>
    /// <remarks>
    /// It used to do <c>EnabledCount += 1</c> from a toggle callback, which drifted the moment
    /// anything else changed a row - a reload, an undo, or the same row firing twice. A count read
    /// from the thing it counts cannot drift.
    /// </remarks>
    public void Recount() => EnabledCount = Rows.Count(r => r.Enabled);
}
