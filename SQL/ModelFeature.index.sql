CREATE INDEX IF NOT EXISTS idx_modelfeature_modelconfig
ON ModelFeature (ModelConfigId, SortOrder);

CREATE INDEX IF NOT EXISTS idx_modelfeature_name
ON ModelFeature (Name);
