using FFXIVClientStructs.FFXIV.Client.UI;

namespace QuickRelist;

public unsafe class ItemSearchResultSubscriber : IDisposable {
    internal AddonItemSearchResult* ItemSearchResult;

    public ItemSearchResultSubscriber() {
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemSearchResult", OnSetup);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ItemSearchResult", OnFinalize);
    }

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ItemSearchResult");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "ItemSearchResult");
    }

    internal void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        ItemSearchResult = (AddonItemSearchResult*)args.Addon.Address;
        // YEET
        if (ItemSearchResult is not null && !KeyState[VirtualKey.SHIFT] && Condition.Any(ConditionFlag.OccupiedSummoningBell)) {
            Callback.Fire((AtkUnitBase*)ItemSearchResult, true, -1, 1);
        }
    }

    internal void OnFinalize(AddonEvent addonEvent, AddonArgs args) {
        ItemSearchResult = null;
    }
}