using System;
using System.Windows;
using System.Windows.Controls;

namespace SunsteadLauncher
{
    public partial class SettingsWindow : Window
    {
        public event EventHandler SettingsChanged;
        public event EventHandler CheckUpdatesRequested;

        public bool AutoUpdate { get { return OptAutoUpdate.IsChecked == true; } }
        public bool ForceDownload { get { return OptForceDownload.IsChecked == true; } }

        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Updater.OpenDllFolder();
        }
    }
}
