// See https://aka.ms/new-console-template for more information
using Microsoft.Data.Sqlite;
using AutomatedTrading.Core.Models;

namespace NinjaTickDataImport;

class Program
{    private static int batchSize = 250;
    private static string csvFile = "C:\\dev\\data\\AutomatedTrading.Core\\20260212\\20260223_20260226.txt";

    static async Task Main(string [] args)
    {
        if (!File.Exists(csvFile))
        {
            Console.WriteLine($"File not found: {csvFile}");
            return;
        }

        var tickBatch = new List<TickData>();
        var totalInserted = 0;

        using var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();
        using var reader = new StreamReader(csvFile);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var tick = ParseCsvLine.Parse(line);
            if (tick == null) continue;
            
            tickBatch.Add(tick);
            if (tickBatch.Count >= batchSize)
            {
                var inserted = await InsertBatchAsync.Insert(connection, tickBatch);
                totalInserted += inserted;
                tickBatch.Clear();
                Thread.Sleep(10);
            }
        }
        
        if (tickBatch.Count > 0)
        {
            var inserted = await InsertBatchAsync.Insert(connection, tickBatch);
            totalInserted += inserted;
        }

        Console.WriteLine($"Finished imported: Total inserted : {totalInserted}");
    }
}

