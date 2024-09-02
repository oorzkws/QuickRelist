using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Gui.ContextMenu;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;

namespace QuickRelist;

public unsafe class ContextMenuSubscriber {
    private const int adjustPriceStringRow = 6948;
    private ExcelSheet<Addon>? addonStrings = Data.GetExcelSheet<Addon>();

    public ContextMenuSubscriber() => QuickRelist.ContextMenu.OnMenuOpened += OnOpened;

    public void Dispose() {
        QuickRelist.ContextMenu.OnMenuOpened -= OnOpened;
    }

    private void OnOpened(IMenuOpenedArgs args) {
        if (QuickRelist.SubscriberRetainerSellList.RetainerSellList is null) {
            // Not where we care about
            return;
        }
        // Manual override, or not at a bell
        if (KeyState[VirtualKey.SHIFT] || !Svc.Condition.Any(ConditionFlag.OccupiedSummoningBell)) {
            return;
        }
        //if (!EzThrottler.Throttle("ContextMenu.OnOpened", 100)) {
        //    return;
        //}

        // See if we have an entry named "Adjust Price" in local language
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("ContextMenu", out var addon)) {
            var menuEntries = new ReaderContextMenu(addon).Entries;
            var adjustPriceString = addonStrings!.GetRow(adjustPriceStringRow)!.Text;
            for (var i = 0; i < menuEntries.Count; i++) {
                var entry = menuEntries[i];
                if (entry.Name != adjustPriceString)
                    continue;
                Callback.Fire(addon, true, 0, i, 0, 0, 0);
                break;
            }
        }
    }
}