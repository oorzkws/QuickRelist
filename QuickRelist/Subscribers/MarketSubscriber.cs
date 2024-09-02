using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Network.Internal;
using Dalamud.Game.Network.Internal.MarketBoardUploaders;
using Dalamud.Game.Network.Structures;
using Dalamud.Hooking;
using ECommons;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using QuickRelist.Extensions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using static ECommons.Throttlers.EzThrottler;
using static QuickRelist.QuickRelist;

namespace QuickRelist;

public class MarketSubscriber : IDisposable {
    public readonly HashSet<uint> CachedItems = new();
    public readonly ConcurrentDictionary<uint, SortedSet<IMarketBoardHistoryListing>> ItemSalesHistory = new();
    public readonly ConcurrentDictionary<uint, SortedSet<IMarketBoardItemListing>> ItemCurrentOfferings = new();
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

        MarketBoard.HistoryReceived += OnHistoryReceived;
        MarketBoard.OfferingsReceived += OnOfferingsReceived;

        Condition.ConditionChange += OnConditionChange;

        // Dalamud/Game/Network/Internal/NetworkHandlers.cs
        var addressResolver = new NetworkHandlersAddressResolver();
        addressResolver.Setup(SigScanner);
        itemRequestStartHook ??= Hook.HookFromAddress<MarketBoardItemRequestStartPacketHandler>(addressResolver.MarketBoardItemRequestStartPacketHandler, MarketItemRequestStartDetour);
        itemRequestStartHook?.Enable();

        MarketTaskManager = new TaskManager();
    }

    public void Dispose() {
        MarketBoard.HistoryReceived -= OnHistoryReceived;
        MarketBoard.OfferingsReceived -= OnOfferingsReceived;

        requestDataHook?.Dispose();
        itemRequestStartHook?.Dispose();
    }

    // Intercepts the market request to get the last ID searched
    // RequestDataDetour -> MarketItemRequestStartDetour -> OnHistoryReceived -> OnOfferingsReceived
    private Hook<InfoProxyItemSearch.Delegates.RequestData>? requestDataHook;

    private unsafe bool RequestDataDetour(InfoProxyItemSearch* self) {
        // The client shouldn't send concurrent requests by default, as far as I know

        // Market Search: 208, 104, 48 || 208 104 48 || 192, 88, 32. Seems to change every session.
        Log.Verbose($"Intercepting search for ItemId {LastRequestedItemId}, special bytes: {(byte)(self + 0x24)}, {(byte)(self + 0x25)}, {(byte)(self + 0x28)}");
        return requestDataHook!.Original(self);
    }

    private void OnHistoryReceived(IMarketBoardHistory sales) {
        var itemId = LastRequestedItemId;
        Log.Verbose($"Received History for {itemId}");
        if (ItemSalesHistory.TryGetValue(itemId, out var cachedHistory)) {
            sales.HistoryListings.Each(listing => {
                cachedHistory.Add(listing);
            });
        } else {
            Log.Warning("Received MB sales history data with no cache structure to put it in");
        }
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings) {
        var itemId = LastRequestedItemId;
        if (ItemCurrentOfferings.TryGetValue(itemId, out var cachedListings)) {
            offerings.ItemListings.Each(listing => {
                cachedListings.Add(listing);
            });
        } else {
            Log.Warning("Received MB sales offering data with no cache structure to put it in");
        }
        // Handle if we're finished a multi-packet request
        if (++ReceivedOfferingsParts >= ExpectedOfferingsParts) {
            Log.Verbose($"Finished receiving {ExpectedOfferingsParts} packets of listings for ItemId {itemId}");
            // Flag as cached
            CachedItems.Add(itemId);
            IsBusy = false;
        }
    }

    private void OnConditionChange(ConditionFlag newCondition, bool bActive) {
        if (newCondition != ConditionFlag.OccupiedSummoningBell)
            return;
        ItemSalesHistory.Clear();
        ItemCurrentOfferings.Clear();
        CachedItems.Clear();
    }

    // We're basically just using Dalamud's hook system
    private readonly Hook<MarketBoardItemRequestStartPacketHandler>? itemRequestStartHook;
    private delegate nint MarketBoardItemRequestStartPacketHandler(nint a1, nint packetRef);
    private const float listingsPerPacket = 10f;

    private nint MarketItemRequestStartDetour(nint a1, nint packetRef) {
        try {
            // Store the amount of packets we expect to receive
            var requestData = MarketBoardItemRequest.Read(packetRef);
            // Are we already busy?
            if (IsBusy) {
                Log.Warning("Market request started before previous request finished");
            }
            if (!requestData.Ok) {
                // Rate-limit: 0x70000003
                Log.Warning($"Server declined our market request, status code 0x{requestData.Status:X8}");
                // Place the request back at the top of the queue
                EnqueueRequest(LastRequestedItemId, true);
            } else {
                Log.Verbose($"Request made for {requestData.AmountToArrive} listings");
                ExpectedOfferingsParts = (uint)float.Ceiling(requestData.AmountToArrive / listingsPerPacket);
            }
        } catch (Exception e) {
            Log.Error(e, "Error in MarketItemRequestStartDetour");
            IsBusy = false;
        }
        return itemRequestStartHook!.OriginalDisposeSafe(a1, packetRef);
    }

    private void EnsureQueueProcessorIsRunning() {
        if (MarketTaskManager.IsBusy) {
            return;
        }
        MarketTaskManager.Enqueue(StepQueue);
    }

    private unsafe bool StepQueue() {
        // Wait before continuing
        if (!Throttle("RetainerSellSubscriber.OnSetup", 2500)) {
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
        proxyInstance->ClearData();
        ItemSalesHistory[itemId] = new SortedSet<IMarketBoardHistoryListing>(new HistoryListingsByPrice());
        ItemCurrentOfferings[itemId] = new SortedSet<IMarketBoardItemListing>(new ItemListingsByPrice());
        CachedItems.Remove(itemId);
        proxyInstance->SearchItemId = itemId;
        // I've never seen this request return false, even on failure, but better safe than sorry
        var success = proxyInstance->RequestData();
        if (success) {
            RequestQueue.Dequeue();
        } else {
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