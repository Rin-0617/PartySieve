using System.Numerics;

namespace PartySieve.Services;

/// <summary>日本の論理DCごとの表示色と、ワールドから論理DCへの対応。</summary>
public static class DataCenterPalette
{
    public static readonly Vector4 Unknown = new(0.65f, 0.65f, 0.65f, 1f);

    // 公式の名前の色（水色）と見分けやすい色にしている。
    private static readonly Dictionary<string, Vector4> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Elemental"] = new(0.45f, 0.95f, 0.40f, 1f),
        ["Gaia"] = new(1.00f, 0.78f, 0.25f, 1f),
        ["Mana"] = new(0.85f, 0.55f, 1.00f, 1f),
        ["Meteor"] = new(1.00f, 0.42f, 0.42f, 1f),
    };

    // 7.57 のリージョン内フリーマッチングで、ゲームデータ上は日本の全ワールドが Elemental 所属になった。
    // そのため論理DCは、ワールド名から自前の表で引く。
    private static readonly Dictionary<string, string> WorldDataCenters = BuildWorldTable(new()
    {
        ["Elemental"] = ["Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon"],
        ["Gaia"] = ["Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima"],
        ["Mana"] = ["Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan"],
        ["Meteor"] = ["Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus"],
    });

    /// <summary>日本の論理DC名（表示順）。</summary>
    public static readonly string[] JapaneseDataCenters = ["Elemental", "Gaia", "Mana", "Meteor"];

    public static bool TryGet(string dataCenter, out Vector4 color) => Colors.TryGetValue(dataCenter, out color);

    public static Vector4 Get(string dataCenter) => Colors.GetValueOrDefault(dataCenter, Unknown);

    /// <summary>日本のワールドなら論理DC名、それ以外は null。</summary>
    public static string? DataCenterOfWorld(string world) => WorldDataCenters.GetValueOrDefault(world);

    private static Dictionary<string, string> BuildWorldTable(Dictionary<string, string[]> groups)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dataCenter, worlds) in groups)
        {
            foreach (var world in worlds)
            {
                table[world] = dataCenter;
            }
        }

        return table;
    }
}
