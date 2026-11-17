using System.Globalization;
using System.Text;

namespace Printify.Backend.Services;

public static class Csv
{
    // Quotes every text cell and neutralises spreadsheet formula injection (=, +, -, @).
    public static string Text(string value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string Num(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static byte[] Build(string[] headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Text)));
        foreach (var row in rows) sb.AppendLine(string.Join(",", row));
        return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }
}
