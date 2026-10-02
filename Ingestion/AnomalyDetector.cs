namespace Lss.EntraLoginTest.Ingestion;

/// <summary>
/// Examines parsed feeds against the TOR anomaly rules: zeros, out of range,
/// data not available, and unusable values. Request/parse failures are recorded
/// by the ingestion service.
/// </summary>
public sealed class AnomalyDetector(IngestionOptions options)
{
    private static readonly int[] SupportedIntervals = [15, 30, 60];

    public IReadOnlyList<DataAnomaly> Inspect(ForecastFeed feed)
    {
        var anomalies = new List<DataAnomaly>();
        if (feed.Intervals.Count == 0)
        {
            anomalies.Add(new(AnomalyCodes.EmptyFeed, null, null, null, "The forecast feed contains no intervals."));
            return anomalies;
        }

        var anyValue = false;
        var allZero = true;

        foreach (var interval in feed.Intervals)
        {
            if (interval.StartLocal is null)
                anomalies.Add(new(AnomalyCodes.NullTimestamp, "IntervalStartTime", interval.Number, null,
                    $"Interval {interval.Number} has a missing or invalid start timestamp."));

            if (interval.Value is null)
            {
                anomalies.Add(new(AnomalyCodes.MissingValue, "ForecastValue", interval.Number, interval.StartLocal,
                    $"Interval {interval.Number} has a missing or invalid forecast value."));
            }
            else
            {
                anyValue = true;
                if (interval.Value != 0m) allZero = false;
                if (interval.Value < 0m)
                    anomalies.Add(new(AnomalyCodes.NegativeValue, "ForecastValue", interval.Number, interval.StartLocal,
                        $"Interval {interval.Number} has a negative forecast value ({interval.Value:0.###} {interval.Unit ?? "MW"})."));
                else if (interval.Value > options.MaxForecastMw)
                    anomalies.Add(new(AnomalyCodes.OutOfRange, "ForecastValue", interval.Number, interval.StartLocal,
                        $"Interval {interval.Number} exceeds the {options.MaxForecastMw:0.###} MW maximum."));
            }

            if (interval.LengthMinutes is null || !SupportedIntervals.Contains(interval.LengthMinutes.Value))
                anomalies.Add(new(AnomalyCodes.InvalidIntervalLength, "IntervalLength", interval.Number, interval.StartLocal,
                    $"Interval {interval.Number} has an unsupported interval length ('{interval.LengthText ?? "missing"}')."));
        }

        if (anyValue && allZero)
            anomalies.Add(new(AnomalyCodes.AllZero, "ForecastValue", null, null, "Every interval in this feed is zero."));

        return anomalies;
    }

    public IReadOnlyList<DataAnomaly> Inspect(MmfFeed feed)
    {
        var anomalies = new List<DataAnomaly>();
        if (feed.Readings.Count == 0)
        {
            anomalies.Add(new(AnomalyCodes.EmptyFeed, null, null, null, "The MMF feed contains no readings."));
            return anomalies;
        }

        foreach (var reading in feed.Readings)
        {
            if (reading.TimeLocal is null)
                anomalies.Add(new(AnomalyCodes.NullTimestamp, "Time", null, null,
                    $"{reading.Parameter} (series {reading.SeriesIndex}) has a missing or invalid timestamp."));

            if (reading.Value is null)
                anomalies.Add(new(AnomalyCodes.MissingValue, "MMF" + reading.SeriesIndex, null, reading.TimeLocal,
                    $"{reading.Parameter} (series {reading.SeriesIndex}) has a missing or invalid value."));
            else if (Math.Abs(reading.Value.Value) > options.MmfOutOfRangeAbs)
                anomalies.Add(new(AnomalyCodes.OutOfRange, "MMF" + reading.SeriesIndex, null, reading.TimeLocal,
                    $"{reading.Parameter} value {reading.Value:0.###} is outside the plausible range."));
        }

        return anomalies;
    }
}
