using Azure.Messaging.ServiceBus;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

// Service Bus topic -> fanout exchange of the same name; subscription -> quorum queue
// "{topic}.{subscription}"; dead-letter sub-queue -> classic queue "{topic}.{subscription}.dlq".
// deploy/local/native/rabbitmq-topology.ps1 declares these; the Python services use the same names.
public static class RabbitMqNames
{
    public const string SessionIdHeader = "session_id";
    public const string RetryAttemptHeader = "x-retry-attempt";
    public const string CorrelationIdHeader = "correlation_id";
    public const string DeadLetterReasonHeader = "dead_letter_reason";
    public const string DeadLetterDescriptionHeader = "dead_letter_error_description";

    private const string DeadLetterSuffix = ".dlq";

    public static string SubscriptionQueue(string topic, string subscription) => $"{topic}.{subscription}";

    public static string DeadLetterQueue(string subscriptionQueue) => subscriptionQueue + DeadLetterSuffix;

    public static string DelayQueue(string topic, TimeSpan delay) => $"{topic}.delay.{(long)delay.TotalMilliseconds}ms";

    public static string QueueFor(DrainTarget target)
    {
        var queue = SubscriptionQueue(target.Topic, target.Subscription);
        return target.SubQueue == SubQueue.DeadLetter ? DeadLetterQueue(queue) : queue;
    }
}
