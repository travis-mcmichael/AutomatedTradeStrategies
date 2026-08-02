CREATE TABLE IF NOT EXISTS ModelFeature (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Parent model
    ModelConfigId INTEGER NOT NULL REFERENCES ModelConfig (Id) ON DELETE CASCADE,

    -- Feature dispatch
    Name TEXT NOT NULL,                   -- e.g. 'AtrVsOpen', 'Return', 'Rsi' — dispatched by FeatureComputations
    Source TEXT CHECK (Source IS NULL OR Source IN ('prevMinute', 'currMinute')),
                                           -- NULL = 'daily' (default source)
    SortOrder INTEGER NOT NULL            -- preserves feature/column ordering from models.json
);
