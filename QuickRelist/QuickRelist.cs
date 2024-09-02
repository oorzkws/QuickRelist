using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
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
    internal static MarketSubscriber SubscriberMarket { get; private set; } = null!;
    internal static ItemSearchResultSubscriber SubscriberItemSearchResult { get; private set; } = null!;
    internal static RetainerSellListSubscriber SubscriberRetainerSellList { get; private set; } = null!;
    internal static RetainerSellSubscriber SubscriberRetainerSell { get; private set; } = null!;
    internal static ContextMenuSubscriber SubscriberContextMenu { get; private set; } = null!;

    private const string CommandName = "/prelist"; //pReList, not PreList

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("QuickRelist");
    private ConfigWindow ConfigWindow { get; init; }

    // Hook AgentRetainer (070)?

    public QuickRelist() {
        ECommonsMain.Init(PluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Displays the configuration interface for QuickRelist",
        });

        SubscriberMarket = new MarketSubscriber();
        SubscriberItemSearchResult = new ItemSearchResultSubscriber();
        SubscriberRetainerSellList = new RetainerSellListSubscriber();
        SubscriberRetainerSell = new RetainerSellSubscriber();
        SubscriberContextMenu = new ContextMenuSubscriber();

        PluginInterface.UiBuilder.Draw += DrawUi;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    public void Dispose() {
        ECommonsMain.Dispose();

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        SubscriberMarket.Dispose();
        SubscriberItemSearchResult.Dispose();
        SubscriberRetainerSellList.Dispose();
        SubscriberRetainerSell.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleConfigUi();
    }

    private void DrawUi() => WindowSystem.Draw();

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}