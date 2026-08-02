using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Models;

public class UpsertModelConfigCommand : IDbCommand<ModelConfig>
{
    // ModelName is the natural key (UNIQUE in schema): an existing row is updated in place,
    // otherwise a new one is inserted. Features/Params are always replaced wholesale rather
    // than diffed — simpler and safe for config-sized data.
    private const string UpsertConfigSql = @"
        INSERT INTO ModelConfig
            (ModelName, Version, Instrument, TrainingStart, TrainingEnd, Target, BarType,
             WindowStart, WindowEnd, FeatureLocation, OutputLocation, Export, Active)
        VALUES
            (@ModelName, @Version, @Instrument, @TrainingStart, @TrainingEnd, @Target, @BarType,
             @WindowStart, @WindowEnd, @FeatureLocation, @OutputLocation, @Export, @Active)
        ON CONFLICT(ModelName) DO UPDATE SET
            Version = excluded.Version,
            Instrument = excluded.Instrument,
            TrainingStart = excluded.TrainingStart,
            TrainingEnd = excluded.TrainingEnd,
            Target = excluded.Target,
            BarType = excluded.BarType,
            WindowStart = excluded.WindowStart,
            WindowEnd = excluded.WindowEnd,
            FeatureLocation = excluded.FeatureLocation,
            OutputLocation = excluded.OutputLocation,
            Export = excluded.Export,
            Active = excluded.Active
        RETURNING Id";

    private const string DeleteParamsSql =
        "DELETE FROM ModelFeatureParam WHERE ModelFeatureId IN (SELECT Id FROM ModelFeature WHERE ModelConfigId = @ModelConfigId)";

    private const string DeleteFeaturesSql =
        "DELETE FROM ModelFeature WHERE ModelConfigId = @ModelConfigId";

    private const string InsertFeatureSql = @"
        INSERT INTO ModelFeature (ModelConfigId, Name, Source, SortOrder)
        VALUES (@ModelConfigId, @Name, @Source, @SortOrder)
        RETURNING Id";

    private const string InsertParamSql = @"
        INSERT INTO ModelFeatureParam (ModelFeatureId, ParamKey, ParamValue)
        VALUES (@ModelFeatureId, @ParamKey, @ParamValue)";

    private readonly ModelConfig request;

    public UpsertModelConfigCommand(ModelConfig request)
    {
        this.request = request;
    }

    public async Task<ModelConfig> ExecuteAsync(DbConnection connection, CancellationToken ct = default)
    {
        using var transaction = connection.BeginTransaction();

        var modelConfigId = await upsertConfig(connection, transaction, ct);

        await deleteChildren(connection, transaction, modelConfigId, ct);

        var features = new List<ModelFeature>();
        for (var i = 0; i < request.ModelFeatures.Count; i++)
        {
            var feature = request.ModelFeatures[i];
            var featureId = await insertFeature(connection, transaction, modelConfigId, feature, i, ct);

            var d = new Dictionary<string, int>();
            foreach (var p in feature.ModelFeatureParams) d.Add(p.ParamKey, p.ParamValue);
            foreach (var entry in d) await insertParam(connection, transaction, featureId, entry.Key, entry.Value, ct);

            feature.Id = featureId;
            features.Add(feature);
        }

        transaction.Commit();

        return new ModelConfig
        {
            Id = modelConfigId,
            ModelName = request.ModelName,
            Version = request.Version,
            Instrument = request.Instrument,
            TrainingStart = request.TrainingStart,
            TrainingEnd = request.TrainingEnd,
            Target = request.Target,
            BarType = request.BarType,
            WindowStart = request.WindowStart,
            WindowEnd = request.WindowEnd,
            FeatureLocation = request.FeatureLocation,
            OutputLocation = request.OutputLocation,
            Export = request.Export,
            Active = request.Active,
            ModelFeatures = features
        };
    }

    private async Task<int> upsertConfig(DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = UpsertConfigSql;
        cmd.Transaction = transaction;
        addParameter(cmd, "@ModelName", request.ModelName);
        addParameter(cmd, "@Version", request.Version);
        addParameter(cmd, "@Instrument", request.Instrument);
        addParameter(cmd, "@TrainingStart", request.TrainingStart);
        addParameter(cmd, "@TrainingEnd", request.TrainingEnd);
        addParameter(cmd, "@Target", request.Target);
        addParameter(cmd, "@BarType", request.BarType);
        addParameter(cmd, "@WindowStart", (object?)request.WindowStart ?? DBNull.Value);
        addParameter(cmd, "@WindowEnd", (object?)request.WindowEnd ?? DBNull.Value);
        addParameter(cmd, "@FeatureLocation", request.FeatureLocation);
        addParameter(cmd, "@OutputLocation", request.OutputLocation);
        addParameter(cmd, "@Export", request.Export);
        addParameter(cmd, "@Active", request.Active);

        var id = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(id);
    }

    private static async Task deleteChildren(DbConnection connection, DbTransaction transaction, int modelConfigId, CancellationToken ct)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = DeleteParamsSql;
            cmd.Transaction = transaction;
            addParameter(cmd, "@ModelConfigId", modelConfigId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = DeleteFeaturesSql;
            cmd.Transaction = transaction;
            addParameter(cmd, "@ModelConfigId", modelConfigId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<int> insertFeature(
        DbConnection connection, DbTransaction transaction, int modelConfigId, ModelFeature feature, int sortOrder, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = InsertFeatureSql;
        cmd.Transaction = transaction;
        addParameter(cmd, "@ModelConfigId", modelConfigId);
        addParameter(cmd, "@Name", feature.Name);
        addParameter(cmd, "@Source", (object?)feature.Source ?? DBNull.Value);
        addParameter(cmd, "@SortOrder", sortOrder);

        var id = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(id);
    }

    private static async Task insertParam(
        DbConnection connection, DbTransaction transaction, int featureId, string key, int value, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = InsertParamSql;
        cmd.Transaction = transaction;
        addParameter(cmd, "@ModelFeatureId", featureId);
        addParameter(cmd, "@ParamKey", key);
        addParameter(cmd, "@ParamValue", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void addParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
