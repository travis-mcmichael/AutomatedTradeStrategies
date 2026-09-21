using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Import;

namespace AutomatedTrading.Import;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: AutomatedTrading.Import <directory> <bar|tick>");
            return;
        }

        var directory  = args[0];
        var importType = args[1].ToLowerInvariant();

        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"Directory not found: {directory}");
            return;
        }

        if (importType != "bar" && importType != "tick")
        {
            Console.WriteLine($"Unknown import type '{importType}'. Expected 'bar' or 'tick'.");
            return;
        }

        var factory = new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION);

        Console.WriteLine($"{importType} data import starting...");
        try
        {
            var imported = 0;
            var files = Directory.GetFiles(directory).OrderBy(x => x);

            if (importType == "bar")
            {
                var barRepo = new BarRepository(factory);
                foreach (var file in files)
                    imported += await BarImporter.ImportBarData(file, barRepo, AppConstants.BAR_DATA_BATCH_SIZE);
            }
            else
            {
                var tickRepo = new TickRepository(factory);
                foreach (var file in files)
                    imported += await TickImporter.ImportTickData(file, tickRepo, AppConstants.TICK_DATA_BATCH_SIZE);
            }

            Console.WriteLine($"Imported {imported} records.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
