using Collector.Domain.Enums;

namespace Collector.Application.Secrets;

public static class RemoteSourceSlots
{
    public static SecretSlot? For(SourceType source) => source switch
    {
        SourceType.Github => SecretSlot.GitHubPat,
        SourceType.AzureDevops => SecretSlot.AzureDevOpsPat,
        SourceType.Ssh => SecretSlot.SshPassphrase,
        _ => null,
    };
}
