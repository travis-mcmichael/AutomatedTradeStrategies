using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public interface ISignal
{
    public bool IsTriggered(IReadOnlyList<Bar> bars);
}