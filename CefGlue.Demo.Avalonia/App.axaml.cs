using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Xilium.CefGlue.Demo.Avalonia
{
    public partial class App : Application
    {
        private const bool UseDiDemo = true;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = UseDiDemo ? new DemoDI() : new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
