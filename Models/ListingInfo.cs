namespace PartySieve.Models;

/// <summary>
/// Dalamud の募集データから、判定と表示に必要なものだけを抜き出したもの。
/// Dalamud の型に依存しないので、テストから直接作れる。
/// </summary>
public sealed record ListingInfo
{
    public ulong Id { get; init; }

    public ulong ContentId { get; init; }

    public string HostName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string DutyName { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public string HomeWorld { get; init; } = string.Empty;

    public string HomeDataCenter { get; init; } = string.Empty;

    public string CurrentWorld { get; init; } = string.Empty;

    public int MinItemLevel { get; init; }

    public int SlotsFilled { get; init; }

    public int SlotsTotal { get; init; }

    public int SecondsRemaining { get; init; }
}
