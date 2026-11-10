namespace AutomatedTrading.Core.Models;

// Result of GetInferencePrediction: the value the live API produces for a model when its trigger fires on a session.
// Succeeded is false, with Error set, when no value could be produced.
public class InferencePrediction
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public string SessionDate { get; set; } = "";
    // HH:mm:ss of the bar that triggered the prediction; only bars up to this time were used.
    public string? AsOfTime { get; set; }
    public string Target { get; set; } = "";
    public double Open { get; set; }
    public double Atr { get; set; }
    // Points from the open. Negative for LOW models, which predict a move down.
    public double Move { get; set; }
    // Open + Move: the predicted high (HIGH models) or predicted low (LOW models).
    public double Level { get; set; }

    public static InferencePrediction Failed(string sessionDate, string error) =>
        new() { SessionDate = sessionDate, Error = error };
}
