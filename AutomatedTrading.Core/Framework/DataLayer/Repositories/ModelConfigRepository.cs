using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer.Commands.Models;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Repositories;

public class ModelConfigRepository : IModelConfigRepository
{
    private readonly IDbConnectionFactory connectionFactory;

    public ModelConfigRepository(IDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<List<ModelConfig>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        return await new GetAllModelConfigsQuery().ExecuteAsync(connection, ct);
    }

    public async Task<ModelConfig> UpsertAsync(ModelConfig request, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        return await new UpsertModelConfigCommand(request).ExecuteAsync(connection, ct);
    }
}
