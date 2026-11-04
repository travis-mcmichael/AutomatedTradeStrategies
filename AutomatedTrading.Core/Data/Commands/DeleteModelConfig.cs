using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data.Commands;

// Deletes a ModelConfig and everything that hangs off it, children first, in one transaction:
// ModelFeatureParams -> ModelFeatures -> ModelPerformanceSplit -> ModelPerformance -> ModelConfig. Done explicitly because SQLite
// only honours the schema's ON DELETE CASCADE when PRAGMA foreign_keys is on, which it isn't here.
// Returns false (and changes nothing) if the model doesn't exist. Files on disk are left alone.
public class DeleteModelConfig(IDbConnectionFactory connectionFactory, ILogger<DeleteModelConfig> logger)
{
    private static readonly string[] statements =
    [
        "DELETE FROM ModelFeatureParam WHERE ModelFeatureId IN (SELECT Id FROM ModelFeature WHERE ModelConfigId = @ModelConfigId)",
        "DELETE FROM ModelFeature WHERE ModelConfigId = @ModelConfigId",
        "DELETE FROM ModelPerformanceSplit WHERE ModelPerformanceId IN (SELECT Id FROM ModelPerformance WHERE ModelConfigId = @ModelConfigId)",
        "DELETE FROM ModelPerformance WHERE ModelConfigId = @ModelConfigId",
        "DELETE FROM ModelConfig WHERE Id = @ModelConfigId"
    ];

    public async Task<bool> Execute(long modelConfigId)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var affected = new int[statements.Length];
        for (var i = 0; i < statements.Length; i++)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = statements[i];
            SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);
            affected[i] = await cmd.ExecuteNonQueryAsync();
        }

        if (affected[statements.Length - 1] == 0)
        {
            transaction.Rollback();
            return false;
        }

        transaction.Commit();
        logger.LogInformation(
            "Deleted ModelConfigId {ModelConfigId}: {Params} feature params, {Features} features, {Splits} performance splits, {Performance} performance rows",
            modelConfigId, affected[0], affected[1], affected[2], affected[3]);
        return true;
    }
}
