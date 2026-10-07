namespace Collector.Application.Extraction;

public enum FileClassification
{
    Pdf,
    Docx,
    Code,
    Text,
}

public static class FileClassifier
{
    public static FileClassification Classify(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return FileClassification.Pdf;
        }

        if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
        {
            return FileClassification.Docx;
        }

        return CodeLanguage.IsKnownCodeExtension(extension) ? FileClassification.Code : FileClassification.Text;
    }
}
