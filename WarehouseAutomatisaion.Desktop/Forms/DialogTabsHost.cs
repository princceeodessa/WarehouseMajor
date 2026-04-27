using WarehouseAutomatisaion.Desktop.Controls;

namespace WarehouseAutomatisaion.Desktop.Forms;

public static class DialogTabsHost
{
    private static Form1? _shell;

    public static void Attach(Form1 shell)
    {
        _shell = shell;
    }

    public static void Detach(Form1 shell)
    {
        if (ReferenceEquals(_shell, shell))
        {
            _shell = null;
        }
    }

    public static DialogResult ShowDialog(Form dialog, IWin32Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        TextMojibakeFixer.NormalizeControlTree(dialog);

        var shell = _shell;
        if (shell is not null && !shell.IsDisposed && !ReferenceEquals(shell, dialog))
        {
            return shell.ShowDialogInDocumentTab(dialog);
        }

        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}
