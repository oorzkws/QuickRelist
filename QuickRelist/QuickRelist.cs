using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
using ECommons;
using QuickRelist.Windows;
using System.Collections.Generic;

namespace QuickRelist;

// ReSharper disable once ClassNeverInstantiated.Global
public class QuickRelist : IDalamudPlugin {
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IMarketBoard MarketBoard { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    //[PluginService] internal static Dalamud.Game.MarketBoard.                InternalMarketBoard        { get; private set; } = null!;
    internal static MarketSubscriber MarketSubscriber { get; private set; } = null!;
    internal static ItemSearchResultSubscriber ItemSearchResultSubscriber { get; private set; } = null!;
    internal static RetainerSellListSubscriber RetainerSellListSubscriber { get; private set; } = null!;
    internal static RetainerSellSubscriber RetainerSellSubscriber { get; private set; } = null!;
    internal static ContextMenuSubscriber ContextMenuSubscriber { get; private set; } = null!;

    private const string CommandName = "/prelist"; //pReList, not PreList

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("QuickRelist");
    private ConfigWindow ConfigWindow { get; init; }

    private static readonly List<string> WatchedAddons = new() {
        "RetainerSellList",
        "RetainerSell",     // Price set/adjustment popup
        "ItemSearchResult", // Price list
    };

    // Hook AgentRetainer (070)?

    public QuickRelist() {
        ECommonsMain.Init(PluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Displays the configuration interface for QuickRelist",
        });

        MarketSubscriber = new MarketSubscriber();
        ItemSearchResultSubscriber = new ItemSearchResultSubscriber();
        RetainerSellListSubscriber = new RetainerSellListSubscriber();
        RetainerSellSubscriber = new RetainerSellSubscriber();
        ContextMenuSubscriber = new ContextMenuSubscriber();

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
    }

    public void Dispose() {
        ECommonsMain.Dispose();

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        MarketSubscriber.Dispose();
        ItemSearchResultSubscriber.Dispose();
        RetainerSellListSubscriber.Dispose();
        RetainerSellSubscriber.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleConfigUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
}