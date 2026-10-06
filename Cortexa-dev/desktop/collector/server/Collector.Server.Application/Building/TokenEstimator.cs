namespace Collector.Server.Application.Building;

public static class TokenEstimator
{
    public const int CharsPerToken = 4;

    public static int Estimate(string text) =>
        (text.Length + CharsPerToken - 1) / CharsPerToken;
}
