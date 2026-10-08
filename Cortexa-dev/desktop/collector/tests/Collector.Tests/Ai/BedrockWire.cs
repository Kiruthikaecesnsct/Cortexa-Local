using System.Reflection;
using Amazon.BedrockRuntime;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;

namespace Collector.Tests.Ai;

internal static class BedrockWire
{
    private static readonly Assembly Infrastructure = typeof(BedrockDirectProvider).Assembly;

    private static readonly Type MapperType =
        Infrastructure.GetType("Collector.Infrastructure.Ai.BedrockErrorMapper")!;

    public static AiProviderException MapBedrock(AmazonBedrockRuntimeException exception) =>
        Invoke(nameof(MapBedrock), exception);

    public static AiProviderException MapIdentityCenter(Exception exception) =>
        Invoke(nameof(MapIdentityCenter), exception);

    public static AiProviderException MapUnreachable(Exception exception) =>
        Invoke(nameof(MapUnreachable), exception);

    public static AiProviderException MapTimeout(Exception exception) =>
        Invoke(nameof(MapTimeout), exception);

    private static AiProviderException Invoke(string methodName, Exception exception)
    {
        var method = MapperType.GetMethod(methodName)!;
        return (AiProviderException)method.Invoke(null, [exception])!;
    }
}
