namespace AutomatedTrading.Core.Framework;

public class TradeWindow
{
    public int WindowStart { get; set; }
    public int WindowEnd { get; set; }
    public int DaysBack { get; set; } = 0;
    public bool InWindow(int time)
        => WindowStart <= WindowEnd ? time >= WindowStart && time < WindowEnd
            : time >= WindowStart || time < WindowEnd;
}