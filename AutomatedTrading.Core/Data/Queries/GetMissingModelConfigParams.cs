using System.Collections.Generic;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Queries;

// For each ModelFeature on the given ModelConfig, finds the ParameterKeys its FeatureType defines
// (FeatureTypeParam) that have no ModelFeatureParam row. Keyed by ModelFeatureId; features with
// every param set are omitted.
public class GetMissingModelConfigParams(IDbConnectionFactory connectionFactory)
{
    public async Task<IDictionary<long, IList<string>>> Execute(long modelConfigId)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT mf.Id, ftp.ParameterKey
              FROM ModelFeature mf
              JOIN FeatureTypeParam ftp
                ON ftp.FeatureTypeId = mf.FeatureTypeId
             WHERE mf.ModelConfigId = @ModelConfigId
               AND NOT EXISTS (SELECT 1
                                 FROM ModelFeatureParam mfp
                                WHERE mfp.ModelFeatureId = mf.Id
                                  AND mfp.FeatureTypeParamId = ftp.Id)
             ORDER BY mf.SortOrder, ftp.ParameterKey";

        SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);

        await connection.OpenAsync();

        var missing = new Dictionary<long, IList<string>>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var modelFeatureId = reader.GetInt64(0);
            if (!missing.TryGetValue(modelFeatureId, out var keys))
            {
                keys = new List<string>();
                missing[modelFeatureId] = keys;
            }
            keys.Add(reader.GetString(1));
        }

        return missing;
    }
}
