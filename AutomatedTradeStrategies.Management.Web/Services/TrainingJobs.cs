using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTradeStrategies.Management.Web.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrainingState { Running, Succeeded, Failed }

public record TrainingStatus(
    long ModelConfigId,
    TrainingState State,
    string Message,
    DateTime StartedAt,
    DateTime? FinishedAt,
    IReadOnlyList<string> Output);

// Runs Scripts/mlpipeline.sh in the background for a model (training can take minutes) and tracks the latest
// status per model in memory so the page can poll it. At most one training runs per model.
public class TrainingJobs(IWebHostEnvironment environment, ILogger<TrainingJobs> logger)
{
    private const int OUTPUT_LINES_KEPT = 40;
    private readonly ConcurrentDictionary<long, TrainingStatus> jobs = new();

    public TrainingStatus? GetStatus(long modelConfigId) =>
        jobs.TryGetValue(modelConfigId, out var status) ? status : null;

    // Starts training unless one is already running for the model; returns the current status either way.
    public TrainingStatus Start(ModelConfig model)
    {
        var starting = new TrainingStatus(model.Id, TrainingState.Running, "Starting training...", DateTime.Now, null, []);
        var status = jobs.AddOrUpdate(model.Id, starting,
            (_, existing) => existing.State == TrainingState.Running ? existing : starting);

        if (ReferenceEquals(status, starting))
            _ = Task.Run(() => run(model, starting));

        return status;
    }

    private async Task run(ModelConfig model, TrainingStatus job)
    {
        var output = new Queue<string>();
        var outputLock = new object();

        void addLine(string line)
        {
            lock (outputLock)
            {
                output.Enqueue(line);
                while (output.Count > OUTPUT_LINES_KEPT)
                    output.Dequeue();
            }
        }

        void update(TrainingState state, string message)
        {
            IReadOnlyList<string> lines;
            lock (outputLock)
                lines = output.ToArray();

            jobs[model.Id] = job with
            {
                State = state,
                Message = message,
                FinishedAt = state == TrainingState.Running ? null : DateTime.Now,
                Output = lines
            };
        }

        try
        {
            var scriptsDirectory = Path.Combine(environment.ContentRootPath, "Scripts");
            var script = Path.Combine(scriptsDirectory, "mlpipeline.sh");
            if (!File.Exists(script))
                throw new FileNotFoundException($"{script} was not found. Check that the Scripts folder was published.");

            var startInfo = new ProcessStartInfo("/bin/bash")
            {
                WorkingDirectory = scriptsDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add("--name");
            startInfo.ArgumentList.Add(model.ModelName);
            startInfo.ArgumentList.Add("--feature-dir");
            startInfo.ArgumentList.Add(Operations.NormalizePath(model.FeatureLocation));
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(model.Target);
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(model.Version);

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) addLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) addLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                update(TrainingState.Succeeded, $"Training finished for {model.ModelName}, version {model.Version}.");
                logger.LogInformation("Training for ModelConfigId {ModelConfigId} ({Version}) finished", model.Id, model.Version);
            }
            else
            {
                update(TrainingState.Failed, $"mlpipeline.sh exited with code {process.ExitCode}.");
                logger.LogError("Training for ModelConfigId {ModelConfigId} failed with exit code {ExitCode}", model.Id, process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Training failed for ModelConfigId {ModelConfigId}", model.Id);
            update(TrainingState.Failed, ex.Message);
        }
    }
}
