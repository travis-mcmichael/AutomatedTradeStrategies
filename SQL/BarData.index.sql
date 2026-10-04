CREATE INDEX IF NOT EXISTS idx_bardata_timestamp
ON BarData (Timestamp);

CREATE INDEX IF NOT EXISTS idx_bardata_instrument_time
ON BarData (Instrument, Timestamp);

CREATE INDEX IF NOT EXISTS idx_bardata_session
ON BarData (SessionDate);