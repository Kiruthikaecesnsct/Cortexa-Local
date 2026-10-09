using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Collector.Presentation.Behaviors;
using Collector.Presentation.ViewModels;

namespace Collector.Presentation.Views;

public partial class RemoteSourceView : UserControl
{
    private RemoteSourceViewModel? _viewModel;

    public RemoteSourceView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach(DataContext as RemoteSourceViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs e) => Attach(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Attach(e.NewValue as RemoteSourceViewModel);
        }
    }

    private void Attach(RemoteSourceViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.CredentialsCleared -= OnCredentialsCleared;
        }

        _viewModel = viewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.CredentialsCleared += OnCredentialsCleared;
        RestoreSecrets(_viewModel);
    }

    private void RestoreSecrets(RemoteSourceViewModel viewModel)
    {
        TokenPassword.Password = viewModel.Token;
        TokenText.Text = viewModel.Token;
        PassphrasePassword.Password = viewModel.SshPassphrase;
        PassphraseText.Text = viewModel.SshPassphrase;
        SyncTokenFocusKey();
    }

    private void OnCredentialsCleared(object? sender, EventArgs e)
    {
        TokenPassword.Clear();
        TokenText.Clear();
        PassphrasePassword.Clear();
        PassphraseText.Clear();
        TokenReveal.IsChecked = false;
        PassphraseReveal.IsChecked = false;
        SyncTokenFocusKey();
    }

    private void OnTokenPasswordChanged(object sender, RoutedEventArgs e) => SetToken(TokenPassword.Password);

    private void OnTokenTextChanged(object sender, TextChangedEventArgs e) => SetToken(TokenText.Text);

    private void OnPassphrasePasswordChanged(object sender, RoutedEventArgs e) => SetPassphrase(PassphrasePassword.Password);

    private void OnPassphraseTextChanged(object sender, TextChangedEventArgs e) => SetPassphrase(PassphraseText.Text);

    private void SetToken(string value)
    {
        if (_viewModel is not null)
        {
            _viewModel.Token = value;
        }
    }

    private void SetPassphrase(string value)
    {
        if (_viewModel is not null)
        {
            _viewModel.SshPassphrase = value;
        }
    }

    private void OnTokenRevealClick(object sender, RoutedEventArgs e)
    {
        Swap(TokenReveal, TokenPassword, TokenText);
        SyncTokenFocusKey();
    }

    private void OnPassphraseRevealClick(object sender, RoutedEventArgs e) =>
        Swap(PassphraseReveal, PassphrasePassword, PassphraseText);

    private void Swap(ToggleButton toggle, PasswordBox hidden, TextBox shown)
    {
        var reveal = toggle.IsChecked == true;
        if (reveal)
        {
            shown.Text = hidden.Password;
            shown.CaretIndex = shown.Text.Length;
        }
        else
        {
            hidden.Password = shown.Text;
        }

        FocusLater(reveal ? shown : hidden);
    }

    private void SyncTokenFocusKey()
    {
        var reveal = TokenReveal.IsChecked == true;
        FocusRouter.SetKey(TokenPassword, reveal ? null : RemoteFocusKeys.Token);
        FocusRouter.SetKey(TokenText, reveal ? RemoteFocusKeys.Token : null);
    }

    private void OnFieldHostGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            ((UIElement)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    private void OnOrganizationGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_viewModel?.OrgUrlError is not null)
        {
            OrganizationUrl.SelectAll();
        }
    }

    private void OnRepositoryClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ((FrameworkElement)sender).DataContext is RemoteRepositoryRowViewModel row)
        {
            _viewModel.SelectedRepository = row;
        }
    }

    private void OnBranchClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ((FrameworkElement)sender).DataContext is BranchOptionViewModel branch)
        {
            _viewModel.SelectedBranch = branch;
        }
    }

    private void OnRepositoryStepVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        FocusWhenShown(e, RepositorySearch);

    private void OnBranchStepVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        FocusWhenShown(e, BranchSearch);

    private void OnFilesStepVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        FocusWhenShown(e, FileSearch);

    private void OnFilesStepPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        FileSearch.Focus();
        FileSearch.SelectAll();
        e.Handled = true;
    }

    private void OnFileSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        e.Handled = e.Key switch
        {
            Key.Escape => ClearFileSearch(_viewModel.FileTree),
            Key.Down => FocusFirstFileRow(),
            _ => false,
        };
    }

    private static bool ClearFileSearch(RemoteFileTreeViewModel tree)
    {
        if (tree.SearchText.Length == 0)
        {
            return false;
        }

        tree.SearchText = string.Empty;
        return true;
    }

    private bool FocusFirstFileRow()
    {
        if (FileTreeList.Items.Count == 0)
        {
            return false;
        }

        FileTreeList.SelectedItem ??= FileTreeList.Items[0];
        FileTreeList.ScrollIntoView(FileTreeList.SelectedItem);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            (FileTreeList.ItemContainerGenerator.ContainerFromItem(FileTreeList.SelectedItem) as ListBoxItem)?.Focus());
        return true;
    }

    private void OnFileTreePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null || FileTreeList.SelectedItem is not FileTreeRowViewModel row)
        {
            return;
        }

        Action<FileTreeRowViewModel>? action = e.Key switch
        {
            Key.Space => _viewModel.FileTree.ToggleCheck,
            Key.Enter => _viewModel.FileTree.Activate,
            Key.Right => _viewModel.FileTree.ExpandOrDescend,
            Key.Left => _viewModel.FileTree.CollapseOrAscend,
            _ => null,
        };
        action?.Invoke(row);
        e.Handled = action is not null;
    }

    private void FocusWhenShown(DependencyPropertyChangedEventArgs e, UIElement target)
    {
        if (e.NewValue is true)
        {
            FocusLater(target);
        }
    }

    private void FocusLater(UIElement target) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (target.IsVisible && target.IsEnabled)
            {
                target.Focus();
            }
        });
}
