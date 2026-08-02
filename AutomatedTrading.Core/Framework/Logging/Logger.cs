using System;
using System.IO;
using log4net;
using log4net.Config;

namespace AutomatedTrading.Core.Framework.Logging;

public static class Logger
{
    private static readonly ILog log = LogManager.GetLogger("AutomatedTrading");

    /// <summary>
    /// Initialises log4net for the Core assembly's repository from log4net.config in the
    /// application's base directory.  Call this once at startup from any host process
    /// (e.g. Program.cs in the API).  Required on .NET 5+ because the
    /// [assembly: XmlConfigurator] attribute is not auto-processed outside .NET Framework.
    /// </summary>
    public static void Configure()
    {
        var repo       = LogManager.GetRepository(typeof(Logger).Assembly);
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config");
        XmlConfigurator.Configure(repo, new FileInfo(configPath));
    }

    public static void Debug(string message)
    {
        log.Debug(message);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:DEBUG-{message}");
    }

    public static void Debug(string message, Exception ex)
    {
        log.Debug(message, ex);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:DEBUG-{message}");
    }

    public static void Info(string message)
    {
        log.Info(message);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:INFO-{message}");
    }

    public static void Info(string message, Exception ex)
    {
        log.Info(message, ex);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:INFO-{message}");
    }

    public static void Warn(string message)
    {
        log.Warn(message);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:WARN-{message}");
    }

    public static void Warn(string message, Exception ex)
    {
        log.Warn(message, ex);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:WARN-{message}");
    }

    public static void Error(string message)
    {
        log.Error(message);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:ERROR-{message}");
    }

    public static void Error(string message, Exception ex)
    {
        log.Error(message, ex);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:ERROR-{message}");
    }

    public static void Fatal(string message)
    {
        log.Fatal(message);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:FATAL-{message}");
    }

    public static void Fatal(string message, Exception ex)
    {
        log.Fatal(message, ex);
        Console.WriteLine($"{DateTime.Now:yyyyMMdd HHmmss}:FATAL-{message}");
    }
}
