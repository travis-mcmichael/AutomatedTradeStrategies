namespace AutomatedTrading.Core.Models;

public record Bar(
    DateTime Time, 
    double Open, 
    double High, 
    double Low, 
    double Close, 
    long Volume);