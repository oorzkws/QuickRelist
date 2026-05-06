using ECommons.DalamudServices;
using ECommons.UIHelpers.AtkReaderImplementations;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using QuickRelist.Extensions;

namespace QuickRelist;

public unsafe class ContextMenuSubscriber {
    private const int adjustPriceStringRow = 6948;
    private ExcelSheet<Addon>? addonStrings = Data.GetExcelSheet<Addon>();

    public ContextMenuSubscriber() => AddonLifecycle.RegisterListener(AddonEvent.PostShow, "ContextMenu", OnSetup);//QuickRelist.ContextMenu.OnMenuOpened += OnOpened;

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostShow, "ContextMenu");//QuickRelist.ContextMenu.OnMenuOpened -= OnOpened;
    }

    private void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        if (QuickRelist.SubscriberRetainerSellList.RetainerSellList is null) {
            // Not where we care about
            return;
        }
        // Manual override, or not at a bell
        if (KeyState[VirtualKey.SHIFT] || !Svc.Condition.Any(ConditionFlag.OccupiedSummoningBell)) {
            return;
        }
        // See if we have an entry named "Adjust Price" in local language
        var addon = (AtkUnitBase*)args.Addon.Address;
        var menuEntries = new ReaderContextMenu(addon).Entries;
        var adjustPriceString = addonStrings!.GetRow(adjustPriceStringRow)!.Text;
        for (var i = 0; i < menuEntries.Count; i++) {
            var entry = menuEntries[i];
            // I guess we doing bools now
            if (!entry.ValueType(0).EqualsAny(ValueType.String, ValueType.String8, ValueType.WideString, ValueType.ManagedString))
                continue;
            if (entry.Name != adjustPriceString)
                continue;
            Log.Verbose($"Clicking {entry.Name}");
            Callback.Fire(addon, true, 0, i, 0, 0, 0);
            return;
        }
    }
}