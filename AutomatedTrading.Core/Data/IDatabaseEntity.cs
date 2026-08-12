namespace AutomatedTrading.Core.Data;

public interface IDatabaseEntity
{
    string TableName { get; }
    string? SchemaName { get; }
    long Id { get; set; }
}