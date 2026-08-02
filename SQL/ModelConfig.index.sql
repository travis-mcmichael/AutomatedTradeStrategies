CREATE INDEX IF NOT EXISTS idx_modelconfig_active_export
ON ModelConfig (Active, Export);

CREATE INDEX IF NOT EXISTS idx_modelconfig_instrument
ON ModelConfig (Instrument);
