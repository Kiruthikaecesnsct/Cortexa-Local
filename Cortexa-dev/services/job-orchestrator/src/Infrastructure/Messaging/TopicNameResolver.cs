using Cortexa.JobOrchestrator.Infrastructure.Configuration;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public static class TopicNameResolver
{
    public static string Resolve(ServiceBusSettings settings, string eventType) =>
        settings.TopicNames.TryGetValue(eventType, out var name)
            ? name
            : eventType.Replace('.', '-');
}
