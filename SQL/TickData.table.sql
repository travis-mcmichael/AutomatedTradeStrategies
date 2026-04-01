CREATE TABLE TickData (
    Id INTEGER PRIMARY KEY,
    Instrument TEXT NOT NULL,
    TickTime INTEGER NOT NULL,        -- Unix epoch milliseconds
    LastPrice REAL NOT NULL,
    Volume INTEGER NOT NULL,
    Bid REAL,
    Ask REAL,
    SessionDate TEXT,
    SessionTime TEXT,

    UNIQUE (Instrument, TickTime)
);