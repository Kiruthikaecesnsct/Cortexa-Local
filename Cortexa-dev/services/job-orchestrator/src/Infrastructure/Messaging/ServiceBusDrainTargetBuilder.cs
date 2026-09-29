using Azure.Messaging.ServiceBus;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed record DrainTarget(string Topic, string Subscription, SubQueue SubQueue);

public static class ServiceBusDrainTargetBuilder
{
    public static IReadOnlyList<DrainTarget> BuildDrainTargets(
        IReadOnlyDictionary<string, string> topicNames,
        string orchestratorSubscription,
        IReadOnlyDictionary<string, string> workerSubscriptions,
        bool includeDeadLetters)
    {
        var targets = new List<DrainTarget>();
        var seen = new HashSet<(string Topic, string Subscription)>();

        AddTargets(
            topicNames.Values.Distinct().Select(topic => (topic, orchestratorSubscription)),
            seen,
            targets,
            includeDeadLetters);

        AddTargets(
            BuildWorkerPairs(topicNames, workerSubscriptions),
            seen,
            targets,
            includeDeadLetters);

        return targets;
    }

    private static IEnumerable<(string Topic, string Subscription)> BuildWorkerPairs(
        IReadOnlyDictionary<string, string> topicNames,
        IReadOnlyDictionary<string, string> workerSubscriptions)
    {
        foreach (var (topicKey, subscription) in workerSubscriptions)
        {
            if (topicNames.TryGetValue(topicKey, out var topic))
                yield return (topic, subscription);
        }
    }

    private static void AddTargets(
        IEnumerable<(string Topic, string Subscription)> pairs,
        HashSet<(string Topic, string Subscription)> seen,
        List<DrainTarget> targets,
        bool includeDeadLetters)
    {
        foreach (var pair in pairs)
        {
            if (!seen.Add(pair))
                continue;

            targets.Add(new DrainTarget(pair.Topic, pair.Subscription, SubQueue.None));

            if (includeDeadLetters)
                targets.Add(new DrainTarget(pair.Topic, pair.Subscription, SubQueue.DeadLetter));
        }
    }
}
