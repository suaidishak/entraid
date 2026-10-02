using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lss.EntraLoginTest.Ingestion;

/// <summary>
/// Parses the MMF JSON returned by an LSS plant API:
/// { "MMFResponse": { "PlantName": "LSS-002", "MMFData": [ { "Parameter", "Time", "Unit", "MMF1"… } ] } }
/// </summary>
public static partial class MmfJsonParser
{
    [GeneratedRegex("^MMF(\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeriesPattern();

    public static MmfFeed Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The MMF response is not valid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!TryGetProperty(root, "MMFResponse", out var response) || response.ValueKind != JsonValueKind.Object)
                throw new FormatException("The MMF response is missing the MMFResponse object.");

            var plantName = TryGetProperty(response, "PlantName", out var name) ? name.GetString() : null;
            if (!TryGetProperty(response, "MMFData", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new FormatException("The MMF response is missing the MMFData array.");

            var readings = new List<MmfReading>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var parameter = (TryGetProperty(item, "Parameter", out var p) ? p.GetString() : null) ?? "(unknown)";
                var timeText = TryGetProperty(item, "Time", out var t) ? t.GetString() : null;
                var unit = TryGetProperty(item, "Unit", out var u) ? u.GetString() : null;

                foreach (var property in item.EnumerateObject())
                {
                    var match = SeriesPattern().Match(property.Name);
                    if (!match.Success) continue;
                    var seriesIndex = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    var valueText = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
                    readings.Add(new MmfReading(
                        parameter.Trim(),
                        string.IsNullOrWhiteSpace(timeText) ? null : timeText.Trim(),
                        ForecastXmlParser.ParseDateTime(timeText),
                        string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(),
                        seriesIndex,
                        valueText,
                        ForecastXmlParser.ParseDecimal(valueText)));
                }
            }

            return new MmfFeed(plantName, readings);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value)) return true;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        }
        value = default;
        return false;
    }
}
