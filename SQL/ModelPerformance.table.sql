CREATE TABLE IF NOT EXISTS ModelPerformance (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelConfigId INTEGER,
    -- Target distribution
    Min REAL NOT NULL,
    Max REAL NOT NULL,
    Mean REAL NOT NULL,
    Std REAL NOT NULL,

    -- Dataset splits
    Train REAL NOT NULL,
    Validation REAL NOT NULL,
    Test REAL NOT NULL,

    -- Regression metrics
    MAE REAL NOT NULL,
    RMSE REAL NOT NULL,
    RSquared REAL NOT NULL,
    ATRLessThanTenPercentOver REAL NOT NULL,
    ATRLessThanTenPercentUnder REAL NOT NULL,
    ATRLessThan25PercentOver REAL NOT NULL,
    ATRLessThan25PercentUnder REAL NOT NULL,

    -- Classification metrics
    Accuracy REAL NOT NULL,
    MeanAbsoluteError REAL NOT NULL,
    RootMeanSquaredError REAL NOT NULL,
    Precision REAL NOT NULL,
    Recall REAL NOT NULL,

    -- Feature importance (JSON string)
    TopFeatureImportance TEXT NOT NULL
);
