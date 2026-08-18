using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Queries;

public class GetBarsBySessionDate(IDefaultRepository<BarData> barRepo)
{
    public async Task<IList<BarData>> Execute(DateTime sessionDate)
    {
        return [.. (await barRepo.GetDataAsync(new Dictionary<string, object>
        {
            { "SessionDate", sessionDate.ToString(AppConstants.DATE_FORMAT) }
        }))
        .OrderBy(x => x.SessionTime)];
    }
}