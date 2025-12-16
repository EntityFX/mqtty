using System.Text;

public static class PrettyPrinterHelper
{
    public record Column(string Title, int Size = 10, sbyte AlignHeader = -1, sbyte AlignItem = -1, string? Format = null, bool DoubleColumnLine = false);

    public record Item(string FieldName, object? Value);

    public record Row(Item[] Items);

    public record Columns(Dictionary<string, Column> Items, bool DoubleRowLine = false);

    public record Header(Columns MainHeader, Columns? BeforeHeader = null, Columns? AfterHeader = null);

    private static string AlignString(string str, int size, sbyte align)
    {
        var cutStr = str.Length > size ?
            str.Substring(0, size) : str;

        var result = align switch
        {
            -1 => cutStr
                .PadLeft(size),
            0 => cutStr
                .PadLeft((size + cutStr.Length) / 2)
                .PadRight(size),
            1 => cutStr
                .PadRight(size),
            _ => throw new NotImplementedException(),
        };

        return result;
    }

    public static string PrintAsTable(Header header, Row[] rows)
    {
        var sb = new StringBuilder();

        var printLine = true;

        if (header.BeforeHeader != null)
        {
            PrintTableLine(header.BeforeHeader.Items, sb);
            PringHeader(header.BeforeHeader.Items, sb);
            printLine = true;
        }

        if (printLine)
        {
            PrintTableLine(header.MainHeader.Items, sb);
        }

        PringHeader(header.MainHeader.Items, sb);
        PrintTableLine(header.MainHeader.Items, sb);

        if (header.AfterHeader != null)
        {
            PringHeader(header.AfterHeader.Items, sb);
            PrintTableLine(header.AfterHeader.Items, sb);
        }

        PrintRos(header.MainHeader.Items, rows, sb);

        PrintTableLine(header.MainHeader.Items, sb);

        return sb.ToString();
    }

    private static void PrintRos(Dictionary<string, Column> columns, Row[] rows, StringBuilder sb)
    {
        foreach (var row in rows)
        {
            sb.Append($"|");
            foreach (var field in row.Items)
            {
                var columnSetting = columns[field.FieldName];
                var valueFormat = $"{{0{(columnSetting.Format != null ? $":{columnSetting.Format}" : "")}}}";
                var fmtString = string.Format(valueFormat, field.Value);
                fmtString = AlignString(fmtString, columnSetting.Size, columnSetting.AlignItem);
                sb.Append($" {fmtString} {(columnSetting.DoubleColumnLine ? "||" : "|")}");
            }
            sb.AppendLine();
        }
    }

    private static void PringHeader(Dictionary<string, Column> columns, StringBuilder sb)
    {
        sb.Append($"|");
        foreach (var columnSetting in columns)
        {
            var header = AlignString(columnSetting.Value.Title, columnSetting.Value.Size, columnSetting.Value.AlignHeader);
            sb.Append($" {header} {(columnSetting.Value.DoubleColumnLine ? "||" : "|")}");
        }
        sb.AppendLine();
    }

    private static void PrintTableLine(Dictionary<string, Column> columnSettings, StringBuilder sb)
    {
        sb.Append($"+");
        foreach (var columnSetting in columnSettings)
        {
            sb.Append($"{new string('-', columnSetting.Value.Size + 2)}");
            sb.Append(columnSetting.Value.DoubleColumnLine ? "++" : "+");
        }
        sb.AppendLine();
    }
}



//Console.WriteLine(networkSimulator.Counters.PrintCounters());

