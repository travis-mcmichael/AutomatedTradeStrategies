using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Queries;

// Bars for one session date of a single instrument + version, ordered by SessionTime.
public class GetBarsByInstrumentSession(IDefaultRepository<BarData> barRepo)
{
    public async Task<IList<BarData>> Execute(string sessionDate, string instrument, string instrumentVersion)
    {
        return [.. (await barRepo.GetDataAsync(new Dictionary<string, object>
        {
            { "SessionDate", sessionDate },
            { "Instrument", instrument },
            { "InstrumentVersion", instrumentVersion }
        }))
        .OrderBy(x => x.SessionTime)];
    }
}
