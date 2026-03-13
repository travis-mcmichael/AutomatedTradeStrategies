// See https://aka.ms/new-console-template for more information
using AutomatedTrading.Core.Framework.Import;

namespace AutomatedTrading.Import.Bar;

class Program
{
    static string[] files = {
        @"C:\dev\data\ML\MES 03-22.Last.txt",
        @"C:\dev\data\ML\MES 06-22.Last.txt",
        @"C:\dev\data\ML\MES 09-22.Last.txt",
        @"C:\dev\data\ML\MES 12-22.Last.txt",
        @"C:\dev\data\ML\MES 03-23.Last.txt",
        @"C:\dev\data\ML\MES 06-23.Last.txt",
        @"C:\dev\data\ML\MES 09-23.Last.txt",
        @"C:\dev\data\ML\MES 12-23.Last.txt",
        @"C:\dev\data\ML\MES 03-24.Last.txt",
        @"C:\dev\data\ML\MES 06-24.Last.txt",
        @"C:\dev\data\ML\MES 09-24.Last.txt",
        @"C:\dev\data\ML\MES 12-24.Last.txt",
        @"C:\dev\data\ML\MES 03-25.Last.txt",
        @"C:\dev\data\ML\MES 06-25.Last.txt",
        @"C:\dev\data\ML\MES 09-25.Last.txt",
        @"C:\dev\data\ML\MES 12-25.Last.txt",
        @"C:\dev\data\ML\MES 03-26.Last.txt",
        };

    static async Task Main(string[] args)
    {
        Console.WriteLine("Bar data import starting...");
        try
        {
            var imported = 0;
            foreach (var file in files)
            {
                imported += await TradeDataImporter.ImportBarData(file);
            }

            Console.WriteLine($"Imported {imported} records.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}