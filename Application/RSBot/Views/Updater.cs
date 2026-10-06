using RSBot.Core;
using RSBot.Core.Config;
using SDUI.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RSBot.Views;

public partial class Updater : UIWindowBase
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/dangvusns/RSBot/releases/latest";
    private string _downloadUrl;
    private string _digest;
    private bool _installing;

    public Updater()
    {
        InitializeComponent();
        btnDownload.DialogResult = DialogResult.None;
        FormClosing += (_, e) => e.Cancel = _installing;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RSBot-Updater");
        return client;
    }

    public async Task<bool> Check()
    {
        try
        {
            using var client = CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.GetAsync(LatestReleaseApi);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return false; // No stable release has been published yet.
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var release = doc.RootElement;
            var tag = release.GetProperty("tag_name").GetString();
            if (!Version.TryParse(tag?.TrimStart('v', 'V'), out var latest) ||
                latest <= Assembly.GetExecutingAssembly().GetName().Version)
                return false;

            // Never select an unrelated/source archive from a release.
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(),
                    $"RSBot.{tag}.zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                _downloadUrl = asset.GetProperty("browser_download_url").GetString();
                if (asset.TryGetProperty("digest", out var digest))
                    _digest = digest.GetString();
                break;
            }
            if (string.IsNullOrEmpty(_downloadUrl))
                return false;
            var body = release.GetProperty("body").GetString();
            rtbUpdateInfo.Rtf = new MarkdownToRtfParser().Parse(body ?? "Update available.");
            lblInfo.Text = $"RSBot {tag} is available. Update and restart?";
            return true;
        }
        catch (Exception ex)
        {
            // An offline startup should not interrupt normal use of the bot.
            Log.Warn($"Update check failed: {ex.Message}");
            return false;
        }
    }

    private async void btnDownload_Click(object sender, EventArgs e)
    {
        if (_installing || string.IsNullOrEmpty(_downloadUrl))
            return;
        _installing = true;
        btnDownload.Enabled = btnSkip.Enabled = false;
        try
        {
            downloadProgress.Visible = true;
            downloadProgress.Value = 0;
            lblInfo.Text = "Downloading update...";
            var tempPath = Path.Combine(Kernel.BasePath, "update_temp");
            Directory.CreateDirectory(tempPath);
            var zipPath = Path.Combine(tempPath, "update.zip");
            using (var client = CreateClient())
            using (var response = await client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await file.WriteAsync(buffer, 0, count);
                    received += count;
                    if (total > 0)
                        downloadProgress.Value = Math.Min(100, received * 100 / total.Value);
                }
                if (total.HasValue && received != total.Value)
                    throw new IOException("The update download is incomplete. Please retry.");
            } // Close the downloaded file before verification or launching the updater.
            if (!string.IsNullOrEmpty(_digest))
            {
                if (!_digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unsupported update checksum.");
                using var file = File.OpenRead(zipPath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file));
                if (!hash.Equals(_digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Update checksum verification failed. Please retry.");
            }

            // Run a copy outside the installation so the updater can replace itself too.
            var runner = Path.Combine(Path.GetTempPath(), "RSBot-Updater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runner);
            foreach (var name in new[] { "RSBot.Updater.exe", "RSBot.Updater.dll",
                "RSBot.Updater.deps.json", "RSBot.Updater.runtimeconfig.json" })
                File.Copy(Path.Combine(Kernel.BasePath, name), Path.Combine(runner, name));

            GlobalConfig.Save();
            PlayerConfig.Save();
            var start = new ProcessStartInfo(Path.Combine(runner, "RSBot.Updater.exe"))
            {
                WorkingDirectory = runner,
                UseShellExecute = false
            };
            start.ArgumentList.Add(Kernel.BasePath);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            using var updater = Process.Start(start) ?? throw new IOException("Could not start the updater.");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _installing = false;
            btnDownload.Enabled = btnSkip.Enabled = true;
            lblInfo.Text = "Update failed. You can retry or skip.";
            MessageBox.Show(this, ex.Message, "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
