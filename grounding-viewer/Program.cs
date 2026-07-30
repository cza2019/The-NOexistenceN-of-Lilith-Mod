using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LilithGroundingViewer;

internal static class Program
{
    private const string MutexPrefix = @"Local\LilithGroundingViewer.";
    private const string PipePrefix = "LilithGroundingViewer.";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }
        var externalTestIndex = Array.FindIndex(args, item =>
            item.Equals("--smoke-https", StringComparison.OrdinalIgnoreCase));
        if (externalTestIndex >= 0 && externalTestIndex + 1 < args.Length)
        {
            return IsAllowedExternalLink(args[externalTestIndex + 1]) ? 0 : 4;
        }

        if (!TryReadArguments(args, out var url, out var parentPid))
        {
            return 2;
        }

        using var mutex = new Mutex(true, MutexPrefix + parentPid, out var ownsMutex);
        if (!ownsMutex)
        {
            if (SendToPrimaryAsync(parentPid, url).GetAwaiter().GetResult()) return 0;
            OpenInDefaultBrowser(url);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var cancellation = new CancellationTokenSource();
        using var form = new GroundingForm(url, parentPid);
        var pipeTask = RunPipeServerAsync(parentPid, form.NavigateTo, cancellation.Token);
        Application.Run(form);
        cancellation.Cancel();
        try
        {
            pipeTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected when the primary window exits.
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            // Pipe disposal may surface as IO cancellation on some Windows builds.
        }
        return 0;
    }

    internal static bool IsAllowedLoopbackPage(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback
            || !uri.AbsolutePath.StartsWith("/v1/grounding/", StringComparison.Ordinal))
        {
            return false;
        }
        return uri.Port > 0;
    }

    internal static bool IsAllowedExternalLink(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }

    internal static void OpenInDefaultBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Browser fallback is best effort; no navigation data is logged.
        }
    }

    private static bool TryReadArguments(string[] args, out string url, out int parentPid)
    {
        url = string.Empty;
        parentPid = 0;
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals("--url", StringComparison.OrdinalIgnoreCase))
            {
                url = args[++index];
            }
            else if (args[index].Equals("--parent-pid", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(args[++index], out parentPid);
            }
        }
        return parentPid > 0 && IsAllowedLoopbackPage(url);
    }

    private static async Task<bool> SendToPrimaryAsync(int parentPid, string url)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    PipePrefix + parentPid,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await pipe.ConnectAsync(250).ConfigureAwait(false);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync(url).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
            catch (IOException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        return false;
    }

    private static async Task RunPipeServerAsync(
        int parentPid,
        Action<string> navigate,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipePrefix + parentPid,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(pipe);
            var url = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (url is not null && IsAllowedLoopbackPage(url))
            {
                navigate(url);
            }
        }
    }
}

internal sealed class GroundingForm : Form
{
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    private readonly int parentPid;
    private readonly System.Windows.Forms.Timer parentTimer = new() { Interval = 1000 };
    private string pendingUrl;
    private string? userDataFolder;
    private bool initialized;

    public GroundingForm(string initialUrl, int parentPid)
    {
        pendingUrl = initialUrl;
        this.parentPid = parentPid;
        Text = "Google Search grounding";
        Width = 960;
        Height = 720;
        MinimumSize = new Size(620, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(webView);
        Shown += OnShown;
        FormClosed += OnFormClosed;
        parentTimer.Tick += CheckParent;
        parentTimer.Start();
    }

    public void NavigateTo(string url)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => NavigateTo(url));
            return;
        }
        pendingUrl = url;
        if (initialized && webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Navigate(url);
            Activate();
        }
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        try
        {
            var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                throw new WebView2RuntimeNotFoundException("WebView2 Evergreen Runtime was not found.");
            }
            userDataFolder = Path.Combine(
                Path.GetTempPath(),
                "LilithGroundingViewer",
                $"{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(userDataFolder);
            var options = new CoreWebView2EnvironmentOptions("--disable-http-cache --disk-cache-size=1");
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await webView.EnsureCoreWebView2Async(environment);
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = true;
            webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                Text = webView.CoreWebView2.DocumentTitle;
            initialized = true;
            webView.CoreWebView2.Navigate(pendingUrl);
        }
        catch (Exception)
        {
            Program.OpenInDefaultBrowser(pendingUrl);
            Close();
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase)
            || Program.IsAllowedLoopbackPage(args.Uri))
        {
            return;
        }
        args.Cancel = true;
        if (Program.IsAllowedExternalLink(args.Uri))
        {
            Program.OpenInDefaultBrowser(args.Uri);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Program.IsAllowedExternalLink(args.Uri))
        {
            Program.OpenInDefaultBrowser(args.Uri);
        }
    }

    private void CheckParent(object? sender, EventArgs eventArgs)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            if (parent.HasExited) Close();
        }
        catch (Exception)
        {
            Close();
        }
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        parentTimer.Stop();
        webView.Dispose();
        if (string.IsNullOrWhiteSpace(userDataFolder)) return;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Directory.Delete(userDataFolder, true);
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(100);
            }
            catch
            {
                break;
            }
        }
    }
}
