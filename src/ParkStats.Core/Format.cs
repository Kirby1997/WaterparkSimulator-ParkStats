using System.Globalization;
using System.Text;

namespace ParkStats.Core;

public static class Format
{
    public const string Unknown = "n/a";

    public static string Money(double? amount)
    {
        if (amount is null) return Unknown;
        var rounded = Math.Round(amount.Value, MidpointRounding.AwayFromZero);
        // Avoid "-0" for small negative amounts.
        if (rounded == 0) return "0";
        return rounded.ToString("#,0", CultureInfo.InvariantCulture);
    }

    public static string Signed(double? amount)
    {
        var text = Money(amount);
        return amount > 0 && text != "0" ? "+" + text : text;
    }

    public static string Percent(double? fraction)
    {
        if (fraction is null) return Unknown;
        var percent = Math.Round(fraction.Value * 100, MidpointRounding.AwayFromZero);
        return percent.ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    public static string Multiplier(double? value) =>
        value is null ? Unknown : "x" + value.Value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Ratio(int? current, int? max) => $"{Count(current)}/{Count(max)}";

    public static string Count(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? Unknown;

    /// <summary>Turns a game enum name such as "StaffSalary" into "Staff salary".</summary>
    public static string Reason(string reason)
    {
        // The game's MoneyChangeReason enum misspells this one.
        if (reason == "Maintance") return "Maintenance";

        var label = new StringBuilder(reason.Length + 4);
        for (var i = 0; i < reason.Length; i++)
        {
            var c = reason[i];
            if (i > 0 && char.IsUpper(c))
            {
                label.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                label.Append(c);
            }
        }
        return label.ToString();
    }
}
