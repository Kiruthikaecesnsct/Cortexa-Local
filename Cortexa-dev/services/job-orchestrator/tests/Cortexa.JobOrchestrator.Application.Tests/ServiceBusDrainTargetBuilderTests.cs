using Azure.Messaging.ServiceBus;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class ServiceBusDrainTargetBuilderTests
{
    private const string OrchestratorSubscription = "orchestrator";

    private static Dictionary<string, string> TwoTopics() =>
        new()
        {
            ["ingestion.requested"] = "ingestion-requested",
            ["extraction.completed"] = "extraction-completed",
        };

    private static Dictionary<string, string> TwoWorkerSubscriptions() =>
        new()
        {
            ["ingestion.requested"] = "ingestion-worker",
            ["extraction.completed"] = "extraction-worker",
        };

    [Fact]
    public void BuildDrainTargets_TopicsAndWorkersWithDeadLetters_ProducesOneActiveAndOneDeadLetterPerPair()
    {
        var topicNames = TwoTopics();
        var workerSubscriptions = TwoWorkerSubscriptions();

        var result = ServiceBusDrainTargetBuilder.BuildDrainTargets(
            topicNames,
            OrchestratorSubscription,
            workerSubscriptions,
            includeDeadLetters: true);

        result.Should().HaveCount(8);

        result.Should().Contain(t => t.Topic == "ingestion-requested" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.None);
        result.Should().Contain(t => t.Topic == "ingestion-requested" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.DeadLetter);
        result.Should().Contain(t => t.Topic == "extraction-completed" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.None);
        result.Should().Contain(t => t.Topic == "extraction-completed" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.DeadLetter);

        result.Should().Contain(t => t.Topic == "ingestion-requested" && t.Subscription == "ingestion-worker" && t.SubQueue == SubQueue.None);
        result.Should().Contain(t => t.Topic == "ingestion-requested" && t.Subscription == "ingestion-worker" && t.SubQueue == SubQueue.DeadLetter);
        result.Should().Contain(t => t.Topic == "extraction-completed" && t.Subscription == "extraction-worker" && t.SubQueue == SubQueue.None);
        result.Should().Contain(t => t.Topic == "extraction-completed" && t.Subscription == "extraction-worker" && t.SubQueue == SubQueue.DeadLetter);
    }

    [Fact]
    public void BuildDrainTargets_IncludeDeadLettersFalse_ProducesNoDeadLetterTargets()
    {
        var topicNames = TwoTopics();
        var workerSubscriptions = TwoWorkerSubscriptions();

        var result = ServiceBusDrainTargetBuilder.BuildDrainTargets(
            topicNames,
            OrchestratorSubscription,
            workerSubscriptions,
            includeDeadLetters: false);

        result.Should().HaveCount(4);
        result.Should().OnlyContain(t => t.SubQueue == SubQueue.None);
    }

    [Fact]
    public void BuildDrainTargets_WorkerSubscriptionMatchesOrchestratorTopicAndSubscription_DedupesToOnePairBeforeExpansion()
    {
        var topicNames = new Dictionary<string, string>
        {
            ["ingestion.requested"] = "shared-topic",
        };
        var workerSubscriptions = new Dictionary<string, string>
        {
            ["ingestion.requested"] = OrchestratorSubscription,
        };

        var result = ServiceBusDrainTargetBuilder.BuildDrainTargets(
            topicNames,
            OrchestratorSubscription,
            workerSubscriptions,
            includeDeadLetters: true);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(t => t.Topic == "shared-topic" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.None);
        result.Should().ContainSingle(t => t.Topic == "shared-topic" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.DeadLetter);
    }

    [Fact]
    public void BuildDrainTargets_EmptyTopicsAndWorkers_ReturnsEmptyResult()
    {
        var topicNames = new Dictionary<string, string>();
        var workerSubscriptions = new Dictionary<string, string>();

        var result = ServiceBusDrainTargetBuilder.BuildDrainTargets(
            topicNames,
            OrchestratorSubscription,
            workerSubscriptions,
            includeDeadLetters: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildDrainTargets_WorkerSubscriptionKeyNotInTopicNames_TargetIsFilteredOutSinceItIsCrossReferenced()
    {
        var topicNames = new Dictionary<string, string>
        {
            ["ingestion.requested"] = "ingestion-requested",
        };
        var workerSubscriptions = new Dictionary<string, string>
        {
            ["unregistered.topic"] = "orphan-worker",
        };

        var result = ServiceBusDrainTargetBuilder.BuildDrainTargets(
            topicNames,
            OrchestratorSubscription,
            workerSubscriptions,
            includeDeadLetters: false);

        result.Should().ContainSingle();
        result.Should().Contain(t => t.Topic == "ingestion-requested" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.None);
        result.Should().NotContain(t => t.Subscription == "orphan-worker");
    }

    [Fact]
    public void BuildDrainTargets_DuplicateTopicValuesInTopicNames_ProducesDistinctOrchestratorTargets()
    {
        var topicNames = new Dictionary<string, string>
        {
            ["alias.one"] = "shared-topic",
            ["alias.two"] = "shared-topic",
        };
        var workerSubscriptions = new Dictionary<string, string>();

        var result = ServiceBusDrainTargetBuilder.BuildDrainTargets(
            topicNames,
            OrchestratorSubscription,
            workerSubscriptions,
            includeDeadLetters: false);

        result.Should().ContainSingle();
        result.Should().Contain(t => t.Topic == "shared-topic" && t.Subscription == OrchestratorSubscription && t.SubQueue == SubQueue.None);
    }
}
