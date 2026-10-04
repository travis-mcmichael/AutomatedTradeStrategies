using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;
using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTradeStrategies.Management.Web.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeatureExportState { Running, Succeeded, Failed }

public record FeatureExportStatus(
    long ModelConfigId,
    FeatureExportState State,
    string Message,
    DateTime StartedAt,
    DateTime? FinishedAt = null,
    int RowsWritten = 0);

// Runs FeatureExporter.GenerateFeatures in the background (an export can take minutes) and tracks the
// latest status per model in memory so the page can poll it. At most one export runs per model.
public class FeatureExportJobs(IServiceScopeFactory scopeFactory, ILogger<FeatureExportJobs> logger)
{
    private const string FEATURES_FILE = "features.csv";
    private readonly ConcurrentDictionary<long, FeatureExportStatus> jobs = new();

    public FeatureExportStatus? GetStatus(long modelConfigId) =>
        jobs.TryGetValue(modelConfigId, out var status) ? status : null;

    // Starts an export unless one is already running for the model; returns the current status either way.
    public FeatureExportStatus Start(long modelConfigId)
    {
        var starting = new FeatureExportStatus(modelConfigId, FeatureExportState.Running, "Starting export...", DateTime.Now);
        var status = jobs.AddOrUpdate(modelConfigId, starting,
            (_, existing) => existing.State == FeatureExportState.Running ? existing : starting);

        if (ReferenceEquals(status, starting))
            _ = Task.Run(() => run(starting));

        return status;
    }

    private async Task run(FeatureExportStatus job)
    {
        void update(FeatureExportState state, string message, int rows = 0) =>
            jobs[job.ModelConfigId] = job with
            {
                State = state,
                Message = message,
                RowsWritten = rows,
                FinishedAt = state == FeatureExportState.Running ? null : DateTime.Now
            };

        string? tempPath = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            update(FeatureExportState.Running, "Loading model configuration...");
            var model = (await services.GetRequiredService<GetAllModelConfigs>().Execute())
                .FirstOrDefault(m => m.Id == job.ModelConfigId)
                ?? throw new InvalidOperationException($"Model {job.ModelConfigId} was not found.");
            if (string.IsNullOrWhiteSpace(model.FeatureLocation))
                throw new InvalidOperationException($"{model.ModelName} has no Feature Location.");
            if (model.ModelFeatures.Count == 0)
                throw new InvalidOperationException($"{model.ModelName} has no features. Add them via Edit Features before exporting.");
            var missing = await services.GetRequiredService<GetMissingModelConfigParams>().Execute(model.Id);
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"{model.ModelName} has {missing.Count} feature(s) with missing parameter values. Set them via Edit Features before exporting.");

            var directory = Operations.NormalizePath(model.FeatureLocation);
            Directory.CreateDirectory(directory);
            var outputPath = Path.Combine(directory, FEATURES_FILE);
            // Written to a temp file and only moved into place on success, so a failed or empty export
            // never leaves a features.csv behind (which the grid would report as exported).
            tempPath = outputPath + ".tmp";

            var start = DateTime.ParseExact(model.TrainingStart, AppConstants.DATE_FORMAT, CultureInfo.InvariantCulture);
            var end = DateTime.ParseExact(model.TrainingEnd, AppConstants.DATE_FORMAT, CultureInfo.InvariantCulture);

            update(FeatureExportState.Running, $"Generating features for {model.ModelName} ({model.TrainingStart}–{model.TrainingEnd})...");
            var exporter = new FeatureExporter(model, tempPath,
                services.GetRequiredService<StreamBarsInRange>(),
                services.GetRequiredService<GetVersionChangeovers>());
            var rows = await exporter.GenerateFeatures(start, end);

            if (rows == 0)
                throw new InvalidOperationException(
                    $"No feature rows were generated for {model.ModelName}. Check that it has features configured and bar data between {model.TrainingStart} and {model.TrainingEnd}.");

            File.Move(tempPath, outputPath, overwrite: true);

            // A successful export stamps the model's Version with today's date.
            var version = DateTime.Today.ToString(AppConstants.DATE_FORMAT);
            await services.GetRequiredService<IDefaultUpdateCommand<ModelConfig>>().Execute(new { Version = version }, new { model.Id });

            update(FeatureExportState.Succeeded, $"{rows:N0} rows written to {outputPath}. Version set to {version}.", rows);
            logger.LogInformation("Feature export for ModelConfigId {ModelConfigId} wrote {Rows} rows to {Path}", job.ModelConfigId, rows, outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feature export failed for ModelConfigId {ModelConfigId}", job.ModelConfigId);
            if (tempPath is not null && File.Exists(tempPath))
                File.Delete(tempPath);
            update(FeatureExportState.Failed, ex.Message);
        }
    }
}
