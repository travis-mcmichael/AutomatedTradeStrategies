using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Queries;

// Checks whether a ModelConfig has everything needed to run an inference for a session date. It mirrors what the
// API reads at run time (InferenceService, StrategyEvaluator, FeatureComputations) so problems are reported up front:
//   - the model is active, has a version, and its artifacts exist on disk
//   - the session date is a trading day with enough bars
//   - ATR has its lookback: ATR_SPAN sessions of bars before the session date, plus the session itself
//   - each feature has every parameter its FeatureType defines, window features have start/end,
//     and the context session its calculation reads has bars (inside the window for window sources,
//     pre-market bars for currentMinute)
// Day-aggregated sources (sessionDay, windowDay) aggregate all available history, so only the context
// session and the ATR lookback are checked for them, not the full history.
public class GetInferenceReadiness(
    IDefaultRepository<ModelConfig> modelConfigs,
    IDefaultRepository<ModelFeature> modelFeatures,
    IDefaultRepository<ModelFeatureParam> modelFeatureParams,
    IDefaultRepository<FeatureType> featureTypes,
    IDefaultRepository<FeatureTypeParam> featureTypeParams,
    IDbConnectionFactory connectionFactory)
{
    // Matches ATR_SPAN in InferenceService: ATR(20) needs 21 bar sessions, the last being the session date.
    private const int ATR_LOOKBACK = 20;
    private const int MAX_DATES_IN_MESSAGE = 5;

    private record SessionCount(int Total, int PreMarket);

    public async Task<InferenceReadiness> Execute(long modelConfigId, DateTime sessionDate)
    {
        var result = new InferenceReadiness();
        var all = new Dictionary<string, object> { { "1", "1" } };

        var model = (await modelConfigs.GetDataAsync(new Dictionary<string, object> { { "Id", modelConfigId } })).FirstOrDefault();
        if (model is null)
        {
            result.Issues.Add($"ModelConfig {modelConfigId} does not exist.");
            return result;
        }

        if (!model.Active) result.Issues.Add("Model is not active, so the API will not load it.");
        if (!TradingCalendar.IsTradingDay(sessionDate)) result.Issues.Add($"{Fmt(sessionDate)} is not a trading day.");
        checkArtifacts(model, result);

        var features = (await modelFeatures.GetDataAsync(new Dictionary<string, object> { { "ModelConfigId", modelConfigId } }))
            .OrderBy(f => f.SortOrder)
            .ToList();
        if (features.Count == 0) result.Issues.Add("No features are configured.");

        var types = (await featureTypes.GetDataAsync(all)).ToDictionary(t => t.Id);
        var typeParams = (await featureTypeParams.GetDataAsync(all)).ToList();
        var typeParamsById = typeParams.ToDictionary(t => t.Id);
        var featureIds = features.Select(f => f.Id).ToHashSet();
        var featureParams = (await modelFeatureParams.GetDataAsync(all))
            .Where(p => featureIds.Contains(p.ModelFeatureId))
            .ToList();

        // Each feature's context session, as FeatureComputations.ParseFeatureComputation resolves it.
        var contexts = new List<(FeatureReadiness Feature, string Source, DateTime ContextDate, Dictionary<string, int> Params)>();
        foreach (var feature in features)
        {
            var label = feature.ModelFeatureName ?? $"Feature {feature.SortOrder}";
            var source = feature.Source ?? "sessionMinute";
            var typeName = types.TryGetValue(feature.FeatureTypeId, out var type) ? type.Name : "unknown type";
            var readiness = new FeatureReadiness(feature.Id, label, typeName, source);
            result.Features.Add(readiness);

            if (type is null)
            {
                readiness.Issues.Add($"Feature type {feature.FeatureTypeId} does not exist.");
                continue;
            }

            var ownParams = featureParams.Where(p => p.ModelFeatureId == feature.Id).ToList();
            foreach (var p in ownParams)
                p.FeatureTypeParam = typeParamsById.TryGetValue(p.FeatureTypeParamId, out var tp) ? tp : null;
            feature.ModelFeatureParams = ownParams;

            foreach (var typeParam in typeParams.Where(t => t.FeatureTypeId == type.Id))
            {
                if (ownParams.All(p => p.FeatureTypeParamId != typeParam.Id))
                    readiness.Issues.Add($"Parameter '{typeParam.ParameterKey}' is not set.");
            }

            var p2 = FeatureComputations.GetParams(feature);
            if (IsWindowSource(source) && (!p2.ContainsKey("start") || !p2.ContainsKey("end")))
                readiness.Issues.Add($"Source '{source}' needs 'start' and 'end' window parameters.");

            var contextDate = FeatureComputations.GetContextDate(sessionDate, p2);
            contexts.Add((readiness, source, contextDate, p2));
        }

        // ATR lookback sessions, oldest first, ending on the session date.
        var atrSessions = new List<DateTime> { sessionDate.Date };
        while (atrSessions.Count < ATR_LOOKBACK + 1)
            atrSessions.Insert(0, TradingCalendar.GetPreviousSessionDate(atrSessions[0]));

        var earliest = contexts.Select(c => c.ContextDate).Append(atrSessions[0]).Min();
        var from = Key(earliest);
        var to = Key(sessionDate);
        var counts = await loadSessionCounts(model.Instrument, from, to);

        var sessionCount = counts.TryGetValue(Key(sessionDate), out var found) ? found : new SessionCount(0, 0);
        if (sessionCount.Total < AppConstants.MINIMUM_BARS_TO_EVALUATE)
            result.Issues.Add($"{Fmt(sessionDate)} has {sessionCount.Total} bars; at least {AppConstants.MINIMUM_BARS_TO_EVALUATE} are needed.");

        var missingAtr = atrSessions.Where(d => !HasBars(counts, d)).ToList();
        if (missingAtr.Count > 0)
            result.Issues.Add($"ATR({ATR_LOOKBACK}) needs bars for {ATR_LOOKBACK + 1} sessions ending {Fmt(sessionDate)}; missing {Describe(missingAtr)}.");

        var windowCounts = new Dictionary<(int Start, int End), Dictionary<string, int>>();
        foreach (var (readiness, source, contextDate, p) in contexts)
        {
            if (!HasBars(counts, contextDate))
                readiness.Issues.Add($"No bars for context session {Fmt(contextDate)}.");

            if (IsWindowSource(source) && p.TryGetValue("start", out var start) && p.TryGetValue("end", out var end))
            {
                if (!windowCounts.TryGetValue((start, end), out var inWindow))
                {
                    inWindow = await loadWindowCounts(model.Instrument, from, to, start, end);
                    windowCounts[(start, end)] = inWindow;
                }
                if (!inWindow.TryGetValue(Key(contextDate), out var barsInWindow) || barsInWindow == 0)
                    readiness.Issues.Add($"No bars inside window {start:D6}-{end:D6} on {Fmt(contextDate)}.");
            }

            if (source == "currentMinute" && sessionCount.PreMarket == 0)
                readiness.Issues.Add($"No pre-market bars before {AppConstants.RTHSTART} on {Fmt(sessionDate)}.");
        }

        return result;
    }

    private static bool IsWindowSource(string source) => source is "windowMinute" or "windowDay";

    private static bool HasBars(Dictionary<string, SessionCount> counts, DateTime date) =>
        counts.TryGetValue(Key(date), out var c) && c.Total > 0;

    private static void checkArtifacts(ModelConfig model, InferenceReadiness result)
    {
        if (string.IsNullOrWhiteSpace(model.Version))
        {
            result.Issues.Add("No version is set; export and train the model first.");
            return;
        }

        var dir = Path.Combine(Operations.NormalizePath(model.OutputLocation), model.ModelName, model.Version);
        foreach (var file in new[] { AppConstants.XGB_MODEL_FILENAME, AppConstants.SCALER_FILENAME })
        {
            if (!File.Exists(Path.Combine(dir, file)))
                result.Issues.Add($"Missing {file} in {dir}.");
        }
    }

    // Per-session bar totals for the instrument over [from, to] (yyyyMMdd). A bar is pre-market when its
    // SessionTime is before the RTH start.
    private async Task<Dictionary<string, SessionCount>> loadSessionCounts(string instrument, string from, string to)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT SessionDate,
                   COUNT(*),
                   SUM(CASE WHEN CAST(SessionTime AS INTEGER) < @RthStart THEN 1 ELSE 0 END)
              FROM BarData
             WHERE Instrument = @Instrument
               AND SessionDate BETWEEN @From AND @To
             GROUP BY SessionDate";

        SqlHelper.AddParameter(cmd, "@Instrument", instrument);
        SqlHelper.AddParameter(cmd, "@From", from);
        SqlHelper.AddParameter(cmd, "@To", to);
        SqlHelper.AddParameter(cmd, "@RthStart", int.Parse(AppConstants.RTHSTART, CultureInfo.InvariantCulture));

        await connection.OpenAsync();

        var counts = new Dictionary<string, SessionCount>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = new SessionCount(reader.GetInt32(1), reader.GetInt32(2));
        }
        return counts;
    }

    // Per-session count of bars inside a TradeWindow-style window (HHmmss ints, wrapping past midnight when start > end).
    private async Task<Dictionary<string, int>> loadWindowCounts(string instrument, string from, string to, int start, int end)
    {
        const string time = "CAST(SessionTime AS INTEGER)";
        var inWindow = start <= end
            ? $"{time} >= @WindowStart AND {time} < @WindowEnd"
            : $"({time} >= @WindowStart OR {time} < @WindowEnd)";

        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT SessionDate, COUNT(*)
              FROM BarData
             WHERE Instrument = @Instrument
               AND SessionDate BETWEEN @From AND @To
               AND {inWindow}
             GROUP BY SessionDate";

        SqlHelper.AddParameter(cmd, "@Instrument", instrument);
        SqlHelper.AddParameter(cmd, "@From", from);
        SqlHelper.AddParameter(cmd, "@To", to);
        SqlHelper.AddParameter(cmd, "@WindowStart", start);
        SqlHelper.AddParameter(cmd, "@WindowEnd", end);

        await connection.OpenAsync();

        var counts = new Dictionary<string, int>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    private static string Key(DateTime date) => date.ToString(AppConstants.DATE_FORMAT, CultureInfo.InvariantCulture);

    private static string Fmt(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Describe(IList<DateTime> dates)
    {
        var shown = string.Join(", ", dates.Take(MAX_DATES_IN_MESSAGE).Select(Fmt));
        return dates.Count > MAX_DATES_IN_MESSAGE ? $"{shown} and {dates.Count - MAX_DATES_IN_MESSAGE} more" : shown;
    }
}
