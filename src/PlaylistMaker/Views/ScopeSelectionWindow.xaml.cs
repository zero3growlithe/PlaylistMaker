using System.Windows;

namespace PlaylistMaker.Views
{
    /// <summary>Asks whether a folder should be scanned one level deep or recursively.</summary>
    public partial class ScopeSelectionWindow : Window
    {
        public ScopeSelectionWindow(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        /// <summary>True when the user picked the recursive option.</summary>
        public bool Recursive => RecursiveRadio.IsChecked == true;

        /// <summary>Returns true/false for the chosen scope, or null when cancelled.</summary>
        public static bool? Ask(Window owner, string message)
        {
            ScopeSelectionWindow window = new ScopeSelectionWindow(message);
            window.Owner = owner;
            bool? result = window.ShowDialog();
            if (result != true)
            {
                return null;
            }
            return window.Recursive;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
