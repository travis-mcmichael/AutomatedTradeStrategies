
namespace AutomatedTrading.Core.Framework.Utility;

public class Operations
{
    public static double Safe(double? v) => v ?? 0;

    public static double SafeDiv(double a, double b) => b == 0 ? 0 : a / b;
}
