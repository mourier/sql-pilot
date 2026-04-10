using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SqlPilot.Installer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            TryLoadLogo();
        }

        /// <summary>
        /// Loads the logo from the embedded WPF resource if it's there. The csproj
        /// only includes the resource when .github/logo.png exists at build time
        /// (Exists() guard), so a fresh checkout without the logo file produces a
        /// build that simply has no logo. Setting Source via code-behind with a
        /// try/catch keeps the window from crashing in that case — the logo slot
        /// is just blank.
        ///
        /// Also sets Window.Icon (the title-bar icon) to the same image. The exe's
        /// file icon is set separately via the &lt;ApplicationIcon&gt; csproj setting,
        /// which is what Explorer shows.
        /// </summary>
        private void TryLoadLogo()
        {
            try
            {
                var logo = new BitmapImage(new Uri("pack://application:,,,/Resources/logo.png", UriKind.Absolute));
                LogoImage.Source = logo;
                this.Icon = logo;
            }
            catch
            {
                LogoImage.Visibility = Visibility.Collapsed;
            }
        }
    }
}
