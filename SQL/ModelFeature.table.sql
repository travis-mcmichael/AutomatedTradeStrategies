CREATE TABLE IF NOT EXISTS ModelFeature (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Parent model
    ModelConfigId INTEGER NOT NULL REFERENCES ModelConfig (Id) ON DELETE CASCADE,
    FeatureTypeId INTEGER NOT NULL REFERENCES FeatureType (Id),

    -- Feature dispatch
    Source TEXT,
                                           -- NULL = 'daily' (default source)
    SortOrder INTEGER NOT NULL,           -- preserves feature/column ordering from models.json
    ModelFeatureName TEXT                 -- optional display name; NULL when not set
);
