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

        var directory = args[0];
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

        Console.WriteLine($"{importType} data import starting...");
        try
        {
            var imported = 0;
            var files = Directory.GetFiles(directory);
            foreach (var file in files)
            {
                imported += importType == "bar"
                    ? await BarImporter.ImportBarData(file)
                    : await TickImporter.ImportTickData(file);
            }

            Console.WriteLine($"Imported {imported} records.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
