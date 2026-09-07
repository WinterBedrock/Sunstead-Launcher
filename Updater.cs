using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SunsteadLauncher
{
    internal static class Updater
    {
        private const string Base =
            "https://github.com/sniffz0ne/SunsteadClientReleases/raw/refs/heads/main/";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient();
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SunsteadLauncher/2.0");
            return c;
        }

        public static string AppDataDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SunsteadLauncher");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string DllPath { get { return Path.Combine(AppDataDir, "Sunstead.dll"); } }
        public static string VersionPath { get { return Path.Combine(AppDataDir, "version.txt"); } }
        private static string TempDllPath { get { return Path.Combine(AppDataDir, "Sunstead.new.dll"); } }

        public static bool DllExists { get { return File.Exists(DllPath); } }

        public static string ReadLocalVersion()
        {
            try
            {
                if (!File.Exists(VersionPath)) return null;
                return File.ReadAllText(VersionPath).Trim();
            }
            catch { return null; }
        }

        public static void WriteLocalVersion(string v)
        {
            try { File.WriteAllText(VersionPath, v ?? ""); } catch { }
        }

        public static void OpenDllFolder()
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + DllPath + "\"");
            }
            catch { }
        }

        /// fetches current.txt
        public static async Task<string> GetRemoteVersionAsync(CancellationToken ct)
        {
            string body = await Http.GetStringAsync(Base + "current.txt");
            return (body ?? "").Trim();
        }

        /// downloads dll
        public static async Task DownloadDllAsync(IProgress<double> progress, CancellationToken ct)
        {
            using (var resp = await Http.GetAsync(
                Base + "Sunstead.dll", HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();

                long total = resp.Content.Headers.ContentLength ?? -1;
                using (var src = await resp.Content.ReadAsStreamAsync())
                using (var dst = new FileStream(TempDllPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                {
                    var buf = new byte[65536];
                    long done = 0;
                    int read;
                    while ((read = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, read, ct);
                        done += read;
                        if (total > 0 && progress != null)
                            progress.Report(Math.Min(1.0, (double)done / total));
                    }
                }
            }

            InstallDownloadedDll();
        }

        private static void InstallDownloadedDll()
        {
            string old = DllPath + ".old";
            try { File.Delete(old); } catch { }

            try
            {
                File.Delete(DllPath);
                File.Move(TempDllPath, DllPath);
            }
            catch (IOException)
            {
                File.Move(DllPath, old);
                File.Move(TempDllPath, DllPath);
                try { File.Delete(old); } catch { }
            }
        }
    }
}
