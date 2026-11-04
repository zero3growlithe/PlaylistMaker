using System.Windows;

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
        }
    }
}
