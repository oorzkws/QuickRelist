using Dalamud.Configuration;

namespace QuickRelist;

[Serializable]
public class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public int MaxPctDropFromLastSale = 30;
    public int MinPriceGil = 1;
    public bool Enabled { get; set; } = true;

    // the below exist just to make saving less cumbersome
    public void Save() {
        PluginInterface.SavePluginConfig(this);
    }
}