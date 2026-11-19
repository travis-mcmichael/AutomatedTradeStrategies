using System.Collections.Generic;
using System.Linq;

namespace AutomatedTrading.Core.Models;

// Result of GetInferenceReadiness. Model-level Issues cover the model, session and ATR; each feature carries its own
// issues. Each issue is a problem that would stop an inference from running.
public class InferenceReadiness
{
    public List<string> Issues { get; } = [];
    public List<FeatureReadiness> Features { get; } = [];
    public bool IsReady => Issues.Count == 0 && Features.All(f => f.IsReady);
}

public class FeatureReadiness(long modelFeatureId, string name, string featureTypeName, string source)
{
    public long ModelFeatureId { get; } = modelFeatureId;
    public string Name { get; } = name;
    public string FeatureTypeName { get; } = featureTypeName;
    public string Source { get; } = source;
    public List<string> Issues { get; } = [];
    public bool IsReady => Issues.Count == 0;
}
