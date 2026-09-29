namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IBlobStorageWriter
{
    Task<string> SaveRawAsync(string batchId, string documentId, Stream content, string contentType, CancellationToken ct);
}
