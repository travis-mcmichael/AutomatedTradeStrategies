namespace AutomatedTrading.Core.Framework;

public interface ITradeSpan
{
    double Open {get; set;}
    double High {get;set;}
    double Low {get;set;}
    double Close {get;set;}
    double Volume {get;set;}
}