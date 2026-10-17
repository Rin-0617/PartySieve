using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;
using PartySieve.Filtering;
using PartySieve.Models;

namespace PartySieve.Services;

public sealed class ListingEntry
{
    public required ListingInfo Info { get; set; }

    public DateTime FirstSeen { get; init; }

    public DateTime LastSeen { get; set; }

    public int Batch { get; set; }

    public FilterResult Result { get; set; } = FilterResult.Visible;
}

/// <summary>
/// 受信した募集を判定して隠し、一覧ウィンドウ用に覚えておく。
/// </summary>
public sealed class ListingMonitor : IDisposable
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(15);

    private readonly Configuration configuration;
    private readonly IPartyFinderGui partyFinderGui;
    private readonly IPluginLog log;
    private readonly ListingFilter filter = new();
    private readonly Dictionary<ulong, ListingEntry> entries = [];
    private readonly Lock gate = new();
    private DateTime lastPrune = DateTime.MinValue;

    public ListingMonitor(Configuration configuration, IPartyFinderGui partyFinderGui, IPluginLog log)
    {
        this.configuration = configuration;
        this.partyFinderGui = partyFinderGui;
        this.log = log;
        this.partyFinderGui.ReceiveListing += this.OnReceiveListing;
    }

    public int LatestBatch { get; private set; }

    /// <summary>記録が変わるたびに増える。表示側のキャッシュの更新判定に使う。</summary>
    public int Version { get; private set; }

    public ListingFilter Filter => this.filter;

    public void Dispose()
    {
        this.partyFinderGui.ReceiveListing -= this.OnReceiveListing;
    }

    public List<ListingEntry> Snapshot()
    {
        lock (this.gate)
        {
            return [.. this.entries.Values];
        }
    }

    /// <summary>設定を変えたあと、覚えている募集の判定をやり直す。ゲーム側の一覧は次の更新で反映される。</summary>
    public void Reevaluate()
    {
        var now = DateTime.UtcNow;
        lock (this.gate)
        {
            foreach (var entry in this.entries.Values)
            {
                entry.Result = this.filter.Evaluate(entry.Info, this.configuration.Rules, this.configuration.Spam, now, this.configuration.ExcludedDataCenters);
            }

            this.Version++;
        }
    }

    public void Clear()
    {
        lock (this.gate)
        {
            this.entries.Clear();
            this.filter.Spam.Clear();
            this.Version++;
        }
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        try
        {
            var info = ToInfo(listing);
            var now = DateTime.UtcNow;
            FilterResult result;
            lock (this.gate)
            {
                this.PruneIfNeeded(now);
                this.filter.Spam.Observe(info, this.configuration.Spam, now);
                result = this.filter.Evaluate(info, this.configuration.Rules, this.configuration.Spam, now, this.configuration.ExcludedDataCenters);

                if (!this.entries.TryGetValue(info.Id, out var entry))
                {
                    entry = new ListingEntry { Info = info, FirstSeen = now };
                    this.entries[info.Id] = entry;
                }

                entry.Info = info;
                entry.LastSeen = now;
                entry.Batch = args.BatchNumber;
                entry.Result = result;
                this.LatestBatch = args.BatchNumber;
                this.Version++;
            }

            if (result.Hidden && this.configuration.HideMatches)
            {
                args.Visible = false;
            }
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "募集の判定に失敗しました。");
        }
    }

    private void PruneIfNeeded(DateTime now)
    {
        if (now - this.lastPrune < TimeSpan.FromMinutes(1))
        {
            return;
        }

        this.lastPrune = now;
        foreach (var (id, entry) in this.entries.ToList())
        {
            if (now - entry.LastSeen > EntryLifetime)
            {
                this.entries.Remove(id);
            }
        }

        this.filter.Spam.Prune(this.configuration.Spam, now);
    }

    private static ListingInfo ToInfo(IPartyFinderListing listing)
    {
        var homeWorld = listing.HomeWorld.ValueNullable;
        var homeWorldName = homeWorld?.Name.ExtractText() ?? string.Empty;
        var dutyName = listing.DutyType == DutyType.Normal
            ? listing.Duty.ValueNullable?.Name.ExtractText() ?? string.Empty
            : string.Empty;

        return new ListingInfo
        {
            Id = listing.Id,
            ContentId = listing.ContentId,
            HostName = listing.Name.TextValue,
            Description = listing.Description.TextValue,
            DutyName = string.IsNullOrEmpty(dutyName) ? listing.Category.ToString() : dutyName,
            CategoryName = listing.Category.ToString(),
            HomeWorld = homeWorldName,
            HomeDataCenter = DataCenterPalette.DataCenterOfWorld(homeWorldName)
                ?? homeWorld?.DataCenter.ValueNullable?.Name.ExtractText()
                ?? string.Empty,
            CurrentWorld = listing.CurrentWorld.ValueNullable?.Name.ExtractText() ?? string.Empty,
            MinItemLevel = listing.MinimumItemLevel,
            SlotsFilled = listing.SlotsFilled,
            SlotsTotal = listing.SlotsAvailable,
            SecondsRemaining = listing.SecondsRemaining,
        };
    }
}
