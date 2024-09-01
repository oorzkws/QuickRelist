using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace QuickRelist;

public unsafe class ItemSearchResultSubscriber : IDisposable {
    internal AddonItemSearchResult* ItemSearchResult;

    public unsafe ItemSearchResultSubscriber() {
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ItemSearchResult", OnSetup);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "ItemSearchResult", OnFinalize);
    }

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ItemSearchResult");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "ItemSearchResult");
    }

    internal void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        ItemSearchResult = (AddonItemSearchResult*)args.Addon;
        // YEET
        if (ItemSearchResult is not null && !KeyState[VirtualKey.SHIFT] && Condition.Any(ConditionFlag.OccupiedSummoningBell)) {
            Callback.Fire((AtkUnitBase*)ItemSearchResult, true, -1, 1);
        }
    }

    internal void OnFinalize(AddonEvent addonEvent, AddonArgs args) {
        ItemSearchResult = null;
    }
}