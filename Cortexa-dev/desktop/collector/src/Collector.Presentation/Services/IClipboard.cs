using System.Runtime.InteropServices;
using System.Windows;

namespace Collector.Presentation.Services;

public interface IClipboard
{
    bool TrySetText(string text);
}

public sealed class WpfClipboard : IClipboard
{
    public bool TrySetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
