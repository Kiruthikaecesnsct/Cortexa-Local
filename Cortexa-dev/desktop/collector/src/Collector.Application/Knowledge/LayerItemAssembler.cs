using Collector.Domain.Enums;
using Collector.Domain.Knowledge;

namespace Collector.Application.Knowledge;

public sealed class LayerItemAssembler(LayerPrompt prompt)
{
    public ExtractedKnowledgeItem Assemble(FolderGroup group, RawKnowledgeItem raw)
    {
        var representative = group.RepresentativeFile.RepresentativeItem;
        return new ExtractedKnowledgeItem
        {
            DocumentId = representative.DocumentId,
            DocumentName = representative.DocumentName,
            DocumentPath = representative.DocumentPath,
            UnitKind = UnitKind.Module,
            Kind = KnowledgeKind.Layer,
            Title = UploadFieldClamp.Truncate(raw.Title, UploadLimitsMirror.TitleMax),
            Summary = UploadFieldClamp.Truncate(raw.Summary, UploadLimitsMirror.SummaryMax),
            Details = UploadFieldClamp.TruncateOptional(raw.Details, UploadLimitsMirror.DetailsMax),
            Source = new KnowledgeSource
            {
                FilePath = FolderPathFor(group.FolderPath),
                LineStart = null,
                LineEnd = null,
            },
            Excerpt = null,
            EchoVerdict = EchoVerdict.Clean,
            PromptVersion = prompt.Version,
        };
    }

    private static string FolderPathFor(string folderPath) =>
        UploadFieldClamp.Truncate(folderPath.TrimEnd('/') + "/", UploadLimitsMirror.FilePathMax);
}
