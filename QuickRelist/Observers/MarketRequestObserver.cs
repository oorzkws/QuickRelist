using Dalamud.Game.Network.Internal.MarketBoardUploaders;

namespace QuickRelist;

public class MarketRequestObserver : IObserver<MarketRequest> {
    private IDisposable unsubscriber;

    public void OnCompleted() {
        throw new NotImplementedException();
    }

    public void OnError(Exception error) {
        throw new NotImplementedException();
    }

    public void OnNext(MarketRequest value) {
        throw new NotImplementedException();
    }
    
}