using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data.Commands;

public class DeleteModelParameters(IDbConnectionFactory connectionFactory, ILogger<DeleteModelParameters> logger)
{
    public async Task<int> Execute(ModelConfig modelConfig)
    {
        var paramTable = new ModelFeatureParam().TableName;
        var featureTable = new ModelFeature().TableName;

        // Matches on the model's existing features in the DB (not the request payload's
        // ModelFeature.Id values, which are unreliable — new/edited features from callers
        // typically don't carry a real DB id) so every stale param row actually gets cleared.
        var sql = $"DELETE FROM {paramTable} WHERE ModelFeatureId IN " +
            $"(SELECT Id FROM {featureTable} WHERE ModelConfigId = @ModelConfigId)";

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfig.Id);

        var affected = await cmd.ExecuteNonQueryAsync();
        logger.LogInformation("Deleted {Count} model feature parameters for ModelConfigId {ModelConfigId}", affected, modelConfig.Id);
        return affected;
    }
}