namespace Collector.Application.Knowledge;

public static class UploadLimitsMirror
{
    public const int TitleMax = 300;

    public const int SummaryMax = 2000;

    public const int DetailsMax = 8000;

    public const int ExcerptMax = 400;

    public const int SectionMax = 300;

    public const int FilePathMax = 1024;

    public const int FilenameMax = 256;

    public const int BatchNameMax = 200;

    public const int MaxDocumentsPerBatch = 50;

    public const int MaxItemsPerDocument = 500;

    public const int MaxBodyBytes = 9 * 1024 * 1024;
}
