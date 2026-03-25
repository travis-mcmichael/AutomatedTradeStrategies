namespace AutomatedTrading.Core.Framework.ML;

public record ModelConfig
{
    public string Name {get; set;}
    public string ModelDate {get;set;}
    public int FeatureCount {get;set;}
    public bool IsHigh {get;set;}

    public ModelConfig(string name, string modelDate, int featureCount, bool isHigh)
    {
        Name = name;
        ModelDate = modelDate;
        FeatureCount = featureCount;
        IsHigh = isHigh;
    }
}