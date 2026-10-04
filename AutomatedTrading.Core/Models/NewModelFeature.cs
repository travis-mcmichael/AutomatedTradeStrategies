namespace AutomatedTrading.Core.Models;

// A ModelFeature for ReplaceModelFeatureParams to insert. TempId is a caller-assigned negative id that the
// accompanying ModelFeatureParams use as their ModelFeatureId until the real Id exists.
public class NewModelFeature
{
    public long TempId { get; set; }
    public long FeatureTypeId { get; set; }
    public string? Source { get; set; }
    public string? ModelFeatureName { get; set; }
}
