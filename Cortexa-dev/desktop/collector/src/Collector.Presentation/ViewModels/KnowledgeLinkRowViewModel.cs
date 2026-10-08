using System.IO;
using Collector.Application.History;
using Collector.Domain.History;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed record HistorySourceContext(string BatchId, LocalSourceResolver Resolver, ILocalFileLauncher Launcher);

public sealed partial class KnowledgeLinkRowViewModel : ObservableObject
{
    private readonly HistorySourceContext _context;
    private LocalSourceTarget? _target;
    private string? _resolvedDocumentId;

    public KnowledgeLinkRowViewModel(CandidateKnowledgeLink link, HistorySourceContext context)
    {
        _context = context;
        Key = KeyOf(link);
        KindLabel = string.Empty;
        Title = string.Empty;
        Summary = string.Empty;
        SourceText = string.Empty;
        OpenName = string.Empty;
        Update(link);
    }

    public string Key { get; }

    public Task Resolution { get; private set; } = Task.CompletedTask;

    public bool IsAvailable => _target is not null;

    public bool ShowNotFound => IsResolved && !IsAvailable;

    public string? NotFoundHint => ShowNotFound ? HistoryStrings.SourceNotFound : null;

    public bool HasSourceText => !string.IsNullOrEmpty(SourceText);

    [ObservableProperty]
    public partial string KindLabel { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSourceText))]
    public partial string SourceText { get; set; }

    [ObservableProperty]
    public partial bool IsFileSource { get; set; }

    [ObservableProperty]
    public partial string OpenName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNotFound), nameof(NotFoundHint))]
    public partial bool IsResolved { get; set; }

    public static string KeyOf(CandidateKnowledgeLink link)
    {
        var source = link.Source;
        return $"{link.KnowledgeItem.Id}|{source.DocumentId}|{source.PageNumber}|{source.LineStart}|{source.LineEnd}";
    }

    public void Update(CandidateKnowledgeLink link)
    {
        var parts = SourceLineFormatter.Parts(link.Source);
        KindLabel = ReviewStrings.KindLabel(link.KnowledgeItem.Kind);
        Title = link.KnowledgeItem.Title;
        Summary = link.KnowledgeItem.Summary;
        SourceText = SourceLineFormatter.Format(parts, string.Empty);
        IsFileSource = SourceLineFormatter.IsFileSource(parts);
        OpenName = HistoryStrings.OpenSourceName(link.KnowledgeItem.Title);
        if (!string.Equals(_resolvedDocumentId, link.Source.DocumentId, StringComparison.Ordinal))
        {
            _resolvedDocumentId = link.Source.DocumentId;
            Resolution = ResolveAsync(link.Source.DocumentId);
        }
    }

    private async Task ResolveAsync(string documentId)
    {
        try
        {
            _target = await _context.Resolver.ResolveAsync(_context.BatchId, documentId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _target = null;
        }

        MarkResolved();
    }

    private void MarkResolved()
    {
        IsResolved = true;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(ShowNotFound));
        OnPropertyChanged(nameof(NotFoundHint));
        OpenSourceCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private void OpenSource()
    {
        if (_target is not null && _context.Launcher.TryLaunch(_target))
        {
            return;
        }

        _target = null;
        MarkResolved();
    }
}
