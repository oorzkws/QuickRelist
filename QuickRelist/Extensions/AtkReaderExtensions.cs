using Dalamud.Game.NativeWrapper;
using ECommons.UIHelpers.AtkReaderImplementations;

namespace QuickRelist.Extensions;

public static class ContextMenuEntryExtensions {
    // Reimplemented from AtkReader in ECommons
    public static unsafe ValueType ValueType(this ReaderContextMenu.ContextMenuEntry entry, int n) {
        var (unitBasePtr, beginOffset) = entry.AtkReaderParams;
        var unitBase = (AtkUnitBase*)unitBasePtr;
        var num = entry.AtkReaderParams.BeginOffset + n;
        if (num >= unitBase->AtkValuesCount)
            return FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Null;
        return unitBase->AtkValues[num].Type;
    }
}