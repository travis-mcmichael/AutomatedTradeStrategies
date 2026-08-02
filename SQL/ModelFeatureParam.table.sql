CREATE TABLE IF NOT EXISTS ModelFeatureParam (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Parent feature
    ModelFeatureId INTEGER NOT NULL REFERENCES ModelFeature (Id) ON DELETE CASCADE,

    -- Param key/value (e.g. 'period', 'lookback', 'fast', 'slow')
    ParamKey TEXT NOT NULL,
    ParamValue INTEGER NOT NULL,

    UNIQUE (ModelFeatureId, ParamKey)
);
