using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SunsteadLauncher
{
    public partial class MainWindow : Window
    {
        private enum Phase { Idle, Checking, Downloading, Waiting, Injecting, Done, Error }

        private Phase _phase = Phase.Idle;
        private bool _busy;
        private SettingsWindow _settings;
        private CancellationTokenSource _cts;
        private string _remoteVersion, _localVersion;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var rise = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var tf = new TranslateTransform();
            Card.RenderTransform = tf;
            BeginAnimation(OpacityProperty, fade);
            tf.BeginAnimation(TranslateTransform.YProperty, rise);

            _localVersion = Updater.DllExists ? Updater.ReadLocalVersion() : null;
            UpdateVersionPill();
            await CheckForUpdatesQuietAsync();
        }

        // status helpers

        private void UpdateVersionPill()
        {
            string v = Updater.DllExists ? _localVersion : null;
            VersionPill.Text = string.IsNullOrEmpty(v) ? "not installed" : "v" + v;
            VersionPill.Foreground = string.IsNullOrEmpty(v)
                ? (Brush)FindResource("Warn") : (Brush)FindResource("Good");
        }

        private void SetStatus(string text, Phase phase)
        {
            _phase = phase;
            StatusText.Text = text;

            Brush dot = Brushes.Gray;
            switch (phase)
            {
                case Phase.Done: dot = (Brush)FindResource("Good"); break;
                case Phase.Error: dot = (Brush)FindResource("Bad"); break;
                default:
                    if (phase != Phase.Idle) dot = (Brush)FindResource("Warn");
                    break;
            }
            StatusDot.Fill = dot;

            bool busyPhase = phase == Phase.Checking || phase == Phase.Downloading ||
                             phase == Phase.Waiting || phase == Phase.Injecting;

            Progress.Visibility = busyPhase ? Visibility.Visible : Visibility.Collapsed;
            if (phase == Phase.Downloading)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = 0;
            }
            else if (busyPhase)
            {
                Progress.IsIndeterminate = true;
            }
        }

        private async Task CheckForUpdatesQuietAsync()
        {
            try
            {
                _remoteVersion = await Updater.GetRemoteVersionAsync(CancellationToken.None);
                if (!Updater.DllExists)
                    SetStatus("Sunstead is not installed - press Launch.", Phase.Idle);
                else if (string.IsNullOrEmpty(_localVersion) || _localVersion != _remoteVersion)
                    SetStatus("Update available - press Launch to update.", Phase.Idle);
                else
                    SetStatus("Up to date (v" + _remoteVersion + ").", Phase.Idle);
            }
            catch
            {
                SetStatus(Updater.DllExists
                    ? "Update check failed - will use the installed version."
                    : "Update check failed (offline?).",
                    Phase.Idle);
            }
            UpdateVersionPill();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            LaunchBtn.IsEnabled = !busy;
        }

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            SetBusy(true);
            _cts = new CancellationTokenSource();

            try
            {
                bool force = _settings != null && _settings.ForceDownload;

                SetStatus("Checking version...", Phase.Checking);
                string remote = null;
                try { remote = await Updater.GetRemoteVersionAsync(_cts.Token); }
                catch { /* offline fallback below */ }

                string local = Updater.ReadLocalVersion();
                bool needDownload = !Updater.DllExists || string.IsNullOrEmpty(local) ||
                                    string.IsNullOrEmpty(remote) || remote != local || force;

                if (!string.IsNullOrEmpty(remote)) _remoteVersion = remote;

                if (needDownload)
                {
                    SetStatus(Updater.DllExists && !string.IsNullOrEmpty(local) && !string.IsNullOrEmpty(remote)
                        ? "Updating: v" + local + " -> v" + remote + "..."
                        : "Downloading Sunstead" + (string.IsNullOrEmpty(remote) ? "" : " v" + remote) + "...",
                        Phase.Downloading);

                    var progress = new Progress<double>(v => Progress.Value = v);
                    try
                    {
                        await Updater.DownloadDllAsync(progress, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        SetStatus("Cancelled.", Phase.Idle);
                        return;
                    }
                    catch (Exception ex)
                    {
                        SetStatus("Download failed: " + ex.Message, Phase.Error);
                        return;
                    }
                    _localVersion = string.IsNullOrEmpty(remote) ? "(new)" : remote;
                    Updater.WriteLocalVersion(remote);
                    SetStatus("Downloaded" + (string.IsNullOrEmpty(remote) ? "." : " v" + remote + "."),
                        Phase.Checking);
                }
                else
                {
                    SetStatus("Sunstead v" + local + " is ready.", Phase.Checking);
                }

                if (!await EnsureGameRunningAsync())
                    return;

                await InjectAsync(Updater.DllPath);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, Phase.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void Launch_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_busy) return;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a DLL to inject",
                Filter = "Dynamic Libraries (*.dll)|*.dll|All Files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;

            SetBusy(true);
            _cts = new CancellationTokenSource();
            InjectCustomAsync(dlg.FileName);
        }

        private async void InjectCustomAsync(string dllPath)
        {
            try
            {
                if (await EnsureGameRunningAsync())
                    await InjectAsync(dllPath);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Cancelled.", Phase.Idle);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, Phase.Error);
            }
            finally { SetBusy(false); }
        }

        private async Task<bool> EnsureGameRunningAsync()
        {
            if (Injector.FindGamePids().Count > 0)
                return true;

            if (!Injector.IsGameInstalled())
            {
                SetStatus("Minecraft for Windows is not installed / not found.", Phase.Error);
                return false;
            }

            SetStatus("Minecraft not running - launching it...", Phase.Waiting);
            if (!Injector.TryLaunchGame())
            {
                SetStatus("Failed to start Minecraft.", Phase.Error);
                return false;
            }

            SetStatus("Waiting for Minecraft to open... (Esc to cancel)", Phase.Waiting);
            for (int i = 0; i < 120; i++) // up to 60s
            {
                await Task.Delay(500, _cts.Token).ContinueWith(_ => { });
                if (_cts.Token.IsCancellationRequested)
                {
                    SetStatus("Cancelled.", Phase.Idle);
                    return false;
                }
                if (Injector.FindGamePids().Count > 0)
                {
                    await Task.Delay(2500); // give the game a moment to initialize
                    return true;
                }
            }

            SetStatus("Minecraft did not start in time.", Phase.Error);
            return false;
        }

        private async Task InjectAsync(string dllPath)
        {
            SetStatus("Looking for Minecraft...", Phase.Injecting);
            var pids = Injector.FindGamePids();
            if (pids.Count == 0)
            {
                SetStatus("Minecraft is not running. Start the game first.", Phase.Error);
                return;
            }

            SetStatus("Injecting (PID " + pids[0] + ")...", Phase.Injecting);
            await Task.Delay(120);

            string error = Injector.Inject(pids[0], dllPath);
            if (error == Injector.ALREADY_INJECTED)
            {
                SetStatus("Already injected - Sunstead is loaded.", Phase.Done);
                return;
            }
            if (error == null)
            {
                SetStatus("Injected! Have fun.", Phase.Done);
                return;
            }

            SetStatus("Injection failed: " + error, Phase.Error);
            new ErrorWindow(error) { Owner = this }.ShowDialog();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Close(); _settings = null; return; }

            _settings = new SettingsWindow { Owner = this };
            _settings.Closed += (s, _) => { SettingsToggle.IsChecked = false; _settings = null; };
            _settings.CheckUpdatesRequested += async (s, _) =>
            {
                SetStatus("Checking for updates...", Phase.Checking);
                await CheckForUpdatesQuietAsync();
            };

            var src = PointToScreen(new Point(ActualWidth, 20));
            _settings.Left = src.X - 60;
            _settings.Top = src.Y;
            _settings.Show();
            SettingsToggle.IsChecked = true;
        }

        private void Discord_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://sunstead.bond/discord");

        private void Changelog_Click(object sender, RoutedEventArgs e)
            => OpenUrl("https://sunstead.bond/changelog");

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = url, UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_settings != null) { _settings.Close(); return; }
                if (_phase == Phase.Downloading || _phase == Phase.Waiting)
                    _cts?.Cancel();
            }
        }
    }
}
