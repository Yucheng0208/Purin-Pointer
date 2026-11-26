using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PurinPointer;

/// <summary>Checks the project's latest GitHub release against this build's version.</summary>
public static class UpdateChecker
{
    public const string ProjectUrl = "https://github.com/Yucheng0208/Purin-Pointer";
    public const string ReleasesUrl = ProjectUrl + "/releases/latest";
    const string LatestApi = "https://api.github.com/repos/Yucheng0208/Purin-Pointer/releases/latest";

    public static string AppVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var v = info ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0";
            int plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    public enum Outcome { UpdateAvailable, UpToDate, NoRelease }

    public static async Task<(Outcome Outcome, string? Tag)> CheckAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PurinPointer-update-check");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.GetAsync(LatestApi);
        if (response.StatusCode == HttpStatusCode.NotFound) return (Outcome.NoRelease, null);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (tag is null) return (Outcome.NoRelease, null);
        return (PurinPointer.Core.Versions.IsNewer(tag, AppVersion) ? Outcome.UpdateAvailable : Outcome.UpToDate, tag);
    }

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
