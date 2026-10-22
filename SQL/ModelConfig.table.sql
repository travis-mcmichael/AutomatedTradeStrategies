CREATE TABLE IF NOT EXISTS ModelConfig (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Model identity & version
    ModelName TEXT NOT NULL,              -- e.g. 'mes.session.high' — key in API prediction dictionary
    Version TEXT NOT NULL,                -- YYYYMMDD — subdirectory under Models/<ModelName>/

    -- Training scope
    Instrument TEXT NOT NULL,
    TrainingStart TEXT NOT NULL,          -- YYYYMMDD
    TrainingEnd TEXT NOT NULL,            -- YYYYMMDD

    -- Prediction target
    Target TEXT NOT NULL CHECK (Target IN ('HIGH', 'LOW', 'CLASS')),
    WindowStart TEXT,                     -- HHmmss (CT), NULL for session-level models
    WindowEnd TEXT,                       -- HHmmss (CT), NULL for session-level models

    -- Pipeline locations
    FeatureLocation TEXT NOT NULL,        -- base dir for features.csv (Python pipeline)
    OutputLocation TEXT NOT NULL,         -- base dir for trained model artifacts

    -- Pipeline flags
    Export INTEGER NOT NULL DEFAULT 0 CHECK (Export IN (0, 1)),   -- included in feature export / training
    Active INTEGER NOT NULL DEFAULT 0 CHECK (Active IN (0, 1)),   -- loaded and run by the API

    UNIQUE (ModelName)
);
