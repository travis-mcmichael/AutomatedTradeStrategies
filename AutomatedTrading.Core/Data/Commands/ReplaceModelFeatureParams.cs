using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data.Commands;

// Replaces every ModelFeatureParam on the given ModelConfig's features with the supplied set, sets the
// supplied ModelFeature names, inserts the new ModelFeatures (appended after the existing SortOrder) and
// deletes the given ModelFeatures, in one transaction. Each param must pair one of that model's ModelFeatures
// (or a new feature's TempId) with a FeatureTypeParam of the feature's FeatureType, at most once; each named
// or deleted feature must belong to the model, and nothing may be set on a deleted feature. Otherwise
// nothing is changed and ArgumentException is thrown.
public class ReplaceModelFeatureParams(IDbConnectionFactory connectionFactory, ILogger<ReplaceModelFeatureParams> logger)
{
    // Must match the popup's FEATURE_SOURCES values and the switch in FeatureComputations.
    private const string SESSION_SOURCE = "sessionMinute";
    private static readonly HashSet<string> windowSources = ["windowMinute", "windowDay"];
    private static readonly HashSet<string?> validSources = [null, "prevMinute", "currMinute", SESSION_SOURCE, "sessionDay", .. windowSources];

    // Context fields are FeatureTypeParams with no FeatureType (FeatureTypeId = WINDOW_FEATURE_TYPE_ID). Days/weeks/months
    // apply to every context-enabled feature; start/end only when the source is a window.
    private static readonly HashSet<string> sessionContextKeys = ["days", "weeks", "months"];
    private static readonly HashSet<string> windowContextKeys = ["start", "end"];

    // Allows a context field on a feature: days/weeks/months for any context-enabled feature, start/end only for a window source.
    private static void allowContextParam(HashSet<(long ModelFeatureId, long FeatureTypeParamId)> allowed, long modelFeatureId,
        string? source, long featureTypeParamId, string key)
    {
        var isSessionField = sessionContextKeys.Contains(key);
        var isWindowField = windowContextKeys.Contains(key) && source is not null && windowSources.Contains(source);
        if (isSessionField || isWindowField)
            allowed.Add((modelFeatureId, featureTypeParamId));
    }

    public async Task<int> Execute(long modelConfigId, IEnumerable<ModelFeatureParam> parameters,
        IReadOnlyDictionary<long, string?>? featureNames = null, IReadOnlyCollection<long>? deletedFeatureIds = null,
        IReadOnlyList<NewModelFeature>? newFeatures = null)
    {
        var list = parameters.ToList();
        featureNames ??= new Dictionary<long, string?>();
        var deleted = (deletedFeatureIds ?? []).ToHashSet();
        newFeatures ??= [];

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var allowed = new HashSet<(long ModelFeatureId, long FeatureTypeParamId)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            // Window fields belong to no FeatureType, so a window feature also allows the window FeatureTypeParams.
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

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                SELECT mf.Id, mf.Source, ftp.Id, ftp.ParameterKey
                  FROM ModelFeature mf
                  JOIN FeatureType ft
                    ON ft.Id = mf.FeatureTypeId
                  JOIN FeatureTypeParam ftp
                    ON ftp.FeatureTypeId = @WindowFeatureTypeId
                 WHERE mf.ModelConfigId = @ModelConfigId
                   AND ft.ContextEnabled = 1";
            SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);
            SqlHelper.AddParameter(cmd, "@WindowFeatureTypeId", AppConstants.WINDOW_FEATURE_TYPE_ID);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                allowContextParam(allowed, reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2), reader.GetString(3));
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

        if (newFeatures.Count > 0)
        {
            var typeParamIds = new Dictionary<long, List<long>>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT ft.Id, ftp.Id FROM FeatureType ft LEFT JOIN FeatureTypeParam ftp ON ftp.FeatureTypeId = ft.Id";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!typeParamIds.TryGetValue(reader.GetInt64(0), out var ids))
                        typeParamIds[reader.GetInt64(0)] = ids = [];
                    if (!reader.IsDBNull(1))
                        ids.Add(reader.GetInt64(1));
                }
            }

            var contextTypeIds = new HashSet<long>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT Id FROM FeatureType WHERE ContextEnabled = 1";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    contextTypeIds.Add(reader.GetInt64(0));
            }

            var contextParams = new List<(long Id, string Key)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT Id, ParameterKey FROM FeatureTypeParam WHERE FeatureTypeId = @WindowFeatureTypeId";
                SqlHelper.AddParameter(cmd, "@WindowFeatureTypeId", AppConstants.WINDOW_FEATURE_TYPE_ID);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    contextParams.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            var tempIds = new HashSet<long>();
            foreach (var feature in newFeatures)
            {
                if (feature.TempId >= 0 || !tempIds.Add(feature.TempId))
                    throw new ArgumentException($"New feature TempId {feature.TempId} must be negative and unique.");
                if (!typeParamIds.TryGetValue(feature.FeatureTypeId, out var ids))
                    throw new ArgumentException($"FeatureType {feature.FeatureTypeId} does not exist.");
                if (!validSources.Contains(feature.Source))
                    throw new ArgumentException($"Source '{feature.Source}' is not valid; use sessionMinute, sessionDay, windowMinute, windowDay, prevMinute, currMinute or none (defaults to sessionMinute).");
                foreach (var id in ids)
                    allowed.Add((feature.TempId, id));
                if (contextTypeIds.Contains(feature.FeatureTypeId))
                    foreach (var (id, key) in contextParams)
                        allowContextParam(allowed, feature.TempId, feature.Source, id, key);
            }
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

        var insertedIds = new Dictionary<long, long>();
        if (newFeatures.Count > 0)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) FROM ModelFeature WHERE ModelConfigId = @ModelConfigId";
            SqlHelper.AddParameter(cmd, "@ModelConfigId", modelConfigId);
            var sortOrder = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            cmd.CommandText = @"
                INSERT INTO ModelFeature (ModelConfigId, FeatureTypeId, Source, SortOrder, ModelFeatureName)
                VALUES (@ModelConfigId, @FeatureTypeId, @Source, @SortOrder, @ModelFeatureName)
                RETURNING Id";
            SqlHelper.AddParameter(cmd, "@FeatureTypeId", null);
            SqlHelper.AddParameter(cmd, "@Source", null);
            SqlHelper.AddParameter(cmd, "@SortOrder", null);
            SqlHelper.AddParameter(cmd, "@ModelFeatureName", null);
            foreach (var feature in newFeatures)
            {
                cmd.Parameters["@FeatureTypeId"].Value = feature.FeatureTypeId;
                cmd.Parameters["@Source"].Value = feature.Source ?? SESSION_SOURCE;
                cmd.Parameters["@SortOrder"].Value = ++sortOrder;
                cmd.Parameters["@ModelFeatureName"].Value = string.IsNullOrWhiteSpace(feature.ModelFeatureName) ? DBNull.Value : feature.ModelFeatureName!.Trim();
                insertedIds[feature.TempId] = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }
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
                cmd.Parameters["@ModelFeatureId"].Value = p.ModelFeatureId < 0 ? insertedIds[p.ModelFeatureId] : p.ModelFeatureId;
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
            foreach (var entry in featureNames)
            {
                cmd.Parameters["@ModelFeatureName"].Value = string.IsNullOrWhiteSpace(entry.Value) ? DBNull.Value : entry.Value!.Trim();
                cmd.Parameters["@Id"].Value = entry.Key;
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
