using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PlaylistMaker.Services
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    /// <summary>
    /// Swaps the palette dictionary of the running application and keeps the native window frame in
    /// step (a dark window with a white title bar looks broken). The palette lives in
    /// Themes/Light.xaml and Themes/Dark.xaml, which define exactly the same keys.
    /// </summary>
    public static class ThemeManager
    {
        private const string LightPaletteSource = "Themes/Light.xaml";

        private const string DarkPaletteSource = "Themes/Dark.xaml";
        private const int UseImmersiveDarkMode = 20;

        private const int UseImmersiveDarkModeBefore20H1 = 19;

        private static bool isHooked;

        /// <summary>The theme that is currently applied.</summary>
        public static AppTheme Current { get; private set; } = AppTheme.Light;

        /// <summary>Reads the saved theme, falling back to light when the file says nothing useful.</summary>
        public static AppTheme LoadSavedTheme()
        {
            return Parse(UserSettings.Load().Theme);
        }

        public static AppTheme Parse(string? theme)
        {
            return string.Equals(theme, nameof(AppTheme.Dark), StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;
        }

        /// <summary>
        /// A fully qualified pack URI, so the lookup never depends on whatever base URI happens to be
        /// ambient when the dictionary is created in code.
        /// </summary>
        private static Uri PaletteUri(string relativePath)
        {
            string assemblyName = typeof(ThemeManager).Assembly.GetName().Name ?? "PlaylistMaker";
            return new Uri(
                "pack://application:,,,/" + assemblyName + ";component/" + relativePath,
                UriKind.Absolute);
        }

        /// <summary>Applies a theme to the whole application, open windows included.</summary>
        public static void Apply(AppTheme theme)
        {
            Current = theme;
            HookWindows();
            SwapPalette(theme);
            ApplyToOpenWindows();
        }

        /// <summary>
        /// Every window created later (the dialogs) also gets its native frame darkened, without the
        /// dialogs having to know about the theme.
        /// </summary>
        private static void HookWindows()
        {
            if (isHooked)
            {
                return;
            }

            isHooked = true;
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyWindowLoaded));
        }

        private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                ApplyToWindowFrame(window);
            }
        }

        private static void ApplyToOpenWindows()
        {
            Application? application = Application.Current;
            if (application == null)
            {
                return;
            }

            foreach (Window window in application.Windows)
            {
                ApplyToWindowFrame(window);
            }
        }

        private static void SwapPalette(AppTheme theme)
        {
            Application? application = Application.Current;
            if (application == null)
            {
                return;
            }

            ResourceDictionary replacement = new ResourceDictionary
            {
                Source = PaletteUri(theme == AppTheme.Dark ? DarkPaletteSource : LightPaletteSource)
            };

            Collection<ResourceDictionary> merged = application.Resources.MergedDictionaries;
            for (int index = 0; index < merged.Count; index++)
            {
                string? source = merged[index].Source?.OriginalString;
                if (source == null)
                {
                    continue;
                }

                string normalised = source.Replace('\\', '/');
                if (normalised.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                    || normalised.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    merged[index] = replacement;
                    return;
                }
            }

            // no palette merged yet: put it in front of the control styles
            merged.Insert(0, replacement);
        }

        /// <summary>
        /// Windows 10 1809+ paints its title bar dark for us; older builds simply ignore the call,
        /// which is why every failure here is swallowed.
        /// </summary>
        private static void ApplyToWindowFrame(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                int value = Current == AppTheme.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, Marshal.SizeOf<int>()) != 0)
                {
                    DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref value, Marshal.SizeOf<int>());
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>Formats the theme the way it is stored in the settings file.</summary>
        public static string ToSettingValue(AppTheme theme)
        {
            return theme.ToString(CultureInfo.InvariantCulture);
        }

        [DllImport("dwmapi.dll", SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
