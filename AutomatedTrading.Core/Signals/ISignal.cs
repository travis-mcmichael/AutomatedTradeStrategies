using System.Collections.Generic;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public interface ISignal
{
    public bool IsTriggered(List<Bar> bars);
}