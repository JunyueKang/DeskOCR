using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;

namespace OcrApp
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        protected override void OnStartup(StartupEventArgs e)
        {
            // 禁用 DPI 感知
            SetProcessDPIAware();
            
            base.OnStartup(e);
            
        }
    }
}