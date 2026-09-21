using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class Instrument : IDatabaseEntity
{
    public long Id { get; set; }
    public string InstrumentName { get; set; }
    public string TableName => "Instrument";
    public string? SchemaName => "dbo";
}