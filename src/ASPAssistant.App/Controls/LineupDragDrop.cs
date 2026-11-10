using System.Windows;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Controls;

public enum LineupDragKind { None, Operator, Equipment }

public static class LineupDragDrop
{
    public const string OperatorFormat = "ASPAssistant.Lineup.Operator";
    public const string EquipmentFormat = "ASPAssistant.Lineup.Equipment";

    private static readonly DependencyPropertyKey ActiveKindPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "ActiveKind", typeof(LineupDragKind), typeof(LineupDragDrop),
        new FrameworkPropertyMetadata(LineupDragKind.None, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ActiveKindProperty = ActiveKindPropertyKey.DependencyProperty;

    public static LineupDragKind GetActiveKind(DependencyObject target) => (LineupDragKind)target.GetValue(ActiveKindProperty);

    internal static void RunWithHints(DependencyObject scope, LineupDragKind kind, Action drag)
    {
        scope.SetValue(ActiveKindPropertyKey, kind);
        try { drag(); }
        finally { scope.ClearValue(ActiveKindPropertyKey); }
    }

    public static bool Apply(LineupEditorViewModel vm, IDataObject data, LineupMemberViewModel? target = null, int? slot = null)
    {
        if (data.GetDataPresent(EquipmentFormat) && data.GetData(EquipmentFormat) is string equipment)
            return target is not null && vm.Equip(target, equipment, slot);
        if (slot is null && data.GetDataPresent(OperatorFormat) && data.GetData(OperatorFormat) is string name)
        {
            var op = vm.OperatorBrowser.FilteredOperators.FirstOrDefault(o => o.Name == name);
            if (op is null) return false;
            var previous = vm.Members.Count;
            vm.AddOperator(op);
            return vm.Members.Count > previous;
        }
        return false;
    }
}
