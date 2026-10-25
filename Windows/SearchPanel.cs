using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using PartySieve.Services;

namespace PartySieve.Windows;

/// <summary>
/// 公式の「詳細検索」ウィンドウをネイティブに右へ広げ（<see cref="NativeSearchExtender"/>）、
/// その空いた領域の中に、枠・タイトルバー・背景なしで高機能フィルターの中身を描く。
/// 見た目は「詳細検索の窓がそのまま右に伸びて、中に機能が入っている」ようになる。
/// </summary>
public sealed class SearchPanel : Window
{
    private readonly Configuration configuration;
    private readonly ListingMonitor monitor;
    private readonly RuleEditor ruleEditor;
    private readonly NativeSearchExtender extender;
    private readonly Action openConfig;

    public SearchPanel(
        Configuration configuration,
        ListingMonitor monitor,
        RuleEditor ruleEditor,
        NativeSearchExtender extender,
        Action openConfig)
        : base(
            "###PartySieveSearchPanel",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoBringToFrontOnFocus)
    {
        this.configuration = configuration;
        this.monitor = monitor;
        this.ruleEditor = ruleEditor;
        this.extender = extender;
        this.openConfig = openConfig;
        this.IsOpen = true;
        this.RespectCloseHotkey = false;
        this.ShowCloseButton = false;
        this.DisableWindowSounds = true;
    }

    public override bool DrawConditions()
    {
        if (!this.configuration.ShowSearchPanel || !this.extender.TryGetPanelArea(out var position, out var size))
        {
            return false;
        }

        // Window はサイズに ImGui の全体倍率を掛けるので、画素サイズを倍率で割って渡す（位置はそのまま画面座標）。
        this.Position = position;
        this.PositionCondition = ImGuiCond.Always;
        this.Size = size / ImGuiHelpers.GlobalScale;
        this.SizeCondition = ImGuiCond.Always;
        return true;
    }

    public override void Draw()
    {
        // タイトルバーが無いので、左に「設定」、右にプラグイン名・バージョン・支援ボタンを置く。
        if (ImGui.SmallButton("設定"))
        {
            this.openConfig();
        }

        var title = $"PartySieve {Plugin.VersionText}";
        var rightWidth = ImGui.CalcTextSize(title).X + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFrameHeight();
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - rightWidth - ImGui.GetStyle().WindowPadding.X));
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(RuleEditor.MutedColor, title);
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Heart))
        {
            Util.OpenLink(SupportLink.OfuseUrl);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("OFUSEで開発を応援する");
        }

        ImGui.Separator();

        var hide = this.configuration.HideMatches;
        if (ImGui.Checkbox("ルールとスパム判定で隠す", ref hide))
        {
            this.configuration.HideMatches = hide;
            this.ruleEditor.SaveAndReevaluate();
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
