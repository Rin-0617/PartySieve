using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using PartySieve.Filtering;
using PartySieve.Models;
using PartySieve.Services;
using RinUi;

namespace PartySieve.Windows;

public sealed class ConfigWindow : Window
{
    private static readonly string[] ViewLabels = ["すべて", "表示中のみ", "非表示のみ"];
    private static readonly Vector4 HiddenColor = new(1f, 0.45f, 0.45f, 1f);

    private readonly Configuration configuration;
    private readonly ListingMonitor monitor;
    private readonly RuleEditor ruleEditor;
    private readonly NativeListDecorator decorator;

    private int viewMode;
    private bool newOnly;
    private bool latestBatchOnly;
    private string listSearch = string.Empty;

    public ConfigWindow(
        Configuration configuration,
        ListingMonitor monitor,
        RuleEditor ruleEditor,
        NativeListDecorator decorator)
        : base($"PartySieve {Plugin.VersionText} 設定###PartySieveConfig")
    {
        this.decorator = decorator;
        this.configuration = configuration;
        this.monitor = monitor;
        this.ruleEditor = ruleEditor;
        this.Size = new Vector2(940, 620);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.TitleBarButtons.Add(SupportLink.CreateTitleBarButton());
    }

    public override void Draw()
    {
        Theme.Push();
        try
        {
            this.DrawTabs();
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void DrawTabs()
    {
        if (ImGui.BeginTabBar("PartySieveTabs"))
        {
            if (ImGui.BeginTabItem("全般"))
            {
                this.DrawGeneralTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("スパム判定"))
            {
                this.DrawSpamTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("ルール"))
            {
                this.ruleEditor.DrawHelp();
                this.ruleEditor.DrawRules();
                this.ruleEditor.DrawTester();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("受信ログ"))
            {
                this.DrawLogTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    // ---- 全般 -------------------------------------------------------------------

    private void DrawGeneralTab()
    {
        var changed = false;

        var hide = this.configuration.HideMatches;
        changed |= ImGui.Checkbox("ルールとスパム判定に合った募集を隠す", ref hide);
        this.configuration.HideMatches = hide;
        ImGui.TextColored(RuleEditor.MutedColor, "オフにすると判定だけ行い、隠しません（受信ログで消しすぎていないか確認できます）。");

        var color = this.configuration.ColorHostByDataCenter;
        changed |= ImGui.Checkbox("公式のパーティ募集で、募集主の名前をホームDCの色で表示する", ref color);
        this.configuration.ColorHostByDataCenter = color;
        ImGui.Indent();
        foreach (var dc in new[] { "Elemental", "Gaia", "Mana", "Meteor" })
        {
            ImGui.TextColored(DataCenterPalette.Get(dc), dc);
            ImGui.SameLine();
        }

        ImGui.NewLine();
        ImGui.TextColored(
            RuleEditor.MutedColor,
            $"DCが分かった募集主 {this.decorator.KnownHostCount} 人 / いま色を付けている名前 {this.decorator.PaintedCount} 件");
        ImGui.Unindent();

        this.ruleEditor.DrawDataCenterFilter();
        ImGui.TextColored(RuleEditor.MutedColor, "チェックを外したDCの募集主の募集を隠します。日本以外のワールドの募集主には影響しません。");

        var panel = this.configuration.ShowSearchPanel;
        changed |= ImGui.Checkbox("公式の「詳細検索」の横に高機能フィルターを表示する", ref panel);
        this.configuration.ShowSearchPanel = panel;

        var newMinutes = this.configuration.NewHighlightMinutes;
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("分間は受信ログで新着として強調", ref newMinutes, 0, 30);
        this.configuration.NewHighlightMinutes = newMinutes;

        if (changed)
        {
            this.ruleEditor.SaveAndReevaluate();
        }
    }

    // ---- スパム判定 -------------------------------------------------------------

    private void DrawSpamTab()
    {
        var spam = this.configuration.Spam;
        var changed = false;

        var duplicate = spam.DuplicateEnabled;
        changed |= ImGui.Checkbox("同じ文面の連投を隠す", ref duplicate);
        spam.DuplicateEnabled = duplicate;
        ImGui.Indent();
        var hosts = spam.DuplicateHostThreshold;
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("人以上が同じ文面を出したら", ref hosts, 2, 10);
        spam.DuplicateHostThreshold = hosts;
        var window = spam.DuplicateWindowMinutes;
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("分以内に", ref window, 5, 120);
        spam.DuplicateWindowMinutes = window;
        var minLength = spam.DuplicateMinLength;
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("文字以上の文面だけ対象（記号・空白を除く）", ref minLength, 1, 60);
        spam.DuplicateMinLength = minLength;
        ImGui.Unindent();

        var symbol = spam.SymbolEnabled;
        changed |= ImGui.Checkbox("記号だらけの募集文を隠す", ref symbol);
        spam.SymbolEnabled = symbol;
        ImGui.Indent();
        var ratio = spam.SymbolRatio * 100f;
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderFloat("% 以上が記号なら", ref ratio, 20f, 100f, "%.0f");
        spam.SymbolRatio = ratio / 100f;
        var symbolMin = spam.SymbolMinLength;
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("文字以上の文面だけ対象##symbol", ref symbolMin, 1, 60);
        spam.SymbolMinLength = symbolMin;
        ImGui.Unindent();

        var repeat = spam.RepeatEnabled;
        changed |= ImGui.Checkbox("同じ文字の長い連続を隠す", ref repeat);
        spam.RepeatEnabled = repeat;
        ImGui.Indent();
        var run = spam.RepeatRunLength;
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("回以上続いたら", ref run, 3, 40);
        spam.RepeatRunLength = run;
        ImGui.Unindent();

        if (changed)
        {
            this.ruleEditor.SaveAndReevaluate();
        }

        ImGui.Separator();
        ImGui.TextWrapped("誤って隠したくない募集は、「ルール」で「合うものは常に表示」のルールを作ると、スパム判定より優先して表示できます。");
    }

    // ---- 受信ログ ---------------------------------------------------------------

    private void DrawLogTab()
    {
        var now = DateTime.UtcNow;
        var all = this.monitor.Snapshot();
        var hiddenCount = all.Count(e => e.Result.Hidden);
        ImGui.TextUnformatted($"受信 {all.Count} 件 / 表示 {all.Count - hiddenCount} 件 / 非表示 {hiddenCount} 件");
        ImGui.SameLine();
        if (ImGui.Button("記録を消す"))
        {
            this.monitor.Clear();
        }

        ImGui.SetNextItemWidth(140);
        ImGui.Combo("##view", ref this.viewMode, ViewLabels, ViewLabels.Length);
        ImGui.SameLine();
        ImGui.Checkbox("新着のみ", ref this.newOnly);
        ImGui.SameLine();
        ImGui.Checkbox("最後に取得した分のみ", ref this.latestBatchOnly);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "このログ内を検索（ルールと同じ書き方）", ref this.listSearch, 256);

        var (searchQuery, searchError) = this.monitor.Filter.Compile(this.listSearch);
        if (searchError is not null)
        {
            ImGui.TextColored(RuleEditor.ErrorColor, searchError);
        }

        var newWindow = TimeSpan.FromMinutes(this.configuration.NewHighlightMinutes);
        var rows = all
            .Where(e => this.viewMode switch
            {
                1 => !e.Result.Hidden,
                2 => e.Result.Hidden,
                _ => true,
            })
            .Where(e => !this.newOnly || now - e.FirstSeen <= newWindow)
            .Where(e => !this.latestBatchOnly || e.Batch == this.monitor.LatestBatch)
            .Where(e => searchQuery is null || searchQuery.Matches(ListingFilter.TargetText(e.Info, RuleTarget.All)))
            .OrderByDescending(e => e.FirstSeen)
            .ToList();

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("Listings", 6, flags))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("状態", ImGuiTableColumnFlags.WidthFixed, 44f);
        ImGui.TableSetupColumn("DC", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("募集主", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("コンテンツ", ImGuiTableColumnFlags.WidthFixed, 170f);
        ImGui.TableSetupColumn("募集文", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("理由", ImGuiTableColumnFlags.WidthFixed, 170f);
        ImGui.TableHeadersRow();

        foreach (var entry in rows)
        {
            var info = entry.Info;
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (entry.Result.Hidden)
            {
                ImGui.TextColored(HiddenColor, "非表示");
            }
            else if (now - entry.FirstSeen <= newWindow)
            {
                ImGui.TextColored(RuleEditor.MatchColor, "NEW");
            }

            ImGui.TableNextColumn();
            var dc = string.IsNullOrEmpty(info.HomeDataCenter) ? "?" : info.HomeDataCenter;
            ImGui.TextColored(DataCenterPalette.Get(dc), dc);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"ホーム: {info.HomeWorld}\n現在地: {info.CurrentWorld}");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(info.HostName);
            ImGui.TextColored(RuleEditor.MutedColor, $"@{info.HomeWorld}");

            ImGui.TableNextColumn();
            ImGui.TextWrapped(info.DutyName);
            ImGui.TextColored(RuleEditor.MutedColor, $"{info.SlotsFilled}/{info.SlotsTotal}"
                + (info.MinItemLevel > 0 ? $"  IL{info.MinItemLevel}" : string.Empty));

            ImGui.TableNextColumn();
            ImGui.TextWrapped(info.Description);

            ImGui.TableNextColumn();
            if (entry.Result.Hidden)
            {
                ImGui.TextWrapped(string.Join("\n", entry.Result.Reasons));
            }
            else if (entry.Result.ShownBy is { } shownBy)
            {
                ImGui.TextColored(RuleEditor.MatchColor, $"常に表示: {shownBy}");
            }
        }

        ImGui.EndTable();
    }
}
