using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public sealed record OrgUrl(SourceType Provider, string Organization);
