using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace Glimpse.API.UI;

public interface IImmediateGUI
{
    public void Separator();

    public void Separator(string heading);

    public void Text(string text);

    public void Text(string text, uint fontSize);

    public bool Button(string text, string? tooltip = null, [CallerLineNumber] int id = 0,
        [CallerMemberName] string caller = "");

    public bool Button(string text, Size size, string? tooltip = null, [CallerLineNumber] int id = 0,
        [CallerMemberName] string caller = "");

    public bool Checkbox(string text, ref bool ticked, string? tooltip = null, [CallerLineNumber] int id = 0,
        [CallerMemberName] string caller = "");

    public bool Dropdown(string label, ref int value, ReadOnlySpan<string> items, string? tooltip = null,
        [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "");

    public bool Input(string? hint, ref string text, string? label = null, string? tooltip = null,
        bool returnTrueOnlyOnEnter = true, [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "");

    public bool Slider(string label, ref int number, int min, int max, string? tooltip = null,
        [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "");

    public bool Slider(string label, ref float number, float min, float max, string? tooltip = null,
        [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "");

    public void Table(ReadOnlySpan<TableHeading> headings, Action<ITableContext> callback, int? numRows = null,
        bool useRelativeWidths = false, [CallerLineNumber] int id = 0, [CallerMemberName] string caller = "");

    /// <summary>
    /// Show a file dialog to open/save files and folders.
    /// </summary>
    /// <param name="type">The <see cref="FileDialogType"/> that the dialog should be.</param>
    /// <param name="title">The title to apply to the dialog window, if any.</param>
    /// <param name="filters"><see cref="FileFilter"/>s that determine the various supported file types. When opening
    /// folders, this is ignored.</param>
    /// <param name="callback">The action that is called when the dialog is completed. The first parameter denotes the
    /// file(s) that were selected. If <see langword="null"/>, the dialog was canceled. The second parameter contains
    /// the index of the filter used.</param>
    /// <param name="allowMany">Allow many files/folders to be selected.</param>
    public void ShowFileDialog(FileDialogType type, string? title, ReadOnlySpan<FileFilter> filters,
        Action<string[]?, int> callback, bool allowMany = false, [CallerLineNumber] int id = 0,
        [CallerMemberName] string caller = "");
}