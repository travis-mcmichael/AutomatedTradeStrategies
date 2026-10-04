using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data.Commands;

// Replaces every ModelFeatureParam on the given ModelConfig's features with the supplied set, sets the
// supplied ModelFeature names and deletes the given ModelFeatures, in one transaction. Each param must pair
// one of that model's ModelFeatures with a FeatureTypeParam of the feature's FeatureType, at most once; each
// named or deleted feature must belong to the model, and nothing may be set on a deleted feature. Otherwise
// nothing is changed and ArgumentException is thrown.
public class ReplaceModelFeatureParams(IDbConnectionFactory connectionFactory, ILogger<ReplaceModelFeatureParams> logger)
{
    public async Task<int> Execute(long modelConfigId, IEnumerable<ModelFeatureParam> parameters,
        IReadOnlyDictionary<long, string?>? featureNames = null, IReadOnlyCollection<long>? deletedFeatureIds = null)
    {
        var list = parameters.ToList();
        featureNames ??= new Dictionary<long, string?>();
        var deleted = (deletedFeatureIds ?? []).ToHashSet();

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var allowed = new HashSet<(long ModelFeatureId, long FeatureTypeParamId)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                SELECT mf.Id, ftp.Id
                  FROM ModelFeature mf
                  JOIN FeatureTypeParam ftp
                    ON ftp.FeatureTypeId = mf.FeatureTypeId
                 WHERE mf.ModelConfigId = @ModelConfigId";
            SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                allowed.Add((reader.GetInt64(0), reader.GetInt64(1)));
        }

        // Looked up separately: features whose FeatureType has no params don't appear in the join above.
        var modelFeatureIds = new HashSet<long>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT Id FROM ModelFeature WHERE ModelConfigId = @ModelConfigId";
            SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                modelFeatureIds.Add(reader.GetInt64(0));
        }
        foreach (var featureId in featureNames.Keys.Concat(deleted))
        {
            if (!modelFeatureIds.Contains(featureId))
                throw new ArgumentException($"ModelFeature {featureId} does not belong to model {modelConfigId}.");
        }
        foreach (var featureId in featureNames.Keys.Concat(list.Select(p => p.ModelFeatureId)))
        {
            if (deleted.Contains(featureId))
                throw new ArgumentException($"ModelFeature {featureId} is being deleted, so its name or params can't also be set.");
        }

        var seen = new HashSet<(long, long)>();
        foreach (var p in list)
        {
            var key = (p.ModelFeatureId, p.FeatureTypeParamId);
            if (!allowed.Contains(key))
                throw new ArgumentException($"FeatureTypeParam {p.FeatureTypeParamId} is not valid for ModelFeature {p.ModelFeatureId} on model {modelConfigId}.");
            if (!seen.Add(key))
                throw new ArgumentException($"FeatureTypeParam {p.FeatureTypeParamId} is set more than once for ModelFeature {p.ModelFeatureId}.");
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                DELETE FROM ModelFeatureParam
                 WHERE ModelFeatureId IN (SELECT Id FROM ModelFeature WHERE ModelConfigId = @ModelConfigId)";
            SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO ModelFeatureParam (ModelFeatureId, FeatureTypeParamId, ParamValue)
                VALUES (@ModelFeatureId, @FeatureTypeParamId, @ParamValue)";
            SqlHelper.AddParameter(cmd, "@ModelFeatureId", null);
            SqlHelper.AddParameter(cmd, "@FeatureTypeParamId", null);
            SqlHelper.AddParameter(cmd, "@ParamValue", null);
            foreach (var p in list)
            {
                cmd.Parameters["@ModelFeatureId"].Value = p.ModelFeatureId;
                cmd.Parameters["@FeatureTypeParamId"].Value = p.FeatureTypeParamId;
                cmd.Parameters["@ParamValue"].Value = p.ParamValue;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE ModelFeature SET ModelFeatureName = @ModelFeatureName WHERE Id = @Id";
            SqlHelper.AddParameter(cmd, "@ModelFeatureName", null);
            SqlHelper.AddParameter(cmd, "@Id", null);
            foreach (var (featureId, name) in featureNames)
            {
                cmd.Parameters["@ModelFeatureName"].Value = string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim();
                cmd.Parameters["@Id"].Value = featureId;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // The model's params were all deleted above, so the deleted features have none left to orphan.
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM ModelFeature WHERE Id = @Id AND ModelConfigId = @ModelConfigId";
            SqlHelper.AddParameter(cmd, "@Id", null);
            SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);
            foreach (var featureId in deleted)
            {
                cmd.Parameters["@Id"].Value = featureId;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
        logger.LogInformation("Replaced feature params for ModelConfigId {ModelConfigId} with {Count} values", modelConfigId, list.Count);
        return list.Count;
    }
}
