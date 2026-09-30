using System.Text;
using System.Text.RegularExpressions;

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
        [7] = new[] { 34, 48, 57, 68, 79, 91 },
    };

    // Tables this wide only fit in smaller text.
    private const int WideTable = 7;
    private const string SmallOpen = "<size=80%>";
    private const string SmallClose = "</size>";

    public static string Render(IReadOnlyList<Row> rows)
    {
        var text = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i > 0)
            {
                text.Append('\n');
                // A gap above each header, unless it follows a blank row or sits under another header.
                var previous = rows[i - 1];
                if (row.Kind == RowKind.Header && !IsBlank(previous) && previous.Kind != RowKind.Header) text.Append('\n');
            }

            var (open, close) = Wrap(row.Kind);
            var small = row.Cells.Count >= WideTable;
            if (small) text.Append(SmallOpen);
            text.Append(open);
            for (var cell = 0; cell < row.Cells.Count; cell++)
            {
                if (cell > 0) text.Append("<pos=").Append(ColumnStart(row.Cells.Count, cell)).Append("%>");
                text.Append(Plain(row.Cells[cell]));
            }
            text.Append(close);
            if (small) text.Append(SmallClose);
        }
        return text.ToString();
    }

    private static bool IsBlank(Row row) => row.Cells.All(string.IsNullOrEmpty);

    private static int ColumnStart(int cellCount, int cell) =>
        Columns.TryGetValue(cellCount, out var starts) ? starts[cell - 1] : cell * 100 / cellCount;

    private static (string Open, string Close) Wrap(RowKind kind) => kind switch
    {
        RowKind.Header => ("<color=#F2A900><b>", "</b></color>"),
        RowKind.Good => ("<color=#1FAF5A>", "</color>"),
        RowKind.Bad => ("<color=#E0433F>", "</color>"),
        _ => ("", ""),
    };

    private static readonly Regex Tag = new("<[^<>]*>", RegexOptions.Compiled);

    // Text comes from the game and from other players. Its own tags are dropped, and no
    // bracket is left that could start one.
    private static string Plain(string cell) => Tag.Replace(cell, "").Replace("<", "").Replace(">", "");
}
