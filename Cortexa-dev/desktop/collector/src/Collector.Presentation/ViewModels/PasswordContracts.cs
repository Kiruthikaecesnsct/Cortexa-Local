namespace Collector.Presentation.ViewModels;

public interface IPasswordSource
{
    string GetPassword();

    void Clear();
}

public interface IPasswordHost
{
    IPasswordSource? PasswordSource { get; set; }

    void OnPasswordEdited();
}
