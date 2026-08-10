namespace AutomatedTrading.Core.Models;
public class FeatureTypeParam
    {
        public int Id { get; set; }
        public FeatureType FeatureType { get; set; }
        public string ParameterKey { get; set; } = default!;
    }