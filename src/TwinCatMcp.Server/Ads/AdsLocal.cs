namespace TwinCatMcp.Ads;

/// <summary>Shared parameter documentation for the local-system guard of writing ADS tools.</summary>
public static class AdsLocal
{
    public const string Description =
        "Required (true) to write to this PC's own TwinCAT system ('local' / its Net ID) on a TwinCAT port (< 25000), " +
        "because it may run another application. Not needed for other targets or customer ADS servers (port >= 25000).";
}
