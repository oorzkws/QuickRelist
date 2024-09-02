using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace QuickRelist;

public unsafe class RetainerSellListSubscriber : IDisposable {
    internal AtkUnitBase* RetainerSellList;
    internal int ListStep = 1;

    public RetainerSellListSubscriber() {
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSellList", OnSetup);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSellList", OnFinalize);
    }

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSellList");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSellList");
    }

    internal void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        RetainerSellList = (AtkUnitBase*)args.Addon;
    }

    internal void OnFinalize(AddonEvent addonEvent, AddonArgs args) {
        RetainerSellList = null;
    }

    internal static AtkValue* EventArgArray(params int[] args) {
        var atkValues = (AtkValue*)Marshal.AllocHGlobal(args.Length * sizeof(AtkValue));
        for (var i = 0; i < args.Length; i++) {
            atkValues[i].Type = ValueType.Int;
            atkValues[i].Int = args[i];
        }
        return atkValues;
    }


    private void ClickItem(AgentInterface* agentInterface, int index) {
        var eventId = 3ul;
        var eventArgs = new[] {
            0,
            index,
            1,
        };
        var atkEventArgs = EventArgArray(eventArgs);
        var returnObject = (AtkValue*)Marshal.AllocHGlobal(sizeof(AtkValue));
        try {
            agentInterface->ReceiveEvent(returnObject, atkEventArgs, (uint)eventArgs.Length, eventId);
        } catch {
            // ignored
        } finally {
            Marshal.FreeHGlobal(new nint(atkEventArgs));
            Marshal.FreeHGlobal(new nint(returnObject));
        }
    }

    internal void AdjustNext() {
        Log.Verbose($"Adjusting item {ListStep}");
        var ret = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
        var saleCount = RetainerManager.Instance()->GetActiveRetainer()->MarketItemCount;
        if (ListStep >= saleCount) {
            ListStep = 1;
            return;
        }
        ClickItem(ret, ListStep);
        ListStep++;
    }
}