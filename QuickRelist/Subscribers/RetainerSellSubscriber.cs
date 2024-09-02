using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using ECommons;
using ECommons.Automation;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static QuickRelist.QuickRelist;

namespace QuickRelist;

public unsafe class RetainerSellSubscriber : IDisposable {
    internal AddonRetainerSell* RetainerSell;
    private static readonly ExcelSheet<Item> items = Data.GetExcelSheet<Item>()!;
    private const string hqToken = " \uE03C";
    private TaskManager taskManager;


    public RetainerSellSubscriber() {
        taskManager = new TaskManager(new TaskManagerConfiguration {
            AbortOnTimeout = false, // If it times out, we don't need to clear the entire stack
        });

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", OnSetup);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSell", OnFinalize);

    }

    public void Dispose() {
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSell");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSell");
        taskManager.Dispose();
    }

    internal Tuple<bool, Item>? GuessItemByName(Utf8String name) {
        var baseName = name.ExtractText();
        var isHq = baseName.EndsWith(hqToken);
        if (isHq) {
            baseName = baseName.Substring(0, baseName.Length - hqToken.Length);
        }

        var item = items.SingleOrDefault(i => i.Name.ExtractText() == baseName);

        if (item is null)
            return null;

        return new Tuple<bool, Item>(isHq, item);
    }

    private static int HistoricalMean(uint itemId, bool isHq) {
        var history = SubscriberMarket.ItemSalesHistory[itemId].ToImmutableArray().Where(sale => !isHq || sale.IsHq).ToImmutableArray();
        switch (history.Length) {
            case 0: return -1;
            case 1: return (int)history[1].SalePrice;
        }

        // Determine mean from a shortened array with the smallest and largest halves missing
        var quarter = (int)float.Round(history.Length / 4f);
        double mean = 0;
        double m = 0;
        for (var i = quarter; i < history.Length - quarter; i++) {
            // ReSharper disable once PossibleLossOfFraction SalePrice/Quantity = OriginalListingPrice, always integer
            mean += (history[i].SalePrice - mean) / ++m;
        }
        return (int)mean;
    }

    private uint GetMinimumAcceptablePrice(uint itemId, bool isHq) {
        var retainerIds = new HashSet<ulong>();
        var retainerMan = RetainerManager.Instance();

        if (retainerMan is not null) {
            for (var i = 0; i < retainerMan->Retainers.Length; i++) {
                retainerIds.Add(retainerMan->Retainers[i].RetainerId);
            }
        }

        var halvedHistoricalMean = (int)float.Round(HistoricalMean(itemId, isHq) * 0.5f);
        Log.Verbose($"Halved historical mean is {halvedHistoricalMean}");
        var filteredOfferings = SubscriberMarket.ItemCurrentOfferings[itemId].ToImmutableArray().Where(offer => (!isHq || offer.IsHq) && !retainerIds.Contains(offer.RetainerId)).Take(10).ToArray();
        var minimumPrice = 1u;
        for (var i = 0; i < filteredOfferings.Length; i++) {
            var offer = filteredOfferings[i];
            if (i == filteredOfferings.Length - 1) {
                Log.Debug("Matched the tenth or last unit price :shrug:");
                minimumPrice = offer.PricePerUnit;
                break;
            }
            if (offer.PricePerUnit < minimumPrice)
                continue;
            if (offer.PricePerUnit < halvedHistoricalMean)
                continue;
            minimumPrice = offer.PricePerUnit;
            break;
        }
        // Still haven't found a price lol
        if (minimumPrice == 1) {
            Log.Warning($"No acceptable price found for {items.GetRow(itemId)!.Name.ExtractText()}");
            minimumPrice = 69421; //items.GetRow(itemId)!.PriceLow;
        }
        return minimumPrice;
    }


    private void OnSetup(AddonEvent addonEvent, AddonArgs args) {
        RetainerSell = (AddonRetainerSell*)args.Addon;
        if (RetainerSell is null) {
            Log.Verbose("RetainerSell was gone when we tried to access it");
            return;
        }
        // Manual override, or not at a bell
        if (KeyState[VirtualKey.SHIFT] || !Svc.Condition.Any(ConditionFlag.OccupiedSummoningBell)) {
            return;
        }

        // Search Lumina for the item name (minus SEString garbage and HQ icons)
        var itemSeString = RetainerSell->ItemName->NodeText;
        var itemRegString = itemSeString.ExtractText();
        var itemData = GuessItemByName(itemSeString);
        if (itemData is null) {
            Log.Warning($"Couldn't find an item matching the name '{itemRegString}'");
            return;
        }

        var itemId = itemData.Item2.RowId;

        Log.Verbose($"Guessed Item ID for {itemRegString} is {itemId}");
        // If we don't have a cache entry, queue a request
        if (!SubscriberMarket.CachedItems.Contains(itemId)) {
            SubscriberMarket.EnqueueRequest(itemId);
        }

        var retries = 0;

        // Wait a little before trying to process
        taskManager.EnqueueDelay(500);
        // Wait for the price check to finish then enter price and close dialog
        taskManager.Enqueue(() => {
            if (!EzThrottler.Throttle("RetainerSellSubscriber.OnSetup.ProcessUpdate", 100)) {
                return false;
            }
            // 15s timeout
            if (retries++ > 15000 / 100) {
                Log.Warning($"Timeout on awaiting market data for {itemId}");
                taskManager.Abort();
                return false;
            }
            if (SubscriberMarket.CachedItems.Contains(itemId)) {
                // Make sure the addon is still valid
                if (RetainerSell is null) {
                    if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var newRetainerSell)) {
                        RetainerSell = newRetainerSell;
                    } else {
                        Log.Warning("Lost RetainerSell and couldn't find it again!");
                    }
                    // true in the sense that we have nothing to edit lmao
                    return true;
                }
                var targetPrice = GetMinimumAcceptablePrice(itemId, itemData.Item1) - 1;
                RetainerSell->AskingPrice->SetValue((int)targetPrice);
                Log.Information($"Set {itemSeString.ExtractText()} price to {targetPrice}");
                // 0 = accept, 1 = cancel
                Callback.Fire(&RetainerSell->AtkUnitBase, true, 0);
                return true;
            }
            return false;
        });
    }

    private void OnFinalize(AddonEvent addonEvent, AddonArgs args) {
        // If CTRL is held while closing the dialog, auto process the whole list
        if (RetainerSell is not null && (KeyState[VirtualKey.CONTROL] || SubscriberRetainerSellList.ListStep != 1)) {
            SubscriberRetainerSellList.AdjustNext();
        }
        RetainerSell = null;
    }
}