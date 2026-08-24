using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class ModelPerformance : IDatabaseEntity
{
    public string TableName { get; } = "ModelPerformance";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public long ModelConfigId { get; set; }

    // Target distribution
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
    public double Std { get; set; }

    // Dataset splits
    public double Train { get; set; }
    public double Validation { get; set; }
    public double Test { get; set; }

    // Regression metrics
    public double MAE { get; set; }
    public double RMSE { get; set; }
    public double RSquared { get; set; }
    public double ATRLessThanTenPercentOver { get; set; }
    public double ATRLessThanTenPercentUnder { get; set; }
    public double ATRLessThan25PercentOver { get; set; }
    public double ATRLessThan25PercentUnder { get; set; }

    // Classification metrics
    public double Accuracy { get; set; }
    public double MeanAbsoluteError { get; set; }
    public double RootMeanSquaredError { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }

    // Feature importance (JSON string)
    public string TopFeatureImportance { get; set; } = default!;
}
