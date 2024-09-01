using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace QuickRelist;

public unsafe class RetainerSellListSubscriber : IDisposable {
    internal AtkUnitBase* RetainerSellList;
    internal int step = 1;
    
    public unsafe RetainerSellListSubscriber() {
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
        for (int i = 0; i < args.Length; i++) {
            atkValues[i].Type = ValueType.Int;
            atkValues[i].Int = args[i];
        }
        return atkValues;
    }
    

    unsafe void ClickItem(AgentInterface* agentInterface, int index) {
        var eventKind = 3;
        var unprocessedArgs = new int[] { 0, index, 1 };
        var atkValues = (AtkValue*)Marshal.AllocHGlobal(unprocessedArgs.Length * sizeof(AtkValue));
        var x = EventArgArray(unprocessedArgs);
        var eventObject = (AtkValue*)Marshal.AllocHGlobal(sizeof(AtkValue));
        try {
            for (int i = 0; i < unprocessedArgs.Length; i++) {
                agentInterface->ReceiveEvent(eventObject, x, (uint)unprocessedArgs.Length, 3);
            }
        }catch(Exception e)
        {
                
        }finally
        {
            Marshal.FreeHGlobal(new nint(atkValues));
        }
            
        var vals = new AtkValue() {
            Type = ValueType.Int,
            Int = 0
        };
    }

    internal void AdjustNext() {
        Log.Verbose($"Adjusting item {step}");
        var ret = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
        var saleCount = RetainerManager.Instance()->GetActiveRetainer()->MarketItemCount;
        if (step >= saleCount) {
            step = 1;
            return;
        }
        ClickItem(ret, step);
        step++;
    }
}