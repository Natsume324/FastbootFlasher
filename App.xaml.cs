using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace FastbootFlasher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            string langfile = Path.Combine(AppContext.BaseDirectory, "lang.ini");
            string fbfile = FastbootCmd.ExecutablePath;

            if (!File.Exists(langfile))
            {
                
                MessageBox.Show($"语言配置文件lang.ini不存在！\n应用程序将退出。",
                    "文件缺失",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
               
                Shutdown();
                return;
            }
            else if (!File.Exists(fbfile))
            {

                MessageBox.Show($"fastboot.exe不存在！\n应用程序将退出。",
                    "文件缺失",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
                return;
            }

            base.OnStartup(e);
        }
    }

}
