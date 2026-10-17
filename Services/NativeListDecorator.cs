using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace PartySieve.Services;

/// <summary>
/// 公式のパーティ募集一覧と詳細画面で、募集主の名前をホームDCの色で塗る。
/// 名前の文字列には色指定が埋め込まれていてノードの文字色は効かないため、
/// 元の色指定を外して DC 色の色指定で包んだ文字列に置き換える。
/// 行の並びとは関係なく、画面上のテキストが受信済みの募集主名と一致したものだけを塗る。
/// </summary>
public sealed unsafe class NativeListDecorator : IDisposable
{
    private static readonly string[] AddonNames = ["LookingForGroup", "LookingForGroupDetail"];

    private readonly Configuration configuration;
    private readonly ListingMonitor monitor;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;
    private readonly Dictionary<nint, Painted> painted = [];
    private Dictionary<string, Vector4> hostColors = new(StringComparer.Ordinal);
    private int cachedVersion = -1;

    public NativeListDecorator(
        Configuration configuration,
        ListingMonitor monitor,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui)
    {
        this.configuration = configuration;
        this.monitor = monitor;
        this.addonLifecycle = addonLifecycle;
        this.gameGui = gameGui;
        this.addonLifecycle.RegisterListener(AddonEvent.PostUpdate, AddonNames, this.OnPostUpdate);
        this.addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonNames, this.OnPreFinalize);
    }

    /// <summary>いま色を付けている名前の数（設定画面での確認用）。</summary>
    public int PaintedCount => this.painted.Count;

    /// <summary>色を付けられる募集主の数（受信済みで DC が分かったもの）。</summary>
    public int KnownHostCount => this.hostColors.Count;

    public void Dispose()
    {
        this.addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, AddonNames, this.OnPostUpdate);
        this.addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonNames, this.OnPreFinalize);

        // 開いたままアンロードされたときは、元の文字列に戻しておく。
        foreach (var name in AddonNames)
        {
            var addon = this.gameGui.GetAddonByName(name);
            if (!addon.IsNull)
            {
                this.Restore((AtkUnitBase*)addon.Address);
            }
        }

        this.painted.Clear();
    }

    private void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible)
        {
            return;
        }

        if (!this.configuration.ColorHostByDataCenter)
        {
            this.Restore(addon);
            return;
        }

        this.RefreshHostColors();
        Walk(&addon->UldManager, 0, this.Paint);
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        // ノードが破棄されるので、覚えているアドレスは使えなくなる。
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon != null)
        {
            Walk(&addon->UldManager, 0, node => this.painted.Remove((nint)node));
        }
    }

    private void RefreshHostColors()
    {
        if (this.cachedVersion == this.monitor.Version)
        {
            return;
        }

        this.cachedVersion = this.monitor.Version;
        var colors = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in this.monitor.Snapshot())
        {
            var name = entry.Info.HostName;
            if (string.IsNullOrEmpty(name) || ambiguous.Contains(name)
                || !DataCenterPalette.TryGet(entry.Info.HomeDataCenter, out var color))
            {
                continue;
            }

            // 同じ名前で別DCの人がいたら、どちらか分からないので塗らない。
            if (colors.TryGetValue(name, out var existing) && existing != color)
            {
                colors.Remove(name);
                ambiguous.Add(name);
                continue;
            }

            colors[name] = color;
        }

        this.hostColors = colors;
    }

    private void Paint(AtkTextNode* node)
    {
        var key = (nint)node;
        var raw = node->NodeText.AsSpan();

        // 自分が書いた文字列のままなら、色が変わっていない限り何もしない。
        if (this.painted.TryGetValue(key, out var current) && raw.SequenceEqual(current.Written))
        {
            if (this.hostColors.TryGetValue(current.Name, out var still) && still == current.Color)
            {
                return;
            }

            SetText(node, current.Original);
            this.painted.Remove(key);
            raw = node->NodeText.AsSpan();
        }
        else if (current is not null)
        {
            // ゲームが別の文字列に書き換えた（行の使い回し）。
            this.painted.Remove(key);
        }

        if (raw.IsEmpty)
        {
            return;
        }

        var text = new ReadOnlySeString(raw).ExtractText().Trim();
        if (text.Length == 0 || !this.hostColors.TryGetValue(text, out var color))
        {
            return;
        }

        var original = raw.ToArray();
        var written = Recolor(original, color);
        SetText(node, written);
        this.painted[key] = new Painted(text, color, original, written);
    }

    private void Restore(AtkUnitBase* addon)
    {
        Walk(&addon->UldManager, 0, node =>
        {
            if (this.painted.Remove((nint)node, out var current) && node->NodeText.AsSpan().SequenceEqual(current.Written))
            {
                SetText(node, current.Original);
            }
        });
    }

    /// <summary>元の色指定を外し、全体を指定色で包んだ文字列を作る。アイコンなどはそのまま残す。</summary>
    private static byte[] Recolor(byte[] original, Vector4 color)
    {
        var builder = new SeStringBuilder();
        builder.PushColorRgba(color);
        foreach (var payload in new ReadOnlySeString(original))
        {
            if (payload.Type == ReadOnlySePayloadType.Macro
                && payload.MacroCode is MacroCode.Color or MacroCode.ColorType)
            {
                continue;
            }

            builder.Append(payload);
        }

        builder.PopColor();
        return builder.ToArray();
    }

    private static void SetText(AtkTextNode* node, byte[] text)
    {
        var terminated = new byte[text.Length + 1];
        text.CopyTo(terminated, 0);
        fixed (byte* pointer = terminated)
        {
            node->SetText(pointer);
        }
    }

    private delegate void TextNodeAction(AtkTextNode* node);

    private static void Walk(AtkUldManager* manager, int depth, TextNodeAction action)
    {
        if (manager == null || manager->NodeList == null || depth > 8)
        {
            return;
        }

        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null)
            {
                continue;
            }

            if (node->Type == NodeType.Text)
            {
                action((AtkTextNode*)node);
            }
            else if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                {
                    Walk(&component->UldManager, depth + 1, action);
                }
            }
        }
    }

    private sealed record Painted(string Name, Vector4 Color, byte[] Original, byte[] Written);
}
