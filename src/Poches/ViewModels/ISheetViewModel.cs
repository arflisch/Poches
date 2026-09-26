using System.Windows.Input;

namespace Poches.ViewModels;

/// <summary>A screen shown as a sheet; on a computer, the Escape key runs <see cref="DismissCommand"/>.</summary>
public interface ISheetViewModel
{
    ICommand DismissCommand { get; }
}
