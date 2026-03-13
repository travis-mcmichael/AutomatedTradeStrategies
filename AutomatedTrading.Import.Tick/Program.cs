// See https://aka.ms/new-console-template for more information
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Import;

namespace NinjaTickDataImport;

class Program
{
    private static string file = "C:\\dev\\data\\AutomatedTrading.Core\\20260212\\20260223_20260226.txt";

    static async Task Main(string[] args)
    {
        Console.WriteLine("Tick data import starting...");
        try
        {
            var imported = await TradeDataImporter.ImportTickData(file);
            Console.WriteLine($"Imported {imported} records.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}

