using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PartySieve.Services;

/// <summary>
/// 公式の「詳細検索」ウィンドウ（LookingForGroupSearch）を、ネイティブに右へ広げる。
/// 窓部品と背景のナイングリッドの幅を毎フレーム広げ直し、閉じたら元に戻す。
/// 広げてできた右側の空き領域（画面座標）を <see cref="TryGetPanelArea"/> で返し、そこに ImGui で中身を描く。
/// </summary>
public sealed unsafe class NativeSearchExtender : IDisposable
{
    public const string AddonName = "LookingForGroupSearch";

    /// <summary>窓を右へ広げる量（ゲーム内の単位。実際の画素は Scale 倍）。</summary>
    private const float ExtraWidth = 470f;

    private readonly Configuration configuration;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Dictionary<nint, float> originalWidths = [];

    private Vector2 areaPosition;
    private Vector2 areaSize;
    private bool hasArea;

    public NativeSearchExtender(Configuration configuration, IAddonLifecycle addonLifecycle, IGameGui gameGui, IPluginLog log)
    {
        this.configuration = configuration;
        this.addonLifecycle = addonLifecycle;
        this.gameGui = gameGui;
        this.log = log;
        this.addonLifecycle.RegisterListener(AddonEvent.PostUpdate, AddonName, this.OnPostUpdate);
        this.addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, this.OnPreFinalize);
    }

    public void Dispose()
    {
        this.addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, AddonName, this.OnPostUpdate);
        this.addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, this.OnPreFinalize);

        var addon = this.gameGui.GetAddonByName(AddonName);
        if (!addon.IsNull)
        {
            this.Restore((AtkUnitBase*)addon.Address);
        }

        this.originalWidths.Clear();
        this.hasArea = false;
    }

    /// <summary>ImGui の中身を描くべき、広げた右側領域（画面座標・画素）。詳細検索が閉じている等なら false。</summary>
    public bool TryGetPanelArea(out Vector2 position, out Vector2 size)
    {
        position = this.areaPosition;
        size = this.areaSize;
        return this.hasArea;
    }

    private void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible)
        {
            this.hasArea = false;
            return;
        }

        if (!this.configuration.ShowSearchPanel)
        {
            this.Restore(addon);
            this.hasArea = false;
            return;
        }

        try
        {
            this.Widen(addon);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "[PartySieve] Failed to widen the search window.");
            this.hasArea = false;
        }
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon != null)
        {
            this.Restore(addon);
        }

        this.hasArea = false;
    }

    private void Widen(AtkUnitBase* addon)
    {
        var frame = addon->WindowNode;
        if (frame == null || frame->Component == null)
        {
            this.hasArea = false;
            return;
        }

        // 窓部品そのものを広げる（右端のボタン類は右アンカーなら一緒に動く）。
        this.WidenNode((AtkResNode*)frame);

        // 窓部品の中の、見えているナイングリッド（背景・タイトルバー）を広げる。いちばん背の高いものを本体背景とみなす。
        var manager = &frame->Component->UldManager;
        AtkResNode* body = null;
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || node->Type != NodeType.NineGrid || !node->IsVisible())
            {
                continue;
            }

            this.WidenNode(node);
            if (body == null || node->Height > body->Height)
            {
                body = node;
            }
        }

        if (body == null)
        {
            this.hasArea = false;
            return;
        }

        this.ComputeArea(addon, body);
    }

    private void WidenNode(AtkResNode* node)
    {
        var key = (nint)node;
        if (!this.originalWidths.TryGetValue(key, out var original))
        {
            original = node->Width;
            this.originalWidths[key] = original;
        }

        var target = (ushort)(original + ExtraWidth);
        if (node->Width != target)
        {
            node->Width = target;
        }
    }

    private void ComputeArea(AtkUnitBase* addon, AtkResNode* body)
    {
        var scale = addon->Scale;
        var sx = body->ScaleX * scale;
        var sy = body->ScaleY * scale;
        var original = this.originalWidths.TryGetValue((nint)body, out var w) ? w : body->Width;

        // 本体背景の、元の右端（＝広げてできた空き領域の左端）から、広げた分だけを使う。
        var left = body->ScreenX + (original * sx);
        var top = body->ScreenY;
        var regionWidth = ExtraWidth * sx;
        var regionHeight = body->Height * sy;

        // タイトルバーと枠のぶん、内側に寄せる。
        var titleBar = 28f * scale;
        var inset = 10f * scale;
        this.areaPosition = new Vector2(left + inset, top + titleBar);
        this.areaSize = new Vector2(Math.Max(0, regionWidth - (inset * 2)), Math.Max(0, regionHeight - titleBar - inset));
        this.hasArea = this.areaSize is { X: > 40, Y: > 40 };
    }

    private void Restore(AtkUnitBase* addon)
    {
        var frame = addon->WindowNode;
        if (frame != null)
        {
            this.RestoreNode((AtkResNode*)frame);
            if (frame->Component != null)
            {
                var manager = &frame->Component->UldManager;
                for (var i = 0; i < manager->NodeListCount; i++)
                {
                    var node = manager->NodeList[i];
                    if (node != null)
                    {
                        this.RestoreNode(node);
                    }
                }
            }
        }
    }

    private void RestoreNode(AtkResNode* node)
    {
        if (this.originalWidths.TryGetValue((nint)node, out var original))
        {
            node->Width = (ushort)original;
        }
    }
}
