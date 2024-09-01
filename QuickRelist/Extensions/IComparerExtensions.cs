using Dalamud.Game.Network.Structures;
using System.Collections.Generic;

namespace QuickRelist.Extensions;

// newest-to-oldest
public class HistoryListingsByTime : IComparer<IMarketBoardHistoryListing> {
    // (-1, 0, 1), (a>b, a==b, b<a)
    int IComparer<IMarketBoardHistoryListing>.Compare(IMarketBoardHistoryListing? a, IMarketBoardHistoryListing? b) {
        if (a is null)
            return b is null ? 0 : -1;
        if (b is null)
            return 1;
        return a.PurchaseTime.CompareTo(b.PurchaseTime);
    }
}

public class HistoryListingsByPrice : IComparer<IMarketBoardHistoryListing> {
    // (-1, 0, 1), (a>b, a==b, b<a)
    int IComparer<IMarketBoardHistoryListing>.Compare(IMarketBoardHistoryListing? a, IMarketBoardHistoryListing? b) {
        if (a is null)
            return b is null ? 0 : -1;
        if (b is null)
            return 1;
        // inverted a->b to be smallest-to-largest
        return a.SalePrice.CompareTo(b.SalePrice);
    }
}

// smallest-to-largest
public class ItemListingsByPrice : IComparer<IMarketBoardItemListing> {
    int IComparer<IMarketBoardItemListing>.Compare(IMarketBoardItemListing? a, IMarketBoardItemListing? b) {
        if (a is null)
            return b is null ? 0 : -1;
        if (b is null)
            return 1;
        // inverted a->b to be smallest-to-largest
        return a.PricePerUnit.CompareTo(b.PricePerUnit);
    }
}