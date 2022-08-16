using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace DebugOutputToasts
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static StreamWriter Errors = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (Errors == null)
            {
                string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DebugOutputToasts");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                string ErrorsPath = Path.Combine(configDir, "errors.txt");

                Errors = new StreamWriter(new FileStream(ErrorsPath, FileMode.Append), encoding: System.Text.Encoding.UTF8);
                Errors.AutoFlush = true;
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, ex) => { Errors.WriteLine($"[{DateTime.Now:s}] {ex.ExceptionObject}"); };

            TaskScheduler.UnobservedTaskException += (sender, ex) => { Errors.WriteLine($"[{DateTime.Now:s}] {ex.Exception}"); };
        }
    }
}
