namespace AutomatedTrading.Core.Data;

public interface IDatabaseEntity
{
    string TableName { get; set; }
    string? SchemaName { get; set; }
    long Id { get; set; }
}