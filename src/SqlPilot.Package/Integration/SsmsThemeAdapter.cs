using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace SqlPilot.Package.Integration
{
    /// <summary>
    /// Makes the tool window follow the SSMS colour theme. The XAML in SqlPilot.UI and
    /// the tool window only ever name SQL Pilot's own brush keys; this fills those keys
    /// on the hosted control from the shell's theme and refills them when it changes.
    /// It lives here, in C#, so SqlPilot.UI needs no VSSDK reference and no XAML has to
    /// name a Shell assembly that differs between the VSSDK 15.x and 17.x builds.
    /// </summary>
    internal sealed class SsmsThemeAdapter : IDisposable
    {
        private readonly FrameworkElement _target;
        private bool _disposed;

        public SsmsThemeAdapter(FrameworkElement target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            Apply();
            VSColorTheme.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            if (_disposed) return;

            if (_target.Dispatcher.CheckAccess())
                Apply();
            else
                _target.Dispatcher.BeginInvoke(new Action(Apply));
        }

        private void Apply()
        {
            if (_disposed) return;

            var r = _target.Resources;

            // Surfaces and text
            Color background = Themed(EnvironmentColors.ToolWindowBackgroundColorKey, SystemColors.WindowColor);
            r["ResultsBackground"] = Brush(background);
            r["SearchBoxBackground"] = Brush(background);
            r["StatusBarBackground"] = Brush(background);
            r["PanelBorder"] = Brush(Themed(EnvironmentColors.ToolWindowBorderColorKey, SystemColors.ControlDarkColor));
            r["PrimaryForeground"] = Brush(Themed(EnvironmentColors.ToolWindowTextColorKey, SystemColors.WindowTextColor));
            r["SecondaryForeground"] = Brush(Themed(EnvironmentColors.SystemGrayTextColorKey, SystemColors.GrayTextColor));
            r["IconForeground"] = Brush(Themed(EnvironmentColors.PanelHyperlinkColorKey, SystemColors.HotTrackColor));

            // Gold reads well on a dark surface and washes out on a light one.
            r["FavoriteForeground"] = Brush(IsDark(background) ? Colors.Gold : Colors.DarkGoldenrod);

            // Search box
            r["InputBackground"] = Brush(Themed(CommonControlsColors.TextBoxBackgroundColorKey, SystemColors.WindowColor));
            r["InputForeground"] = Brush(Themed(CommonControlsColors.TextBoxTextColorKey, SystemColors.WindowTextColor));
            r["InputBorder"] = Brush(Themed(CommonControlsColors.TextBoxBorderColorKey, SystemColors.ControlDarkColor));

            // Buttons
            r["ButtonBackground"] = Brush(Themed(CommonControlsColors.ButtonColorKey, SystemColors.ControlColor));
            r["ButtonForeground"] = Brush(Themed(CommonControlsColors.ButtonTextColorKey, SystemColors.ControlTextColor));
            r["ButtonBorder"] = Brush(Themed(CommonControlsColors.ButtonBorderColorKey, SystemColors.ControlDarkColor));
            r["ButtonHoverBackground"] = Brush(Themed(CommonControlsColors.ButtonHoverColorKey, SystemColors.ControlLightColor));
            r["ButtonHoverForeground"] = Brush(Themed(CommonControlsColors.ButtonHoverTextColorKey, SystemColors.ControlTextColor));
            r["ButtonHoverBorder"] = Brush(Themed(CommonControlsColors.ButtonBorderHoverColorKey, SystemColors.HighlightColor));
            r["ButtonPressedBackground"] = Brush(Themed(CommonControlsColors.ButtonPressedColorKey, SystemColors.ControlDarkColor));
            r["ButtonPressedForeground"] = Brush(Themed(CommonControlsColors.ButtonPressedTextColorKey, SystemColors.ControlTextColor));
            r["ButtonPressedBorder"] = Brush(Themed(CommonControlsColors.ButtonBorderPressedColorKey, SystemColors.HighlightColor));
            r["ButtonDisabledForeground"] = Brush(Themed(CommonControlsColors.ButtonDisabledTextColorKey, SystemColors.GrayTextColor));

            // Context menu
            r["MenuBackground"] = Brush(Themed(EnvironmentColors.CommandBarMenuBackgroundGradientBeginColorKey, SystemColors.MenuColor));
            r["MenuForeground"] = Brush(Themed(EnvironmentColors.CommandBarTextActiveColorKey, SystemColors.MenuTextColor));
            r["MenuBorder"] = Brush(Themed(EnvironmentColors.CommandBarMenuBorderColorKey, SystemColors.ControlDarkColor));

            // Update notification bar
            r["InfoBarBackground"] = Brush(Themed(InfoBarColors.InfoBarBackgroundColorKey, SystemColors.InfoColor));
            r["InfoBarForeground"] = Brush(Themed(InfoBarColors.InfoBarBackgroundTextColorKey, SystemColors.InfoTextColor));
            r["InfoBarBorder"] = Brush(Themed(InfoBarColors.InfoBarBorderColorKey, SystemColors.ControlDarkColor));

            // The shell's own scroll bar style, when it publishes one. It is built on
            // theme brushes itself, so it only needs putting in place once.
            if (!r.Contains(typeof(ScrollBar)))
            {
                try
                {
                    if (Application.Current?.TryFindResource(VsResourceKeys.ScrollBarStyleKey) is Style scrollBarStyle)
                        r[typeof(ScrollBar)] = scrollBarStyle;
                }
                catch { /* stock scroll bars */ }
            }
        }

        /// <summary>
        /// A colour from the current theme. Falls back to the matching Windows system
        /// colour when the shell has no value for the key, which is how the window
        /// looked before it followed the theme.
        /// </summary>
        private static Color Themed(ThemeResourceKey key, Color fallback)
        {
            try
            {
                var c = VSColorTheme.GetThemedColor(key);
                return c.A == 0 ? fallback : Color.FromArgb(c.A, c.R, c.G, c.B);
            }
            catch
            {
                return fallback;
            }
        }

        private static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static bool IsDark(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            VSColorTheme.ThemeChanged -= OnThemeChanged;
        }
    }
}
