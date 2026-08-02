using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Repositories;

public interface IModelConfigRepository
{
    /// <summary>Returns every ModelConfig row with its Features and each feature's Params fully populated.</summary>
    Task<List<ModelConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Inserts a new ModelConfig, or updates the existing one matched by ModelName. Features/Params are replaced wholesale.</summary>
    Task<ModelConfig> UpsertAsync(ModelConfig request, CancellationToken ct = default);
}
