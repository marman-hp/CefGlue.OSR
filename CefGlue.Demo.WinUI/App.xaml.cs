using Microsoft.UI.Xaml;

namespace Xilium.CefGlue.Demo.WinUI
{
    public partial class App : Application
    {
        private const bool UseDiDemo = true;

        internal static MainWindow MainWindowInstance { get; private set; }
        internal static DemoDI DemoDIInstance { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            if (UseDiDemo)
            {
                DemoDIInstance = new DemoDI();
                DemoDIInstance.Activate();
            }
            else
            {
                MainWindowInstance = new MainWindow();
                MainWindowInstance.Activate();
            }
        }
    }
}
