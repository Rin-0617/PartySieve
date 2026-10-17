using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Game.NativeWrapper;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using PartySieve.Services;

namespace PartySieve.Windows;

/// <summary>
/// 公式の「詳細検索」ウィンドウの右隣に張り付けて出す、高機能フィルターのパネル。
/// 詳細検索が開いているあいだだけ表示する。
/// </summary>
public sealed class SearchPanel : Window
{
    /// <summary>公式の詳細検索ウィンドウのアドオン名。</summary>
    public const string SearchAddonName = "LookingForGroupSearch";

    private readonly Configuration configuration;
    private readonly ListingMonitor monitor;
    private readonly RuleEditor ruleEditor;
    private readonly IGameGui gameGui;
    private readonly Action openConfig;

    public SearchPanel(
        Configuration configuration,
        ListingMonitor monitor,
        RuleEditor ruleEditor,
        IGameGui gameGui,
        Action openConfig)
        : base($"PartySieve {Plugin.VersionText} 高機能フィルター###PartySieveSearchPanel", ImGuiWindowFlags.NoCollapse)
    {
        this.configuration = configuration;
        this.monitor = monitor;
        this.ruleEditor = ruleEditor;
        this.gameGui = gameGui;
        this.openConfig = openConfig;
        this.IsOpen = true;
        this.RespectCloseHotkey = false;
        this.ShowCloseButton = false;
        this.Size = new Vector2(440, 0);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.TitleBarButtons.Add(SupportLink.CreateTitleBarButton());
    }

    public override bool DrawConditions()
    {
        if (!this.configuration.ShowSearchPanel)
        {
            return false;
        }

        var addon = this.gameGui.GetAddonByName(SearchAddonName);
        if (addon.IsNull || !addon.IsVisible)
        {
            return false;
        }

        // 毎フレーム、詳細検索の右端に隙間なく合わせる。高さもそろえる。
        // Window はサイズにだけ ImGui の全体倍率を掛ける（位置には掛けない）ので、高さだけ倍率で割って渡す。
        var (frameRight, frameTop, frameHeight) = GetFrameBounds(addon);
        var height = frameHeight / ImGuiHelpers.GlobalScale;
        this.Position = new Vector2(frameRight, frameTop);
        this.PositionCondition = ImGuiCond.Always;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, height),
            MaximumSize = new Vector2(900, Math.Max(height, 300)),
        };
        return true;
    }

    /// <summary>
    /// 見えているウィンドウ枠の右端・上端・高さ。
    /// ウィンドウノードはアドオン全体と同じ大きさで、緑の背景はその中に余白をとって描かれるので、
    /// ウィンドウ部品の中でいちばん大きい表示中のナイングリッド（背景画像）の範囲を使う。
    /// </summary>
    private static unsafe (float Right, float Top, float Height) GetFrameBounds(AtkUnitBasePtr addon)
    {
        var unit = (AtkUnitBase*)addon.Address;
        var frame = unit->WindowNode;
        var fallback = (addon.X + addon.ScaledWidth, addon.Y, addon.ScaledHeight);
        if (frame == null || frame->Component == null)
        {
            return fallback;
        }

        var manager = &frame->Component->UldManager;
        AtkResNode* background = null;
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node->Type != NodeType.NineGrid || !node->IsVisible())
            {
                continue;
            }

            if (background == null || node->Width * node->Height > background->Width * background->Height)
            {
                background = node;
            }
        }

        if (background == null)
        {
            return fallback;
        }

        var width = background->Width * background->ScaleX * unit->Scale;
        var height = background->Height * background->ScaleY * unit->Scale;
        return (background->ScreenX + width, background->ScreenY, height);
    }

    public override void Draw()
    {
        var hide = this.configuration.HideMatches;
        if (ImGui.Checkbox("ルールとスパム判定で隠す", ref hide))
        {
            this.configuration.HideMatches = hide;
            this.ruleEditor.SaveAndReevaluate();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("設定"))
        {
            this.openConfig();
        }

        var all = this.monitor.Snapshot();
        ImGui.TextColored(
            RuleEditor.MutedColor,
            $"受信 {all.Count} 件 / 非表示 {all.Count(e => e.Result.Hidden)} 件　※変更は一覧の更新で反映");

        this.ruleEditor.DrawDataCenterFilter();
        ImGui.Separator();

        if (ImGui.CollapsingHeader("書き方"))
        {
            this.ruleEditor.DrawHelp();
        }

        this.ruleEditor.DrawRules();

        if (ImGui.CollapsingHeader("ルールのテスト"))
        {
            this.ruleEditor.DrawTester();
        }
    }
}
