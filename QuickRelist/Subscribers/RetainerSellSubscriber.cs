using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Linq;

namespace QuickRelist;

public unsafe class RetainerSellSubscriber : IDisposable {
    private static readonly ExcelSheet<Item> items = Data.GetExcelSheet<Item>()!;
    private const string hqToken = " \uE03C";



    public RetainerSellSubscriber() {
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", OnSetup);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSell", OnFinalize);

        SubscriberMarket.OnRequestFinished += OnRequestFinished;
    }

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSell");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSell");
    }

    internal (bool hq, Item item) GuessItemByName(AddonRetainerSell* retainerSell) {
        // Search Lumina for the item name (minus SEString garbage and HQ icons)
        var baseName = retainerSell->ItemName->NodeText.GetText();
        var isHq = baseName.EndsWith(hqToken);
        if (isHq) {
            baseName = baseName.Substring(0, baseName.Length - hqToken.Length);
        }
        var itemData = items.First(i => i.Name.ExtractText() == baseName);
        Log.Verbose($"Guessed Item ID for {itemData.Name.GetText()} is {itemData.RowId}");
        return (isHq, itemData);
    }

    private static int HistoricalMean(uint itemId, bool isHq) {
        var history = SubscriberMarket.ItemSalesHistory[itemId].Where(sale => !isHq || sale.IsHq).ToArray();
        switch (history.Length) {
            case 0: return -1;
            case 1: return (int)history[0].SalePrice;
        }

        // Determine mean from a shortened array with the smallest and largest quartiles missing
        var quarter = (int)float.Round(history.Length / 4f);
        Log.Verbose($"{history.Length} entries, iterating from {quarter} to {history.Length - quarter}");
        history = history.Skip(quarter).SkipLast(quarter).ToArray();
        double mean = 0;
        double m = 0;
        foreach (var entry in history) {
            mean += (entry.SalePrice - mean) / ++m;
        }
        return (int)mean;
    }

    private uint GetMinimumAcceptablePrice(uint itemId, bool isHq) {
        var retainerIds = new HashSet<ulong>();
        var retainerMan = RetainerManager.Instance();
        if (retainerMan is not null) {
            retainerIds = retainerMan->Retainers.ToArray().Select(r => r.RetainerId).ToHashSet();
        }
        
        var halvedHistoricalMean = (uint)float.Round(HistoricalMean(itemId, isHq) * 0.5f);
        Log.Verbose($"Halved historical mean is {halvedHistoricalMean}");
        
        // Price fixing agreements, currently hard-coded
        var agreedRetainers = new HashSet<string>([
            "Shoshanaa" //owned by Strawberry Moon, 33777097236564573
        ]);
        var currentOfferings = SubscriberMarket.ItemCurrentOfferings[itemId];
        var agreedOffer = currentOfferings.FirstOrDefault(o => agreedRetainers.Contains(o.RetainerName));
        if (agreedOffer is not null) {
            Log.Information($"Using fixed price to match {agreedOffer.RetainerName}");
            return agreedOffer.PricePerUnit + 1;
        }
        // filter our own listings and non-HQ if the item is HQ
        var filteredOfferings = currentOfferings.Where(
            offer => (!isHq || offer.IsHq) 
            && !retainerIds.Contains(offer.RetainerId)
        ).Take(10).ToArray();
        // Take the first offer that is >= the halved historical mean
        foreach (var offer in filteredOfferings) {
            if (offer.PricePerUnit >= halvedHistoricalMean)
                return offer.PricePerUnit;
        }
        // Still no matches, use the sale history to determine the price
        if (halvedHistoricalMean > 0) {
            uint minimumPrice = halvedHistoricalMean * 2;
            Log.Debug($"No suitable listings found for {items.GetRow(itemId).Name.ExtractText()}, using historical mean price of {minimumPrice}");
            return minimumPrice;
        }
        // Welp
        Log.Warning($"No acceptable price found for {items.GetRow(itemId).Name.ExtractText()}");
        return 69420420;
    }

    private void OnRequestFinished(MarketSubscriber self, uint itemId) {
        if (!GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var retainerSell))
            return;
        // Search Lumina for the shown item name (minus SEString garbage and HQ icons)
        var itemData = GuessItemByName(retainerSell);
        // Changed since the request started
        if (itemData.item.RowId != itemId)
            return;
        // Fetch the target price from the history etc
        var targetPrice = GetMinimumAcceptablePrice(itemId, itemData.hq) - 1;
        var previousPrice = retainerSell->AskingPrice->Value;
        var basePrice = (uint)double.Ceiling(itemData.item.PriceLow * (itemData.hq ? 1.1 : 1.0)); // Default fill price, 10% bonus for HQ
        if (targetPrice != previousPrice) {
            retainerSell->AskingPrice->SetValue((int)targetPrice);
            if (previousPrice != basePrice) { // Existing listing
                var diff = targetPrice - previousPrice;
                var dir = diff > 0 ? "increased" : "decreased";
                Log.Information($"Adjusted {itemData.item.Name.GetText()} price by {diff} to {targetPrice}");
                Toasts.ShowNormal($"{itemData.item.Name.GetText()} price {dir} by {Math.Abs(diff)} gil");
            }
            // 0 = accept, 1 = cancel
            Callback.Fire(&retainerSell->AtkUnitBase, true, 0);
            return;
        }
        // 0 = accept, 1 = cancel
        Callback.Fire(&retainerSell->AtkUnitBase, true, 1);
    }

    private void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        var retainerSell = (AddonRetainerSell*)args.Addon.Address;
        if (retainerSell is null) {
            Log.Verbose("RetainerSell was gone when we tried to access it");
            return;
        }
        // Manual override, or not at a bell
        if (KeyState[VirtualKey.SHIFT] || !Svc.Condition.Any(ConditionFlag.OccupiedSummoningBell)) {
            return;
        }
        
        var itemData = GuessItemByName(retainerSell);
        var itemId = itemData.item.RowId;
        
        // If we don't have a cache entry, queue a request
        if (!SubscriberMarket.CachedItems.Contains(itemId)) {
            SubscriberMarket.EnqueueRequest(itemId);
        } else {
            OnRequestFinished(SubscriberMarket, itemId); // Just manually invoke it lol
        }
    }

    private void OnFinalize(AddonEvent addonEvent, AddonArgs args) {
        var retainerSell = (AddonRetainerSell*)args.Addon.Address;
        // If CTRL is held while closing the dialog, auto process the whole list
        if (retainerSell is not null) {
            if (KeyState[VirtualKey.CONTROL]) {
                SubscriberRetainerSellList.Adjusting = true;
            }
            if (SubscriberRetainerSellList.Adjusting) {
                SubscriberRetainerSellList.AdjustNext();
            }
        }
    }
}