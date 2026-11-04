using System.Collections.Generic;
using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

// One row per trained version of a model. Written by Scripts/train_model_xgboost.py, which also writes the
// per-split regression metrics to ModelPerformanceSplit (no entity class; nothing in C# reads those yet).
public class ModelPerformance : IDatabaseEntity
{
    public string TableName { get; } = "ModelPerformance";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public long ModelConfigId { get; set; }
    public string Version { get; set; } = default!;
    public string TrainedAt { get; set; } = default!;

    // Training
    public long BestIteration { get; set; }
    public double BestValidationScore { get; set; }

    // Target distribution
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
    public double Std { get; set; }

    // Feature importance (JSON string)
    public string TopFeatureImportance { get; set; } = default!;

    // Regression metrics per split (Train, Validation, Test), in insertion order.
    public List<ModelPerformanceSplit> Splits { get; set; } = [];
}
