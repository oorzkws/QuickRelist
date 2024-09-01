using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Network.Internal;
using Dalamud.Game.Network.Structures;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System.Collections.Concurrent;
using Dalamud.Game.Network.Internal.MarketBoardUploaders;
using ECommons;
using ECommons.Automation.NeoTaskManager;
using QuickRelist.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace QuickRelist;

public class MarketSubscriber : IDisposable {
    public readonly ConcurrentDictionary<uint, SortedSet<IMarketBoardHistoryListing>> ItemSalesHistory = new();
    public readonly ConcurrentDictionary<uint, SortedSet<IMarketBoardItemListing>> ItemCurrentOfferings = new();

    private TaskManager MarketTaskManager { get; set; }

    public uint LastRequestedItemId { get; private set; } = 0;
    public uint ExpectedOfferingsParts { get; private set; } = 0;
    public uint ReceivedOfferingsParts { get; private set; } = 0;
    private bool isBusy = false;
    // Shorthand for resetting expected/received when toggling
    public bool IsBusy {
        get => isBusy;
        private set {
            ReceivedOfferingsParts = 0;
            if (!value) {
                ExpectedOfferingsParts = 0;
            }
            isBusy = value;
        }
    }

    public unsafe MarketSubscriber() {
        requestDataHook ??= Hook.HookFromAddress<InfoProxyItemSearch.Delegates.RequestData>((nint)InfoProxyItemSearch.StaticVirtualTablePointer->RequestData, RequestDataDetour);
        requestDataHook?.Enable();

        QuickRelist.MarketBoard.HistoryReceived += OnHistoryReceived;
        QuickRelist.MarketBoard.OfferingsReceived += OnOfferingsReceived;

        Condition.ConditionChange += OnConditionChange;

        // Dalamud/Game/Network/Internal/NetworkHandlers.cs
        var addressResolver = new NetworkHandlersAddressResolver();
        addressResolver.Setup(SigScanner);
        itemRequestStartHook ??= Hook.HookFromAddress<MarketBoardItemRequestStartPacketHandler>(addressResolver.MarketBoardItemRequestStartPacketHandler, MarketItemRequestStartDetour);
        itemRequestStartHook?.Enable();

        MarketTaskManager = new TaskManager();
    }

    public void Dispose() {
        QuickRelist.MarketBoard.HistoryReceived -= OnHistoryReceived;
        QuickRelist.MarketBoard.OfferingsReceived -= OnOfferingsReceived;

        requestDataHook?.Dispose();
        itemRequestStartHook?.Dispose();
    }

    private void OnHistoryReceived(IMarketBoardHistory sales) {
        Log.Verbose($"Received History for {LastRequestedItemId}");
        if (ItemSalesHistory.TryGetValue(LastRequestedItemId, out var cachedHistory)) {
            sales.HistoryListings.Each(listing => {
                ItemSalesHistory[LastRequestedItemId].Add(listing);
            });
        } else {
            Log.Warning("Received MB sales history data with no cache structure to put it in");
        }
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings) {
        // Are we done?
        if (++ReceivedOfferingsParts >= ExpectedOfferingsParts) {
            Log.Verbose($"Finished receiving {ExpectedOfferingsParts} packets of listings for ItemId {LastRequestedItemId}");
            IsBusy = false;
        }

        if (offerings.ItemListings.Count == 0) {
            return;
        }
        var itemId = offerings.ItemListings.First().ItemId;
        if (ItemCurrentOfferings.TryGetValue(itemId, out var cachedListings)) {
            offerings.ItemListings.Each(listing => {
                ItemCurrentOfferings[itemId].Add(listing);
            });
        } else {
            Log.Warning("Received MB sales offering data with no cache structure to put it in");
        }
    }

    private void OnConditionChange(ConditionFlag newCondition, bool bActive) {
        if (newCondition != ConditionFlag.OccupiedSummoningBell)
            return;
        ItemSalesHistory.Clear();
        ItemCurrentOfferings.Clear();
    }

    private Hook<InfoProxyItemSearch.Delegates.RequestData>? requestDataHook;

    // Intercepts the market request to get the last ID searched
    private unsafe bool RequestDataDetour(InfoProxyItemSearch* self) {
        // The client shouldn't send concurrent requests by default, as far as I know
        if (IsBusy) {
            Log.Warning("Market request started before previous request finished");
        } else {
            IsBusy = true;
        }
        LastRequestedItemId = self->SearchItemId;
        // Clear the cache in anticipation of new data
        ItemSalesHistory[LastRequestedItemId] = new SortedSet<IMarketBoardHistoryListing>(new HistoryListingsByPrice());
        ItemCurrentOfferings[LastRequestedItemId] = new SortedSet<IMarketBoardItemListing>(new ItemListingsByPrice());
        // Market Search: 208, 104, 48 || 208 104 48 || 192, 88, 32. Seems to change every session.
        Log.Verbose($"Intercepting search for ItemId {LastRequestedItemId}, special bytes: {(byte)(self + 0x24)}, {(byte)(self + 0x25)}, {(byte)(self + 0x28)}");
        return requestDataHook!.Original(self);
    }

    // Triggers a marketboard sales listing request for the itemid
    internal unsafe bool RequestDataForItem(uint itemId) {
        var proxyInstance = InfoProxyItemSearch.Instance();
        if (proxyInstance == null)
            return false;

        proxyInstance->ClearData();
        proxyInstance->SearchItemId = itemId;
        return proxyInstance->RequestData();
    }

    // We're basically just using Dalamud's hook system
    private readonly Hook<MarketBoardItemRequestStartPacketHandler>? itemRequestStartHook;
    private delegate nint MarketBoardItemRequestStartPacketHandler(nint a1, nint packetRef);
    private const float listingsPerPacket = 10f;

    private nint MarketItemRequestStartDetour(nint a1, nint packetRef) {
        try {
            // Store the amount of packets we expect to receive
            var requestData = MarketBoardItemRequest.Read(packetRef);
            Log.Verbose($"Request made for {requestData.AmountToArrive} listings");
            if (requestData.AmountToArrive == 0 || !requestData.Ok) {
                IsBusy = false;
                var itemId = LastRequestedItemId;
                Log.Warning($"Re-requesting, 0 listings were requested. Status: {requestData.Status}, OK: {requestData.Ok}");
                MarketTaskManager.EnqueueDelay(500);
                MarketTaskManager.Enqueue(() => RequestDataForItem(itemId));
                ExpectedOfferingsParts = 999;
            } else {
                ExpectedOfferingsParts = (uint)float.Ceiling(requestData.AmountToArrive / listingsPerPacket);
            }
        } catch (Exception e) {
            Log.Error(e, "Error in MarketItemRequestStartDetour");
            IsBusy = false;
        }
        return itemRequestStartHook!.OriginalDisposeSafe(a1, packetRef);
    }

}