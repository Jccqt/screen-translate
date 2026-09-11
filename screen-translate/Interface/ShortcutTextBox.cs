using System.Runtime.InteropServices;

namespace screen_translate.Interface;

internal sealed class ShortcutTextBox : TextBox
{
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) != Keys.Tab || (keyData & Keys.Modifiers) is not (Keys.None or Keys.Shift);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // WinForms KeyData omits the Windows modifier; never reinterpret a Win chord as Ctrl/Alt alone.
        if (GetKeyState((int)Keys.LWin) < 0 || GetKeyState((int)Keys.RWin) < 0)
        {
            e.SuppressKeyPress = true;
            base.OnKeyDown(new KeyEventArgs(Keys.LWin));
            return;
        }
        base.OnKeyDown(e);
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);
}
