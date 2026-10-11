using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace WorkChatMini;

internal sealed class AppUpdateFeedInfo
{
    public string Version { get; set; } = string.Empty;
    public string PackageUrl { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
}

internal static class AppUpdateFeed
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static bool IsHttps(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? url) &&
        url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0;

    internal static async Task<AppUpdateFeedInfo> CheckAsync(string feedUrl, CancellationToken token)
    {
        if (!IsHttps(feedUrl)) throw new InvalidDataException("Update feed must use HTTPS.");
        using var client = CreateClient();
        using HttpResponseMessage response = await OpenHttpsAsync(client, new Uri(feedUrl), token);
        if (response.Content.Headers.ContentLength is > 65536)
            throw new InvalidDataException("Update feed response is too large.");
        await using Stream input = await response.Content.ReadAsStreamAsync(token);
        await using var capped = new MemoryStream();
        await CopyLimitedAsync(input, capped, 65536, token);
        capped.Position = 0;
        AppUpdateFeedInfo? feed = await JsonSerializer.DeserializeAsync<AppUpdateFeedInfo>(capped, JsonOptions, token);
        if (feed is null || !AppUpdatePackage.IsValidSha256(feed.PackageSha256) ||
            !IsHttps(feed.PackageUrl) || !AppUpdatePackage.IsNewerVersion(feed.Version, "0.0.0-preview0"))
            throw new InvalidDataException("Update feed has invalid fields.");
        return feed;
    }

    internal static async Task<string> DownloadAsync(AppUpdateFeedInfo feed, CancellationToken token)
    {
        if (!IsHttps(feed.PackageUrl) || !AppUpdatePackage.IsValidSha256(feed.PackageSha256))
            throw new InvalidDataException("Invalid update package address or digest.");
        using var client = CreateClient();
        using HttpResponseMessage response = await OpenHttpsAsync(client, new Uri(feed.PackageUrl), token);
        if (response.Content.Headers.ContentLength > AppUpdatePackage.MaxDownloadBytes)
            throw new InvalidDataException("Update download is too large.");
        string folder = Path.Combine(AppUpdatePackage.WorkDirectory, "Downloads");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (Stream input = await response.Content.ReadAsStreamAsync(token))
            await using (Stream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await CopyLimitedAsync(input, output, AppUpdatePackage.MaxDownloadBytes, token);
            string hash = await AppUpdatePackage.ComputeSha256Async(path, token);
            if (!hash.Equals(feed.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded ZIP SHA-256 differs from the update feed.");
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromMinutes(10) };

    private static async Task<HttpResponseMessage> OpenHttpsAsync(HttpClient client, Uri initial, CancellationToken token)
    {
        Uri next = initial;
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (next.Scheme != Uri.UriSchemeHttps || next.UserInfo.Length != 0)
                throw new InvalidDataException("An update address redirected away from HTTPS.");
            HttpResponseMessage response = await client.GetAsync(next, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                Uri? location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("Invalid update redirect.");
                next = new Uri(next, location);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); }
            catch { response.Dispose(); throw; }
            return response;
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    private static async Task CopyLimitedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("Update exceeds the allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }
}
