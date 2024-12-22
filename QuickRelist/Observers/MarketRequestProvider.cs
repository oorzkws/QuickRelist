using Dalamud.Game.Network.Internal.MarketBoardUploaders;
using Dalamud.Game.Network.Structures;
using ECommons;
using QuickRelist.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace QuickRelist;

public record struct MarketRequest(
    uint ItemId
) {
    public readonly uint ItemId = ItemId;
    public readonly SortedSet<IMarketBoardHistoryListing> HistoryListings = new(new HistoryListingsByPrice());
    public readonly SortedSet<IMarketBoardItemListing> ItemListings = new(new ItemListingsByPrice());
    public uint TotalPackets = 0;
    public uint ReceivedPackets = 0;
    public uint Status = 0;
    public bool Completed = false;
    public bool Errored => Completed && Status != 0;
};

public class MarketRequestProvider : IObservable<MarketRequest>, IDisposable {
    private readonly HashSet<IObserver<MarketRequest>> observers = [];
    private MarketRequest request;

    public MarketRequestProvider(uint itemId) {
        request = new MarketRequest(itemId);
        QuickRelist.SubscriberMarket.OnRequestStarted += OnRequestStarted;
        QuickRelist.SubscriberMarket.OnRequestUpdated += OnRequestUpdated;
        QuickRelist.SubscriberMarket.OnRequestErrored += OnRequestErrored;
    }

    private class Unsubscriber(HashSet<IObserver<MarketRequest>> observers, IObserver<MarketRequest> observer) : IDisposable {
        public void Dispose() {
            observers.Remove(observer);
        }
    }

    private void NotifyObserversUpdated() {
        observers.Each(o => o.OnNext(request));
    }

    private void NotifyObserversCompleted() {
        observers.Each(o => o.OnCompleted());
    }

    private void NotifyObserversErrored(Exception exception) {
        observers.Each(o => o.OnError(exception));
    }

    public IDisposable Subscribe(IObserver<MarketRequest> observer) {
        observers.Add(observer);
        return new Unsubscriber(observers, observer);
    }

    public void Dispose() {
        QuickRelist.SubscriberMarket.OnRequestStarted -= OnRequestStarted;
        QuickRelist.SubscriberMarket.OnRequestUpdated -= OnRequestUpdated;
        QuickRelist.SubscriberMarket.OnRequestErrored -= OnRequestErrored;
    }

    private void OnRequestStarted(uint totalPackets) {
        request.TotalPackets = totalPackets;
    }

    private void OnRequestUpdated(IMarketBoardHistory? history, IMarketBoardCurrentOfferings? listings) {
        var notifyUpdated = false;
        var notifyCompleted = false;
        // Already done?
        if (request.Completed) {
            return;
        }
        // history?
        if (history is not null) {
            if (history.ItemId != request.ItemId)
                return;

            history.HistoryListings.Each(entry => request.HistoryListings.Add(entry));
            notifyUpdated = true;
        }
        // listings?
        if (listings is not null && listings.ItemListings.Count != 0) {
            if (listings.ItemListings[0].ItemId != request.ItemId)
                return;

            listings.ItemListings.Each(entry => request.ItemListings.Add(entry));
            notifyUpdated = true;
            if (++request.ReceivedPackets >= request.TotalPackets) {
                request.Completed = true;
                notifyCompleted = true;
            }
        }
        // Update observers if relevant
        if (notifyUpdated) {
            NotifyObserversUpdated();
        }
        if (notifyCompleted) {
            NotifyObserversCompleted();
        }
    }

    private void OnRequestErrored(uint exceptionCode) {
        request.Status = exceptionCode;
        request.Completed = true;
        NotifyObserversErrored(exceptionCode == 0x70000003
                                   ? new Exception($"Server declined market request due to rate-limit")
                                   : new Exception($"Market request failed due to error: {exceptionCode}"));
    }
}