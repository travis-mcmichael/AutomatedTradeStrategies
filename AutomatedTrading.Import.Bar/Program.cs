// See https://aka.ms/new-console-template for more information

namespace AutomatedTrading.Import.Bar;

class Program
{
    static string file = @"C:\dev\data\ML";

    static async Task Main(string[] args)
    {
        Console.WriteLine("Bar data import starting...");
        try
        {
            var imported = 0;
            var files = Directory.GetFiles(file);
            foreach (var file in files)
            {
                imported += await BarImporter.ImportBarData(file);
            }

            Console.WriteLine($"Imported {imported} records.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}