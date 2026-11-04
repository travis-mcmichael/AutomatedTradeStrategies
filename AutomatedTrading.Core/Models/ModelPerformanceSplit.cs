using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

// Regression metrics for one chronological split (Train/Validation/Test) of a ModelPerformance row.
// Written by Scripts/train_model_xgboost.py. The ATR shares are fractions from 0 to 1.
public class ModelPerformanceSplit : IDatabaseEntity
{
    public string TableName { get; } = "ModelPerformanceSplit";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public long ModelPerformanceId { get; set; }
    public string SplitName { get; set; } = default!;
    public long RowCount { get; set; }
    public double MAE { get; set; }
    public double RMSE { get; set; }
    public double? RSquared { get; set; }
    public double ATRLessThanTenPercentOver { get; set; }
    public double ATRLessThanTenPercentUnder { get; set; }
    public double ATRLessThan25PercentOver { get; set; }
    public double ATRLessThan25PercentUnder { get; set; }
}
