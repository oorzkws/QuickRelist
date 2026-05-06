using FFXIVClientStructs.FFXIV.Client.UI;

namespace QuickRelist;

public unsafe class ItemSearchResultSubscriber : IDisposable {

    public ItemSearchResultSubscriber() {
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemSearchResult", OnSetup);
    }

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ItemSearchResult");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "ItemSearchResult");
    }

    internal void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        var itemSearchResult = (AddonItemSearchResult*)args.Addon.Address;
        // YEET
        if (itemSearchResult is not null && !KeyState[VirtualKey.SHIFT] && Condition.Any(ConditionFlag.OccupiedSummoningBell)) {
            Callback.Fire((AtkUnitBase*)itemSearchResult, true, -1, 1);
        }
    }

}