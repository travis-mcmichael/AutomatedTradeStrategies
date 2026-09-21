using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class InstrumentVersion : IDatabaseEntity
{

    public long Id { get; set; }
    public long InstrumentId { get; set; }
    public string Version { get; set; }

    public string TableName => "InstrumentVersion";

    public string? SchemaName => "dbo";
}