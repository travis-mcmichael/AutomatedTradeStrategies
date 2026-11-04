using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

// Result of GetInferenceReadiness. Each issue is a problem that would stop an inference from running.
public class InferenceReadiness
{
    public List<string> Issues { get; } = [];
    public bool IsReady => Issues.Count == 0;
}
