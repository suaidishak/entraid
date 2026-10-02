using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Lss.EntraLoginTest.Ingestion;

/// <summary>
/// Parses the DDQ forecast XML returned by an LSS plant API:
/// &lt;PlantForecastIntervals&gt;&lt;PlantForecastIntervalNode&gt;…
/// Keeps raw element text so missing/invalid values can be reported later.
/// </summary>
public static class ForecastXmlParser
{
    public static ForecastFeed Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new FormatException("The forecast response is not valid XML.", exception);
        }

        var root = document.Root ?? throw new FormatException("The forecast response has no root element.");
        var intervals = new List<ForecastInterval>();
        var number = 0;

        foreach (var node in root.Elements().Where(element => element.Name.LocalName == "PlantForecastIntervalNode"))
        {
            number++;
            var startText = Value(node, "IntervalStartTime");
            var endText = Value(node, "IntervalEndTime");
            var lengthText = Value(node, "IntervalLength");
            var valueText = Value(node, "ForecastValue");
            intervals.Add(new ForecastInterval(
                number,
                startText,
                ParseDateTime(startText),
                ParseDateTime(endText),
                lengthText,
                int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : null,
                Value(node, "ForecastResultParameter"),
                valueText,
                ParseDecimal(valueText),
                Value(node, "ValueUnit")));
        }

        return new ForecastFeed(null, intervals);
    }

    private static string? Value(XElement node, string localName)
    {
        var element = node.Elements().FirstOrDefault(child => child.Name.LocalName == localName);
        if (element is null) return null;
        var text = element.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    internal static DateTime? ParseDateTime(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;

    internal static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
