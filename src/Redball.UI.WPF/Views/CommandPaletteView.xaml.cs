using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Redball.UI.ViewModels;

namespace Redball.UI.Views
{
    public partial class CommandPaletteView : UserControl
    {
        public CommandPaletteView()
        {
            InitializeComponent();
            IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    SearchBox.Focus();
                }
            };
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = (CommandPaletteViewModel)DataContext;
            if (e.Key == Key.Down)
            {
                // Navigate list
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                // Navigate list
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                viewModel.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                viewModel.CloseCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Grid)
            {
                ((CommandPaletteViewModel)DataContext).CloseCommand.Execute(null);
            }
        }
    }
}
