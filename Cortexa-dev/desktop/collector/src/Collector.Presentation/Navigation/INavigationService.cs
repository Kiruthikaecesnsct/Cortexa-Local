namespace Collector.Presentation.Navigation;

public interface INavigationService
{
    IReadOnlyList<ScreenRegistration> Screens { get; }

    ScreenRegistration? CurrentScreen { get; }

    object? CurrentViewModel { get; }

    event EventHandler? Navigated;

    void NavigateTo(string key);
}

public interface INavigationAware
{
    void OnNavigatedTo();

    void OnNavigatedFrom();
}
