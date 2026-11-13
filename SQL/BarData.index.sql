CREATE INDEX IF NOT EXISTS idx_bardata_timestamp
ON BarData (Timestamp);

CREATE INDEX IF NOT EXISTS idx_bardata_instrument_time
ON BarData (Instrument, Timestamp);

CREATE INDEX IF NOT EXISTS idx_bardata_session
ON BarData (SessionDate);

-- Lets GetVersionChangeovers' per-instrument daily totals and the roll adjustment lookups use an index
-- instead of scanning every bar for the instrument. Without it, a changeover lookup over one year took ~19s.
CREATE INDEX IF NOT EXISTS idx_bardata_instrument_session_version
ON BarData (Instrument, SessionDate, InstrumentVersion);
