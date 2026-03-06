// See https://aka.ms/new-console-template for more information
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Import.Bar;

class Program
{
    static string file = "C:\\dev\\data\\MES 03-26.Last.txt";

    static async Task Main(string[] args)
    {
        Console.WriteLine("Bar data import starting...");
        try
        {
            var imported = await Importer.ImportBarData(file);
            Console.WriteLine($"Imported {imported} records.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}