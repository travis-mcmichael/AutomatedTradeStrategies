namespace AutomatedTrading.Core.Framework.ML;

public record SessionResult
{
    public string SessionDate { get; set; }
    public double Predicted { get; set; }
    public double Actual { get; set; }
    public double PredictedMove { get; set; }
    public double ActualMove { get; set; }
    public double Atr20 { get; set; }
    public SessionResult(
        string sessionDate, 
        double predicted, 
        double actual, 
        double predictedMove, 
        double actualMove, 
        double atr20)
    {
        SessionDate = sessionDate;
        Predicted = predicted;
        Actual = actual;
        PredictedMove = predictedMove;
        ActualMove = actualMove;
        Atr20 = atr20;
    }
}