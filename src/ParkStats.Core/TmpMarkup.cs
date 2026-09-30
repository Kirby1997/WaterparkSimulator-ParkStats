using System.Text;

namespace ParkStats.Core;

/// <summary>Renders rows as one TextMeshPro rich-text string.</summary>
public static class TmpMarkup
{
    // Where each cell after the first starts, in percent of the line width, by cell count.
    private static readonly Dictionary<int, int[]> Columns = new()
    {
        [2] = new[] { 62 },
        [3] = new[] { 50, 75 },
        [4] = new[] { 25, 50, 75 },
        [7] = new[] { 32, 46, 56, 68, 79, 91 },
    };

    public static string Render(IReadOnlyList<Row> rows)
    {
        var text = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i > 0)
            {
                text.Append('\n');
                if (row.Kind == RowKind.Header) text.Append('\n');
            }

            var (open, close) = Wrap(row.Kind);
            text.Append(open);
            for (var cell = 0; cell < row.Cells.Count; cell++)
            {
                if (cell > 0) text.Append("<pos=").Append(ColumnStart(row.Cells.Count, cell)).Append("%>");
                text.Append(Plain(row.Cells[cell]));
            }
            text.Append(close);
        }
        return text.ToString();
    }

    private static int ColumnStart(int cellCount, int cell) =>
        Columns.TryGetValue(cellCount, out var starts) ? starts[cell - 1] : cell * 100 / cellCount;

    private static (string Open, string Close) Wrap(RowKind kind) => kind switch
    {
        RowKind.Header => ("<color=#FFD84A><b>", "</b></color>"),
        RowKind.Good => ("<color=#7CF29A>", "</color>"),
        RowKind.Bad => ("<color=#FF8A8A>", "</color>"),
        RowKind.Muted => ("<color=#A9C4D6>", "</color>"),
        _ => ("", ""),
    };

    // Names come from the game and other players; without brackets they cannot form tags.
    private static string Plain(string cell) => cell.Replace("<", "").Replace(">", "");
}
