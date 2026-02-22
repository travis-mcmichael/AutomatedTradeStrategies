using System;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

// Data transfer objects for requests and responses
public class ComputeFeaturesRequest
{
    public DateTime? BarTime { get; set; }
    public double? Atr14 { get; set; }
    public double? VolumeAccel { get; set; }
    public double? SessionHigh { get; set; }
    public double? SessionLow  { get; set; }
    public int? TickCountThisBar { get; set; }
    public List<Bar>? Bars { get; set; }

    public ComputeFeaturesRequest()
    { }
    public ComputeFeaturesRequest(
        DateTime barTime,
        double atr14,
        double volumeAccel,
        double sessionHigh,
        double sessionLow,
        int tickCountThisBar,
        List<Bar> bars)
    {
        BarTime = barTime;
        Atr14 = atr14;
        VolumeAccel = volumeAccel;
        SessionHigh = sessionHigh;
        SessionLow = sessionLow;
        TickCountThisBar = tickCountThisBar;
        Bars = bars;
    }
}

public class FeaturesRequest 
{
    public double[]? Features { get; set; }
    public FeaturesRequest(double[] features)
    {
        Features = features;
    }

    public FeaturesRequest() { }
}

public class FeaturesResponse 
{
    public double[]? Features { get; set; }
    public FeaturesResponse(double[] features)
    {
        Features = features;
    }

    public FeaturesResponse() { }
} 

public class MetaInferenceRequest
{
    public double[]? Scaled { get; set; }
    public double? BaseProbability { get; set; }
    public MetaInferenceRequest(double[] scaled, double baseProbability)
    {
        Scaled = scaled;
        BaseProbability = baseProbability;
    }

    public MetaInferenceRequest() { }
}
public class InferenceResponse
{
    public double? Result { get; set; }
    public InferenceResponse(double result)
    {
        Result = result;
    }

    public InferenceResponse() { }
}