using System.Numerics;
using Dalamud.Bindings.ImGui;
using PartySieve.Filtering;
using PartySieve.Models;
using PartySieve.Services;

namespace PartySieve.Windows;

/// <summary>
/// ルール（高機能フィルター）の編集部品。設定ウィンドウと、公式の詳細検索の横に出すパネルで共用する。
/// </summary>
public sealed class RuleEditor(Configuration configuration, ListingMonitor monitor)
{
    private static readonly string[] ActionLabels = ["条件に合う募集を隠す", "条件に合う募集だけ残す", "条件に合う募集は必ず表示"];
    private static readonly string[] JoinerLabels = ["かつ (AND)", "または (OR)"];
    private static readonly string[] TargetLabels = ["すべて", "募集文", "募集主", "コンテンツ名", "募集主のDC"];
    private static readonly string[] OperatorLabels = ["を含む", "を含まない", "正規表現（上級者向け）"];

    public static readonly Vector4 MatchColor = new(0.45f, 1f, 0.55f, 1f);
    public static readonly Vector4 MutedColor = new(0.65f, 0.65f, 0.65f, 1f);
    public static readonly Vector4 ErrorColor = new(1f, 0.6f, 0.2f, 1f);

    private string testDuty = string.Empty;
    private string testDescription = string.Empty;

    public void SaveAndReevaluate()
    {
        configuration.Save();
        monitor.Reevaluate();
    }

    /// <summary>「表示する募集主のDC」のチェックボックス。外したDCの募集を隠す。</summary>
    public void DrawDataCenterFilter()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("表示する募集主のDC:");
        var excluded = configuration.ExcludedDataCenters;
        foreach (var dc in DataCenterPalette.JapaneseDataCenters)
        {
            ImGui.SameLine();
            var shown = !excluded.Contains(dc, StringComparer.OrdinalIgnoreCase);
            ImGui.PushStyleColor(ImGuiCol.Text, DataCenterPalette.Get(dc));
            var clicked = ImGui.Checkbox($"{dc}##dcfilter", ref shown);
            ImGui.PopStyleColor();
            if (!clicked)
            {
                continue;
            }

            excluded.RemoveAll(d => string.Equals(d, dc, StringComparison.OrdinalIgnoreCase));
            if (!shown)
            {
                excluded.Add(dc);
            }

            this.SaveAndReevaluate();
        }
    }

    public void DrawHelp()
    {
        ImGui.TextWrapped(
            "1行が1つの条件です。「＋ 条件を追加」で行を増やし、行の先頭で「かつ」「または」を選びます。"
            + "「かつ」でつながった行がひとまとまりになり、「または」でまとまり同士をつなぎます。"
            + "全角半角・大文字小文字・カタカナひらがなは区別しません。");
        ImGui.TextColored(MutedColor, "例: [コンテンツ名] 絶 を含む ／ かつ [募集文] 練習 を含まない ／ または [募集文] 消化 を含む");
    }

    public void DrawRules()
    {
        if (ImGui.Button("ルールを追加"))
        {
            configuration.Rules.Add(new FilterRule());
            this.SaveAndReevaluate();
        }

        ImGui.Separator();

        var snapshot = monitor.Snapshot();
        var rules = configuration.Rules;
        for (var i = 0; i < rules.Count; i++)
        {
            ImGui.PushID(i);
            var structural = this.DrawRule(rules, i, snapshot);
            ImGui.PopID();
            if (structural)
            {
                break;
            }
        }
    }

    public void DrawTester()
    {
        ImGui.TextUnformatted("ルールのテスト");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##testDuty", "コンテンツ名（例: 絶もうひとつの未来）", ref this.testDuty, 128);
        ImGui.InputTextMultiline("##testDescription", ref this.testDescription, 512, new Vector2(-1, 60));
        if (string.IsNullOrEmpty(this.testDescription) && string.IsNullOrEmpty(this.testDuty))
        {
            return;
        }

        var sample = new ListingInfo { Description = this.testDescription, DutyName = this.testDuty };
        foreach (var rule in configuration.Rules.Where(r => r.Enabled))
        {
            var matched = monitor.Filter.Matches(rule, sample);
            if (matched is null)
            {
                continue;
            }

            ImGui.TextColored(matched.Value ? MatchColor : MutedColor, $"{(matched.Value ? "一致" : "不一致")}: {rule.Name}");
        }
    }

    /// <summary>ルールを1つ描く。ルールの一覧が変わった（削除・並べ替え）ときは true。</summary>
    private bool DrawRule(List<FilterRule> rules, int index, List<ListingEntry> snapshot)
    {
        var rule = rules[index];
        var changed = false;

        var enabled = rule.Enabled;
        if (ImGui.Checkbox("##enabled", ref enabled))
        {
            rule.Enabled = enabled;
            changed = true;
        }

        ImGui.SameLine();
        var name = rule.Name;
        ImGui.SetNextItemWidth(-110);
        if (ImGui.InputText("##name", ref name, 64))
        {
            rule.Name = name;
            changed = true;
        }

        ImGui.SameLine();
        var structural = false;
        if (ImGui.ArrowButton("##up", ImGuiDir.Up) && index > 0)
        {
            (rules[index - 1], rules[index]) = (rules[index], rules[index - 1]);
            structural = true;
        }

        ImGui.SameLine();
        if (ImGui.ArrowButton("##down", ImGuiDir.Down) && index < rules.Count - 1)
        {
            (rules[index + 1], rules[index]) = (rules[index], rules[index + 1]);
            structural = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("削除"))
        {
            rules.RemoveAt(index);
            structural = true;
        }

        if (structural)
        {
            this.SaveAndReevaluate();
            return true;
        }

        var action = (int)rule.Action;
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("##action", ref action, ActionLabels, ActionLabels.Length))
        {
            rule.Action = (RuleAction)action;
            changed = true;
        }

        changed |= this.DrawConditions(rule);

        if (ImGui.SmallButton("＋ 条件を追加"))
        {
            rule.Conditions.Add(new RuleCondition());
            changed = true;
        }

        ImGui.SameLine();
        var hits = snapshot.Count(e => monitor.Filter.Matches(rule, e.Info) == true);
        ImGui.TextColored(MutedColor, $"受信済みの {snapshot.Count} 件中 {hits} 件が条件に一致");

        if (changed)
        {
            this.SaveAndReevaluate();
        }

        ImGui.Separator();
        return false;
    }

    private bool DrawConditions(FilterRule rule)
    {
        const float JoinerWidth = 110f;
        const float TargetWidth = 110f;
        const float OperatorWidth = 150f;
        var changed = false;
        var conditions = rule.Conditions;
        var removeAt = -1;

        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            ImGui.PushID(i);

            if (i == 0)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(MutedColor, "条件");
                ImGui.SameLine(JoinerWidth + ImGui.GetStyle().ItemSpacing.X + ImGui.GetCursorStartPos().X);
            }
            else
            {
                var joiner = (int)condition.Joiner;
                ImGui.SetNextItemWidth(JoinerWidth);
                if (ImGui.Combo("##joiner", ref joiner, JoinerLabels, JoinerLabels.Length))
                {
                    condition.Joiner = (ConditionJoiner)joiner;
                    changed = true;
                }

                ImGui.SameLine();
            }

            var target = (int)condition.Target;
            ImGui.SetNextItemWidth(TargetWidth);
            if (ImGui.Combo("##target", ref target, TargetLabels, TargetLabels.Length))
            {
                condition.Target = (RuleTarget)target;
                changed = true;
            }

            ImGui.SameLine();
            var text = condition.Text;
            var removeWidth = ImGui.GetFrameHeight();
            ImGui.SetNextItemWidth(-(OperatorWidth + removeWidth + (ImGui.GetStyle().ItemSpacing.X * 2)));
            if (ImGui.InputTextWithHint("##text", "語", ref text, 256))
            {
                condition.Text = text;
                changed = true;
            }

            ImGui.SameLine();
            var op = (int)condition.Operator;
            ImGui.SetNextItemWidth(OperatorWidth);
            if (ImGui.Combo("##operator", ref op, OperatorLabels, OperatorLabels.Length))
            {
                condition.Operator = (ConditionOperator)op;
                changed = true;
            }

            ImGui.SameLine();
            ImGui.BeginDisabled(conditions.Count <= 1);
            if (ImGui.Button("×", new Vector2(removeWidth, 0)))
            {
                removeAt = i;
            }

            ImGui.EndDisabled();

            if (condition.Operator == ConditionOperator.Regex && !string.IsNullOrWhiteSpace(condition.Text)
                && monitor.Filter.CompileRegex(condition.Text).Error is { } error)
            {
                ImGui.TextColored(ErrorColor, error);
            }

            ImGui.PopID();
        }

        if (removeAt >= 0)
        {
            conditions.RemoveAt(removeAt);
            changed = true;
        }

        return changed;
    }
}
