using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Runtime.InteropServices;


namespace QuickRelist;

public unsafe class RetainerSellListSubscriber : IDisposable {
    internal bool Adjusting;
    internal int ListStep;

    public RetainerSellListSubscriber() {
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSellList", OnSetup);
        // Lets us figure out the last-clicked index
        SubscriberAgentRetainerEvent.ReceiveEvent += delegate (object? _, AgentRetainerEventSubscriber.ReceiveEventArgs args) {
            if (args.SenderID != 3ul || args.EventArgsCount != 3)
                return;
            ListStep = args.EventArgs[1].Int + 1;
            Log.Verbose($"Clicked RetainerSellList item # {ListStep - 1}");
        };
    }

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSellList");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSellList");
    }

    internal void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        var retainerSellList = (AtkUnitBase*)args.Addon.Address;
        ListStep = 0;
        if (retainerSellList is not null && KeyState[VirtualKey.CONTROL]) {
            Adjusting = true;
            SubscriberRetainerSellList.AdjustNext();
        }
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
        ClickItem(ret, ListStep);
        if (ListStep >= saleCount) {
            Log.Verbose($"Finished adjustment");
            Adjusting = false;
        }
    }
}