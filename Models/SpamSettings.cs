namespace PartySieve.Models;

[Serializable]
public sealed class SpamSettings
{
    /// <summary>同じ文面を、別々の募集主が何人も出していたら隠す。</summary>
    public bool DuplicateEnabled { get; set; } = true;

    public int DuplicateHostThreshold { get; set; } = 3;

    public int DuplicateWindowMinutes { get; set; } = 30;

    /// <summary>重複とみなす文面の最短文字数（記号・空白を除く）。「よろしく」程度の短文は対象外にする。</summary>
    public int DuplicateMinLength { get; set; } = 12;

    /// <summary>記号だらけの募集文を隠す。</summary>
    public bool SymbolEnabled { get; set; } = true;

    public float SymbolRatio { get; set; } = 0.6f;

    public int SymbolMinLength { get; set; } = 12;

    /// <summary>同じ文字の長い連続（「ああああああああ」「★★★★★★★★」）を隠す。</summary>
    public bool RepeatEnabled { get; set; } = true;

    public int RepeatRunLength { get; set; } = 10;

    public void Normalize()
    {
        this.DuplicateHostThreshold = Math.Clamp(this.DuplicateHostThreshold, 2, 50);
        this.DuplicateWindowMinutes = Math.Clamp(this.DuplicateWindowMinutes, 1, 240);
        this.DuplicateMinLength = Math.Clamp(this.DuplicateMinLength, 1, 200);
        this.SymbolRatio = Math.Clamp(this.SymbolRatio, 0.1f, 1f);
        this.SymbolMinLength = Math.Clamp(this.SymbolMinLength, 1, 200);
        this.RepeatRunLength = Math.Clamp(this.RepeatRunLength, 3, 100);
    }
}
