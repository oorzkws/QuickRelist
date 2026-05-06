using Dalamud.Game.Network.Internal.MarketBoardUploaders;
using Dalamud.Game.Network.Structures;
using Dalamud.Hooking;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using QuickRelist.Extensions;
using System.Collections.Concurrent;
using static ECommons.Throttlers.EzThrottler;
using SortedHistory = System.Collections.Generic.SortedSet<Dalamud.Game.Network.Structures.IMarketBoardHistoryListing>;
using SortedListings = System.Collections.Generic.SortedSet<Dalamud.Game.Network.Structures.IMarketBoardItemListing>;


namespace QuickRelist;

public class MarketSubscriber : IDisposable {
    public readonly HashSet<uint> CachedItems = new();
    public readonly ConcurrentDictionary<uint, SortedHistory> ItemSalesHistory = new();
    public readonly ConcurrentDictionary<uint, SortedListings> ItemCurrentOfferings = new();
    public readonly Queue<uint> RequestQueue = new();

    public uint ExpectedOfferingsParts { get; private set; }
    public uint ReceivedOfferingsParts { get; private set; }

    private TaskManager MarketTaskManager { get; set; }

    public unsafe uint LastRequestedItemId {
        get {
            var instance = InfoProxyItemSearch.Instance();
            return instance is null ? 0 : instance->SearchItemId;
        }
    }
    // Shorthand for resetting expected/received when toggling
    public bool IsBusy {
        get => ReceivedOfferingsParts < ExpectedOfferingsParts;
        private set {
            ReceivedOfferingsParts = 0;
            if (!value) {
                ExpectedOfferingsParts = 0;
            }
        }
    }

    public unsafe MarketSubscriber() {
        requestDataHook ??= Hook.HookFromAddress<InfoProxyItemSearch.Delegates.RequestData>((nint)InfoProxyItemSearch.StaticVirtualTablePointer->RequestData, RequestDataDetour);
        requestDataHook?.Enable();
        
        IMarketBoard.HistoryReceived += OnHistoryReceived;
        IMarketBoard.OfferingsReceived += OnOfferingsReceived;

        Condition.ConditionChange += OnConditionChange;

        // Dalamud/Game/Network/Internal/NetworkHandlers.cs
        itemRequestStartHook ??= Hook.HookFromAddress<PacketDispatcher.Delegates.HandleMarketBoardItemRequestStartPacket>(PacketDispatcher.Addresses.HandleMarketBoardItemRequestStartPacket.Value, MarketItemRequestStartDetour);
        itemRequestStartHook?.Enable();

        MarketTaskManager = new TaskManager();
    }

    public void Dispose() {
        IMarketBoard.HistoryReceived -= OnHistoryReceived;
        IMarketBoard.OfferingsReceived -= OnOfferingsReceived;

        requestDataHook?.Dispose();
        itemRequestStartHook?.Dispose();
    }

    // Intercepts the market request to get the last ID searched
    // RequestDataDetour -> MarketItemRequestStartDetour -> OnHistoryReceived -> OnOfferingsReceived
    private Hook<InfoProxyItemSearch.Delegates.RequestData>? requestDataHook;

    private unsafe bool RequestDataDetour(InfoProxyItemSearch* self) {
        // The client shouldn't send concurrent requests by default, as far as I know
        
        Log.Verbose($"Intercepting search for ItemId {LastRequestedItemId}");
        return requestDataHook!.Original(self);
    }
    
    public delegate void OnRequestFinishedDelegate(MarketSubscriber self, uint itemId);
    public event OnRequestFinishedDelegate OnRequestFinished = (self, itemId) => {
        self.ItemCurrentOfferings.TryAdd(itemId, new SortedListings(new ItemListingsByPrice()));
        self.ItemSalesHistory.TryAdd(itemId, new SortedHistory(new HistoryListingsByPrice()));
    };
    
    public delegate void OnRequestErroredDelegate(MarketSubscriber self, uint itemId, uint exceptionCode);
    public event OnRequestErroredDelegate OnRequestErrored = delegate(MarketSubscriber self, uint itemId, uint statusCode) {
        var errorHex = $"0x{statusCode:X8}";
        Log.Warning($"Server declined our market request, status code {errorHex}");
        self.IsBusy = false;
        // Place the request back at the top of the queue if it was a rate-limit error
        if (errorHex == "0x70000003")
            self.EnqueueRequest(itemId, true);
        else { // otherwise give up
            Log.Warning("Received non-ratelimit error, giving up");
        }
    };
    
    public delegate void OnRequestUpdatedDelegate(MarketSubscriber self, uint itemId, uint expectedPackets, uint receivedPackets);
    public event OnRequestUpdatedDelegate OnRequestUpdated = delegate (MarketSubscriber self, uint itemId, uint expectedPackets, uint receivedPackets) {
        // This will also trigger if there are 0 offerings and the history has arrived
        if (receivedPackets >= expectedPackets) {
            Log.Verbose($"Finished receiving {receivedPackets} packets of listings for ItemId {itemId}");
            self.CachedItems.Add(itemId);
            self.OnRequestFinished.Invoke(self, itemId); //, self.ItemCurrentOfferings[itemId], self.ItemSalesHistory[itemId]. Pain to use currently, may change later.
            self.IsBusy = false;
        }
    };
    
    public delegate void OnRequestStartedDelegate(MarketSubscriber self, uint itemId);
    public event OnRequestStartedDelegate OnRequestStarted = delegate(MarketSubscriber self, uint itemId) {
        self.ItemCurrentOfferings[itemId] = new SortedListings(new ItemListingsByPrice());
        self.ItemSalesHistory[itemId] = new SortedHistory(new HistoryListingsByPrice());
        self.CachedItems.Remove(itemId);
    };

    private void CacheItemHistory(uint itemId, IReadOnlyList<IMarketBoardHistoryListing> historyListings) {
        if (!ItemSalesHistory.ContainsKey(itemId))
            ItemSalesHistory[itemId] = new SortedHistory(new HistoryListingsByPrice());
        foreach (var listing in historyListings)
            ItemSalesHistory[itemId].Add(listing);
    }
    
    private void OnHistoryReceived(IMarketBoardHistory sales) {
        Log.Verbose($"Received History for {sales.ItemId}");
        CacheItemHistory(sales.ItemId, sales.HistoryListings);
        OnRequestUpdated.Invoke(this, sales.ItemId, ExpectedOfferingsParts, ReceivedOfferingsParts);
    }
    
    private void CacheItemOffering(uint itemId, IReadOnlyList<IMarketBoardItemListing> itemListings) {
        if (!ItemCurrentOfferings.ContainsKey(itemId))
            ItemCurrentOfferings[itemId] = new SortedListings(new ItemListingsByPrice());
        foreach (var listing in itemListings)
            ItemCurrentOfferings[itemId].Add(listing);
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings) {
        var itemId = LastRequestedItemId;
        Log.Verbose($"Received partial Offerings for {itemId}");
        CacheItemOffering(itemId, offerings.ItemListings);
        ReceivedOfferingsParts++;
        OnRequestUpdated.Invoke(this, itemId, ExpectedOfferingsParts, ReceivedOfferingsParts);
    }

    private void OnConditionChange(ConditionFlag newCondition, bool bActive) {
        if (newCondition != ConditionFlag.OccupiedSummoningBell)
            return;
        ItemSalesHistory.Clear();
        ItemCurrentOfferings.Clear();
        CachedItems.Clear();
    }

    // Added to FFXIVClientStructs for 7.1
    private readonly Hook<PacketDispatcher.Delegates.HandleMarketBoardItemRequestStartPacket>? itemRequestStartHook;
    private const float listingsPerPacket = 10f;

    private unsafe void MarketItemRequestStartDetour(uint targetId, IntPtr packetRef) {
        if (IsBusy) {
            Log.Warning("Market request started before previous request finished");
            IsBusy = false;
        }
        try {
            // Store the amount of packets we expect to receive
            var requestData = MarketBoardItemRequest.Read(packetRef);
            // Invoke the event
            var packetsToReceive = (uint)(requestData.AmountToArrive == 0 ? 0 : float.Ceiling(requestData.AmountToArrive / listingsPerPacket));
            ExpectedOfferingsParts = packetsToReceive;
            if (!requestData.Ok)
                OnRequestErrored.Invoke(this, LastRequestedItemId, requestData.Status);
            else {
                Log.Verbose($"Request made for {requestData.AmountToArrive} listings");
                OnRequestStarted.Invoke(this, targetId);
            }
        } catch (Exception e) {
            Log.Error(e, "Error in MarketItemRequestStartDetour");
            IsBusy = false;
        }
        itemRequestStartHook!.Original(targetId, packetRef);
    }

    private void EnsureQueueProcessorIsRunning() {
        if (MarketTaskManager.IsBusy) {
            return;
        }
        MarketTaskManager.Enqueue(StepQueue);
    }

    private unsafe bool StepQueue() {
        // Wait before continuing
        if (!Throttle("MarketSubscriber.StepQueue", 2000)) {
            return false;
        }
        // If still receiving the last request
        if (IsBusy) {
            return false;
        }
        // Queue empty?
        if (RequestQueue.Count == 0) {
            // No need to re-run
            return true;
        }
        // Get the ItemSearch Proxy
        var proxyInstance = InfoProxyItemSearch.Instance();
        if (proxyInstance == null)
            return false;
        // Clear the last search data and our cache then update the SearchItemId
        var itemId = RequestQueue.Peek();
        proxyInstance->EntryCount = 0; // Same as ClearData() according to ClientStructs
        proxyInstance->SearchItemId = itemId;
        // I've never seen this request return false, even on failure, but better safe than sorry
        var success = proxyInstance->RequestData();
        if (success) {
            RequestQueue.Dequeue();
        } else {
            Log.Warning("RequestData returned false!");
            return false;
        }
        // Continue processing queue if necessary
        if (RequestQueue.Count > 0) {
            MarketTaskManager.Enqueue(StepQueue);
        }
        return true;
    }

    internal void EnqueueRequest(uint itemId, bool top = false) {
        EnsureQueueProcessorIsRunning();
        if (!top) {
            RequestQueue.Enqueue(itemId);
        } else {
            var restOfQueue = RequestQueue.ToArray();
            RequestQueue.Clear();
            RequestQueue.Enqueue(itemId);
            restOfQueue.Each(i => RequestQueue.Enqueue(i));
        }
    }

}