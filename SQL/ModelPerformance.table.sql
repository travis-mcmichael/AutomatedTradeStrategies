-- One row per trained model version. Replaces the earlier single-table layout: on a database that still has the
-- old ModelPerformance table, run DROP TABLE ModelPerformance first (IF NOT EXISTS would otherwise keep the old shape).
CREATE TABLE IF NOT EXISTS ModelPerformance (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelConfigId INTEGER NOT NULL REFERENCES ModelConfig (Id) ON DELETE CASCADE,
    Version TEXT NOT NULL,                -- YYYYMMDD, matches the ModelConfig version that was trained
    TrainedAt TEXT NOT NULL,              -- ISO 8601 local time when training finished

    -- Training
    BestIteration INTEGER NOT NULL,
    BestValidationScore REAL NOT NULL,    -- validation RMSE at BestIteration (reg:squarederror)

    -- Target distribution (full dataset)
    Min REAL NOT NULL,
    Max REAL NOT NULL,
    Mean REAL NOT NULL,
    Std REAL NOT NULL,

    -- Feature importance (JSON object: feature name → importance, top 15)
    TopFeatureImportance TEXT NOT NULL,

    UNIQUE (ModelConfigId, Version)
);

-- Regression metrics for each chronological split of a ModelPerformance row.
CREATE TABLE IF NOT EXISTS ModelPerformanceSplit (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelPerformanceId INTEGER NOT NULL REFERENCES ModelPerformance (Id) ON DELETE CASCADE,
    SplitName TEXT NOT NULL CHECK (SplitName IN ('Train', 'Validation', 'Test')),
    RowCount INTEGER NOT NULL,

    MAE REAL NOT NULL,
    RMSE REAL NOT NULL,
    RSquared REAL,                        -- NULL when R² is undefined (e.g. too few rows)

    -- Share of predictions that land within the band above (+) or below (-) the actual value, as a fraction 0-1
    ATRLessThanTenPercentOver REAL NOT NULL,
    ATRLessThanTenPercentUnder REAL NOT NULL,
    ATRLessThan25PercentOver REAL NOT NULL,
    ATRLessThan25PercentUnder REAL NOT NULL,

    UNIQUE (ModelPerformanceId, SplitName)
);
