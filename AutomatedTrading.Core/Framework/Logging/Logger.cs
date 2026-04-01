using System;
using log4net;

namespace AutomatedTrading.Core.Framework.Logging;

public static class Logger
{
    private static readonly ILog log = LogManager.GetLogger("AutomatedTrading");

    public static string Debug(string message)
    {
        log.Debug(message);
        return message;
    }

    public static string Debug(string message, Exception ex)
    {
        log.Debug(message, ex);
        return message;
    }

    public static string Info(string message)
    {
        log.Info(message);
        return message;
    }

    public static string Info(string message, Exception ex)
    {
        log.Info(message, ex);
        return message;
    }

    public static string Warn(string message)
    {
        log.Warn(message);
        return message;
    }

    public static string Warn(string message, Exception ex)
    {
        log.Warn(message, ex);
        return message;
    }

    public static string Error(string message)
    {
        log.Error(message);
        return message;
    }

    public static string Error(string message, Exception ex)
    {
        log.Error(message, ex);
        return message;
    }

    public static string Fatal(string message)
    {
        log.Fatal(message);
        return message;
    }

    public static string Fatal(string message, Exception ex)
    {
        log.Fatal(message, ex);
        return message;
    }
}
