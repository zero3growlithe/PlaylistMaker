using System.Windows;
using PlaylistMaker.Services;

namespace PlaylistMaker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show(
                    "Unexpected error:\n\n" + args.Exception.Message,
                    "Playlist Maker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            // Before StartupUri creates the main window, so the first paint is already in the right
            // theme instead of flashing light and then turning dark.
            ThemeManager.Apply(ThemeManager.LoadSavedTheme());
        }
    }
}
