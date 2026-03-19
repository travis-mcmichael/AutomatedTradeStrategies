// See https://aka.ms/new-console-template for more information
namespace AutomatedTrading.Import.Tick;

class Program
{
    private static string file = "C:\\dev\\data\\AutomatedTrading.Core\\20260212\\20260223_20260226.txt";
    private static string instrument = "MES";

    static async Task Main(string[] args)
    {
        Console.WriteLine("Tick data import starting...");
        try
        {
            var imported = await TickImporter.ImportTickData(file, instrument);
            Console.WriteLine($"Imported {imported} records.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}

