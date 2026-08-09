using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Models;

public class GetAllModelConfigsQuery : IDbQuery<List<ModelConfig>>
{
    private const string ConfigsSql = @"
        SELECT Id, ModelName, Version, Instrument, TrainingStart, TrainingEnd, Target, BarType,
               WindowStart, WindowEnd, FeatureLocation, OutputLocation, Export, Active
        FROM ModelConfig
        ORDER BY ModelName";

    private const string FeaturesSql = @"
        SELECT mf.Id, mf.ModelConfigId, ft.Name, mf.Source, mf.SortOrder
        FROM ModelFeature mf
        JOIN FeatureType ft ON ft.Id = mf.FeatureTypeId
        ORDER BY mf.ModelConfigId, mf.SortOrder";

    private const string ParamsSql = @"
        SELECT mfp.Id, mfp.ModelFeatureId, ftp.ParameterKey, mfp.ParamValue
        FROM ModelFeatureParam mfp
        JOIN FeatureTypeParam ftp ON ftp.Id = mfp.FeatureTypeParamId";

    public async Task<List<ModelConfig>> ExecuteAsync(DbConnection connection, CancellationToken ct = default)
    {
        var paramsByFeatureId = await loadParams(connection, ct);
        var featuresByModelId = await loadFeatures(connection, paramsByFeatureId, ct);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = ConfigsSql;

        var results = new List<ModelConfig>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);
            results.Add(new ModelConfig
            {
                Id = id,
                ModelName = reader.GetString(1),
                Version = reader.GetString(2),
                Instrument = reader.GetString(3),
                TrainingStart = reader.GetString(4),
                TrainingEnd = reader.GetString(5),
                Target = reader.GetString(6),
                BarType = reader.GetString(7),
                WindowStart = reader.IsDBNull(8) ? null : reader.GetString(8),
                WindowEnd = reader.IsDBNull(9) ? null : reader.GetString(9),
                FeatureLocation = reader.GetString(10),
                OutputLocation = reader.GetString(11),
                Export = reader.GetBoolean(12),
                Active = reader.GetBoolean(13),
                ModelFeatures = featuresByModelId.TryGetValue(id, out var features) ? features : new List<ModelFeature>()
            });
        }

        return results;
    }

    private static async Task<Dictionary<int, Dictionary<string, int>>> loadParams(DbConnection connection, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = ParamsSql;

        var byFeatureId = new Dictionary<int, Dictionary<string, int>>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var featureId = reader.GetInt32(1);
            if (!byFeatureId.TryGetValue(featureId, out var dict))
                byFeatureId[featureId] = dict = new Dictionary<string, int>();

            dict[reader.GetString(2)] = reader.GetInt32(3);
        }

        return byFeatureId;
    }

    private static async Task<Dictionary<int, List<ModelFeature>>> loadFeatures(
        DbConnection connection, Dictionary<int, Dictionary<string, int>> paramsByFeatureId, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = FeaturesSql;

        var byModelId = new Dictionary<int, List<ModelFeature>>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var featureId = reader.GetInt32(0);
            var modelId = reader.GetInt32(1);

            if (!byModelId.TryGetValue(modelId, out var list))
                byModelId[modelId] = list = new List<ModelFeature>();
                Dictionary<string, int> p;
            if (paramsByFeatureId.TryGetValue(featureId, out p))
            {
                var mfp = new List<ModelFeatureParam>();
                foreach (var entry in p) mfp.Add(
                    new ModelFeatureParam
                    {
                        ParamKey = entry.Key,
                        ParamValue = entry.Value
                    });
                
                list.Add(new ModelFeature 
                {
                    Id = featureId,
                    Name = reader.GetString(2),
                    Source = reader.IsDBNull(3) ? null : reader.GetString(3),
                    SortOrder = reader.GetInt32(4),
                    ModelFeatureParams = mfp,
                });
            } 
            else
            {
                list.Add(new ModelFeature 
                {
                    Id = featureId,
                    Name = reader.GetString(2),
                    Source = reader.IsDBNull(3) ? null : reader.GetString(3),
                    SortOrder = reader.GetInt32(4),
                    ModelFeatureParams = new List<ModelFeatureParam>()
                });
            }
        }

        return byModelId;
    }
}
