using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace QuickRelist;

public unsafe class AgentRetainerEventSubscriber : IDisposable {
    public event EventHandler<ReceiveEventArgs>? ReceiveEvent;
    internal delegate void* AgentReceiveEvent(AgentInterface* agent, void* rawData, AtkValue* eventArgs, uint eventArgsCount, ulong sender);
    internal static Hook<AgentReceiveEvent>? receiveEventHook;

    public AgentRetainerEventSubscriber() {
        var retainerAgentInterface = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
        receiveEventHook ??= Hook.HookFromAddress<AgentReceiveEvent>(new IntPtr(retainerAgentInterface->VirtualTable->ReceiveEvent), OnReceiveEvent);
        receiveEventHook?.Enable();
    }


    public void Dispose() {
        receiveEventHook?.Dispose();
    }

    private void* OnReceiveEvent(AgentInterface* agent, void* rawData, AtkValue* eventArgs, uint eventArgsCount, ulong sender) {
        try {
            var args = new ReceiveEventArgs(agent, rawData, eventArgs, eventArgsCount, sender);
            //args.PrintData();
            ReceiveEvent!.Invoke(this, args);
        } catch (Exception ex) {
            Log.Error(ex, "Something went wrong when re-invoking AgentRetainer");
        }

        return receiveEventHook!.Original(agent, rawData, eventArgs, eventArgsCount, sender);
    }

    public class ReceiveEventArgs(AgentInterface* agentInterface, void* rawData, AtkValue* eventArgs, uint eventArgsCount, ulong senderId)
        : EventArgs {

        public AgentInterface* AgentInterface = agentInterface;
        public void* RawData = rawData;
        public AtkValue* EventArgs = eventArgs;
        public uint EventArgsCount = eventArgsCount;
        public ulong SenderID = senderId;

        public void PrintData() {
            Log.Verbose("ReceiveEvent Argument Printout --------------");
            Log.Verbose($"AgentInterface: {(IntPtr)AgentInterface:X8}");
            Log.Verbose($"RawData: {(IntPtr)RawData:X8}");
            Log.Verbose($"EventArgs: {(IntPtr)EventArgs:X8}");
            Log.Verbose($"EventArgsCount: {EventArgsCount}");
            Log.Verbose($"SenderID: {SenderID}");

            for (var i = 0; i < EventArgsCount; i++) {
                Log.Verbose($"[{i}] {EventArgs[i].Int}, {EventArgs[i].Type}");
            }

            Log.Verbose("End -----------------------------------------");
        }
    }
}