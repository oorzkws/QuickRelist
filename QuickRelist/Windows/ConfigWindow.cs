using Dalamud.Interface.Utility;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace QuickRelist.Windows;

public class ConfigWindow(QuickRelist plugin) : Window("A Wonderful Configuration Window###With a constant ID"), IDisposable {
    private readonly Configuration configuration = plugin.Configuration;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui

    public void Dispose() {
    }

    public override void PreDraw() {
        Flags &= ~ImGuiWindowFlags.NoMove;
    }

    public override unsafe void Draw() {
        ImGui.SetNextWindowSizeConstraints(new Vector2(700, 600) * ImGuiHelpers.GlobalScale, new Vector2(9999));
        // can't ref a property, so use a local copy
        var configValue = configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref configValue)) {
            configuration.Enabled = configValue;
            // can save immediately on change, if you don't want to provide a "Save and Close" button
            configuration.Save();
        }

        if (ImGui.Button("Generate MB Request")) {
            QuickRelist.MarketSubscriber.RequestDataForItem(2);
        }
    }
}