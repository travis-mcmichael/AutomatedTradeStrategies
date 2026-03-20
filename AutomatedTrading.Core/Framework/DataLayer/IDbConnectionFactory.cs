using System.Data.Common;

namespace AutomatedTrading.Core.Framework.DataLayer;

public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}
