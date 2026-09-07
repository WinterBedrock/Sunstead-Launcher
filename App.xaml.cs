using System;
using System.IO;
using System.Windows;

namespace SunsteadLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Log(args.Exception);
                MessageBox.Show(args.Exception.ToString(), "Sunstead Launcher - error");
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Log(args.ExceptionObject as Exception);

            base.OnStartup(e);
        }

        private static void Log(Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    ex?.ToString() ?? "unknown");
            }
            catch { }
        }
    }
}
