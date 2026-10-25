using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PartySieve.Services;
using PartySieve.Windows;

namespace PartySieve;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/psieve";

    /// <summary>ウィンドウのタイトルに出すバージョン（csproj の Version から取る）。</summary>
    public static string VersionText { get; } = $"v{typeof(Plugin).Assembly.GetName().Version?.ToString(3)}";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly WindowSystem windowSystem = new("PartySieve");
    private readonly ListingMonitor monitor;
    private readonly NativeListDecorator decorator;
    private readonly NativeSearchExtender searchExtender;
    private readonly ConfigWindow configWindow;
    private readonly SearchPanel searchPanel;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPartyFinderGui partyFinderGui,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.addonLifecycle = addonLifecycle;
        this.log = log;

        this.configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.Initialize(pluginInterface);

        this.monitor = new ListingMonitor(this.configuration, partyFinderGui, log);
        this.decorator = new NativeListDecorator(this.configuration, this.monitor, addonLifecycle, gameGui);
        this.searchExtender = new NativeSearchExtender(this.configuration, addonLifecycle, gameGui, log);

        var ruleEditor = new RuleEditor(this.configuration, this.monitor);
        this.configWindow = new ConfigWindow(this.configuration, this.monitor, ruleEditor, this.decorator);
        this.searchPanel = new SearchPanel(this.configuration, this.monitor, ruleEditor, this.searchExtender, this.OpenConfigUi);
        this.windowSystem.AddWindow(this.configWindow);
        this.windowSystem.AddWindow(this.searchPanel);

        this.pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi += this.OpenConfigUi;
        this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        this.addonLifecycle.RegisterListener(AddonEvent.PostSetup, this.LogPartyFinderAddon);

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "PartySieveの設定を開きます。サブコマンド: on（隠す）, off（判定のみ）",
        });
    }

    public void Dispose()
    {
        this.commandManager.RemoveHandler(CommandName);
        this.addonLifecycle.UnregisterListener(AddonEvent.PostSetup, this.LogPartyFinderAddon);
        this.searchExtender.Dispose();
        this.decorator.Dispose();
        this.monitor.Dispose();

        this.pluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenConfigUi;
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;

        this.windowSystem.RemoveAllWindows();
    }

    private void OpenConfigUi()
    {
        this.configWindow.IsOpen = true;
    }

    /// <summary>パーティ募集まわりのアドオン名を /xllog に出す（詳細検索のアドオン名の確認用）。</summary>
    private void LogPartyFinderAddon(AddonEvent type, AddonArgs args)
    {
        if (args.AddonName.StartsWith("LookingForGroup", StringComparison.Ordinal))
        {
            this.log.Debug($"Addon opened: {args.AddonName}");
        }
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":
                this.configuration.HideMatches = true;
                this.configuration.Save();
                break;
            case "off":
                this.configuration.HideMatches = false;
                this.configuration.Save();
                break;
            default:
                this.configWindow.Toggle();
                break;
        }
    }
}
