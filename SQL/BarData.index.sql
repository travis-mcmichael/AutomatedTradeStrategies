CREATE INDEX IF NOT EXISTS idx_bardata_timestamp
ON BarData (Timestamp);

CREATE INDEX IF NOT EXISTS idx_bardata_instrument_time
ON BarData (Instrument, Timestamp);

CREATE INDEX IF NOT EXISTS idx_bardata_session
ON BarData (SessionDate);

CREATE INDEX IF NOT EXISTS idx_bardata_session_key
ON BarData (SessionDate, SessionTime, Instrument, InstrumentVersion);