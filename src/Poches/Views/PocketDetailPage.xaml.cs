using Poches.Controls;
using Poches.ViewModels;

namespace Poches.Views;

public partial class PocketDetailPage : ContentPage
{
    private bool _isWide;

    public PocketDetailPage(PocketDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Body.SizeChanged += (_, _) => UpdateLayout();
    }

    /// <summary>
    /// On a wide screen (iPad, large Mac window), the balance, actions and schedules move to a column of their own,
    /// next to the history, instead of scrolling away above it.
    /// </summary>
    private void UpdateLayout()
    {
        var wide = Body.Width >= TwoPane.WideWidth;
        if (wide == _isWide)
            return;
        _isWide = wide;

        // Moving views while the grid is being laid out would be ignored: do it right after.
        Dispatcher.Dispatch(() =>
        {
            if (_isWide)
            {
                HeaderStack.Remove(Summary);
                Summary.Margin = new Thickness(20, 8, 0, 40);
                SummaryScroll.Content = Summary;
                Body.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
                Grid.SetColumn(History, 1);
                SummaryScroll.IsVisible = true;
            }
            else
            {
                SummaryScroll.IsVisible = false;
                SummaryScroll.Content = null;
                Summary.Margin = new Thickness(0);
                HeaderStack.Insert(0, Summary);
                Body.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
                Grid.SetColumn(History, 0);
            }
        });
    }
}
