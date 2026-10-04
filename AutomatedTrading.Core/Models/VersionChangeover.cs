namespace AutomatedTrading.Core.Models;

public class VersionChangeover
{
    public string SessionDate { get; set; } = default!;
    public string OldVersion { get; set; } = default!;
    public string NewVersion { get; set; } = default!;

    // NewVersion's first Open on SessionDate minus OldVersion's first Open on SessionDate.
    // Add to OldVersion bars (and every version before it, cumulatively) to line them up with the newer contract.
    public double Adjustment { get; set; }
}
