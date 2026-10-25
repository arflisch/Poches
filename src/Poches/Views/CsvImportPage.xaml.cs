using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class CsvImportPage : ContentPage
{
    public CsvImportPage(CsvImportViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
