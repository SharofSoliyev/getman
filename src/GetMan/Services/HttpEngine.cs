using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>
/// Owns the HTTP stack: per-configuration client pool, a shared cookie jar,
/// connection level timings and challenge based auth retries.
/// </summary>
public sealed class HttpEngine : IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();
    private static readonly AsyncLocal<TimingProbe> Probe = new();

    public CookieContainer Cookies { get; private set; } = new();

    private sealed class TimingProbe
    {
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public double Dns;
        public double Connect;
        public double Tls;
        public double ConnectDoneAt = -1;
        public bool Reused = true;
    }

    public void ClearCookies() => Cookies = new CookieContainer();

    public IEnumerable<Cookie> AllCookies()
    {
        foreach (Cookie c in Cookies.GetAllCookies()) yield return c;
    }

    public async Task<ResponseModel> SendAsync(PreparedRequest req, AppSettings app, Action<string> log, CancellationToken ct)
    {
        var model = new ResponseModel();
        var sw = Stopwatch.StartNew();
        var probe = new TimingProbe();
        Probe.Value = probe;

        try
        {
            var client = GetClient(req.Settings, app);
            var url = UrlUtil.EnsureScheme(req.Url);

            using var message = BuildMessage(req, url);
            model.RequestPreview = req.Dump();

            var timeout = req.Settings.TimeoutMs > 0 ? req.Settings.TimeoutMs : app.RequestTimeoutMs;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout > 0) cts.CancelAfter(timeout);

            var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            double firstByte = sw.Elapsed.TotalMilliseconds;

            // --- Digest challenge -------------------------------------------------
            if (response.StatusCode == HttpStatusCode.Unauthorized && req.Auth?.Type == AuthType.Digest)
            {
                var challenge = response.Headers.WwwAuthenticate
                    .FirstOrDefault(h => string.Equals(h.Scheme, "Digest", StringComparison.OrdinalIgnoreCase))?.ToString();
                if (!string.IsNullOrEmpty(challenge))
                {
                    response.Dispose();
                    log?.Invoke("Digest challenge received - re-sending with credentials");
                    using var retry = BuildMessage(req, url);
                    retry.Headers.Remove("Authorization");
                    retry.Headers.TryAddWithoutValidation("Authorization",
                        AuthApplier.BuildDigestHeader(req.Auth, challenge, req.Method, new Uri(url), req.MaterializeBody()));
                    response = await client.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    firstByte = sw.Elapsed.TotalMilliseconds;
                }
            }

            using (response)
            {
                model.StatusCode = (int)response.StatusCode;
                model.StatusText = ReasonPhrase(response);
                model.HttpVersion = "HTTP/" + response.Version;
                model.FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

                long headerBytes = 0;
                foreach (var h in response.Headers)
                    foreach (var v in h.Value)
                    {
                        model.Headers.Add(new KeyValuePair<string, string>(h.Key, v));
                        headerBytes += h.Key.Length + (v?.Length ?? 0) + 4;
                    }
                foreach (var h in response.Content.Headers)
                    foreach (var v in h.Value)
                    {
                        model.Headers.Add(new KeyValuePair<string, string>(h.Key, v));
                        headerBytes += h.Key.Length + (v?.Length ?? 0) + 4;
                    }

                var maxBytes = Math.Max(1, app.MaxResponseSizeMb) * 1024L * 1024L;
                var body = await ReadBodyAsync(response, maxBytes, cts.Token).ConfigureAwait(false);
                model.RawBody = body;
                model.BodyBytes = body.LongLength;
                model.HeaderBytes = headerBytes;
                model.SizeBytes = headerBytes + body.LongLength;
                model.ContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
                model.BodyText = DecodeBody(body, response.Content.Headers.ContentType?.CharSet);

                CollectCookies(model, response, url);
            }

            sw.Stop();
            model.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            model.Timing = new TimingInfo
            {
                DnsMs = probe.Dns,
                ConnectMs = probe.Connect,
                TlsMs = probe.Tls,
                FirstByteMs = firstByte,
                DownloadMs = Math.Max(0, model.ElapsedMs - firstByte),
                TotalMs = model.ElapsedMs
            };
            return model;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            model.Error = "Request timed out.";
            model.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return model;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            model.Error = "Request cancelled.";
            model.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return model;
        }
        catch (Exception ex)
        {
            sw.Stop();
            model.Error = Describe(ex);
            model.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return model;
        }
        finally
        {
            Probe.Value = null;
        }
    }

    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        var e = ex;
        while (e != null)
        {
            if (sb.Length > 0) sb.Append("  ->  ");
            sb.Append(e.Message);
            e = e.InnerException;
        }
        if (ex is HttpRequestException hre && hre.InnerException is SocketException se)
            sb.Append($" (socket error {se.SocketErrorCode})");
        return sb.ToString();
    }

    private static string ReasonPhrase(HttpResponseMessage r)
    {
        if (!string.IsNullOrEmpty(r.ReasonPhrase)) return r.ReasonPhrase;
        return r.StatusCode.ToString();
    }

    private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                ms.Write(buffer, 0, read);
                break;
            }
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static string DecodeBody(byte[] body, string charset)
    {
        if (body == null || body.Length == 0) return string.Empty;
        Encoding enc = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { enc = Encoding.GetEncoding(charset.Trim('"')); } catch { enc = Encoding.UTF8; }
        }
        try { return enc.GetString(body); }
        catch { return Encoding.UTF8.GetString(body); }
    }

    private void CollectCookies(ResponseModel model, HttpResponseMessage response, string url)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var raw in setCookies)
            {
                var c = ParseSetCookie(raw, url);
                if (c != null) model.Cookies.Add(c);
            }
        }

        try
        {
            var uri = new Uri(UrlUtil.EnsureScheme(url));
            foreach (Cookie c in Cookies.GetCookies(uri))
            {
                if (model.Cookies.Any(x => x.Name == c.Name && x.Domain == c.Domain)) continue;
                model.Cookies.Add(new ResponseCookie
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    Expires = c.Expires == DateTime.MinValue ? "session" : c.Expires.ToString("u"),
                    HttpOnly = c.HttpOnly,
                    Secure = c.Secure
                });
            }
        }
        catch { }
    }

    private static ResponseCookie ParseSetCookie(string raw, string url)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split(';');
        var first = parts[0];
        var eq = first.IndexOf('=');
        if (eq < 0) return null;
        var cookie = new ResponseCookie
        {
            Name = first.Substring(0, eq).Trim(),
            Value = first.Substring(eq + 1).Trim()
        };
        try { cookie.Domain = new Uri(UrlUtil.EnsureScheme(url)).Host; } catch { }

        for (int i = 1; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            var pe = p.IndexOf('=');
            var key = (pe < 0 ? p : p.Substring(0, pe)).Trim().ToLowerInvariant();
            var val = pe < 0 ? string.Empty : p.Substring(pe + 1).Trim();
            switch (key)
            {
                case "domain": cookie.Domain = val; break;
                case "path": cookie.Path = val; break;
                case "expires": cookie.Expires = val; break;
                case "max-age": cookie.Expires = DateTime.UtcNow.AddSeconds(double.TryParse(val, out var s) ? s : 0).ToString("u"); break;
                case "httponly": cookie.HttpOnly = true; break;
                case "secure": cookie.Secure = true; break;
                case "samesite": cookie.SameSite = val; break;
            }
        }
        return cookie;
    }

    private HttpRequestMessage BuildMessage(PreparedRequest req, string url)
    {
        var message = new HttpRequestMessage(new HttpMethod(req.Method), url);

        HttpContent content = null;
        switch (req.Mode)
        {
            case BodyMode.FormData:
                {
                    var boundary = "----GetManBoundary" + Guid.NewGuid().ToString("N");
                    var mp = new MultipartFormDataContent(boundary);
                    foreach (var entry in req.Multipart)
                    {
                        if (entry.IsFile)
                        {
                            if (string.IsNullOrWhiteSpace(entry.FilePath) || !File.Exists(entry.FilePath)) continue;
                            var fileContent = new ByteArrayContent(File.ReadAllBytes(entry.FilePath));
                            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                                string.IsNullOrWhiteSpace(entry.ContentType) ? MimeTypes.Guess(entry.FilePath) : entry.ContentType);
                            mp.Add(fileContent, entry.Name, Path.GetFileName(entry.FilePath));
                        }
                        else
                        {
                            var sc = new StringContent(entry.Value ?? string.Empty, Encoding.UTF8);
                            sc.Headers.ContentType = string.IsNullOrWhiteSpace(entry.ContentType)
                                ? null
                                : new MediaTypeHeaderValue(entry.ContentType);
                            mp.Add(sc, entry.Name);
                        }
                    }
                    content = mp;
                    break;
                }

            case BodyMode.Binary:
                if (!string.IsNullOrWhiteSpace(req.BinaryPath) && File.Exists(req.BinaryPath))
                {
                    content = new ByteArrayContent(File.ReadAllBytes(req.BinaryPath));
                    content.Headers.ContentType = new MediaTypeHeaderValue(MimeTypes.Guess(req.BinaryPath));
                }
                break;

            case BodyMode.None:
                break;

            default:
                {
                    var bytes = req.MaterializeBody();
                    if (bytes.Length > 0 || req.Mode == BodyMode.UrlEncoded)
                    {
                        content = new ByteArrayContent(bytes);
                        var ctHeader = req.GetHeader("Content-Type");
                        var ctValue = !string.IsNullOrWhiteSpace(ctHeader) ? ctHeader : req.ContentType;
                        if (!string.IsNullOrWhiteSpace(ctValue))
                        {
                            if (!MediaTypeHeaderValue.TryParse(ctValue, out var parsed))
                                parsed = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
                            content.Headers.ContentType = parsed;
                        }
                    }
                    break;
                }
        }

        message.Content = content;

        foreach (var h in req.Headers)
        {
            if (string.IsNullOrWhiteSpace(h.Key)) continue;
            var key = h.Key.Trim();
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (content != null && req.Mode != BodyMode.FormData)
                {
                    if (MediaTypeHeaderValue.TryParse(h.Value, out var mt)) content.Headers.ContentType = mt;
                }
                else if (content == null)
                {
                    // No body: keep it as a plain request header.
                    message.Headers.TryAddWithoutValidation(key, h.Value);
                }
                continue;
            }
            if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;

            if (!message.Headers.TryAddWithoutValidation(key, h.Value))
                content?.Headers.TryAddWithoutValidation(key, h.Value);
        }

        if (!req.HasHeader("User-Agent"))
            message.Headers.TryAddWithoutValidation("User-Agent", "GetMan/1.0");
        if (!req.HasHeader("Accept"))
            message.Headers.TryAddWithoutValidation("Accept", "*/*");

        message.Version = req.Settings.HttpVersion switch
        {
            "1.0" => HttpVersion.Version10,
            "1.1" => HttpVersion.Version11,
            "2.0" => HttpVersion.Version20,
            "3.0" => HttpVersion.Version30,
            _ => HttpVersion.Version11
        };
        message.VersionPolicy = req.Settings.HttpVersion == "auto"
            ? HttpVersionPolicy.RequestVersionOrHigher
            : HttpVersionPolicy.RequestVersionExact;

        return message;
    }

    private HttpClient GetClient(RequestSettings settings, AppSettings app)
    {
        var key = string.Join("|",
            settings.VerifySsl, settings.FollowRedirects, settings.MaxRedirects, settings.SendCookies, settings.StoreCookies,
            app.UseCustomProxy, app.ProxyHost, app.ProxyPort, app.ProxyAuth, app.ProxyUsername, app.UseSystemProxy,
            app.ClientCertPath, settings.HttpVersion);

        return _clients.GetOrAdd(key, _ =>
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = settings.FollowRedirects,
                MaxAutomaticRedirections = Math.Max(1, settings.MaxRedirects),
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = settings.SendCookies || settings.StoreCookies,
                CookieContainer = Cookies,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(60),
                EnableMultipleHttp2Connections = true
            };

            var ssl = new SslClientAuthenticationOptions();
            if (!settings.VerifySsl)
                ssl.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            if (!string.IsNullOrWhiteSpace(app.ClientCertPath) && File.Exists(app.ClientCertPath))
            {
                try
                {
                    var cert = string.IsNullOrEmpty(app.ClientCertPassword)
                        ? X509CertificateLoader.LoadPkcs12FromFile(app.ClientCertPath, null)
                        : X509CertificateLoader.LoadPkcs12FromFile(app.ClientCertPath, app.ClientCertPassword);
                    ssl.ClientCertificates = new X509CertificateCollection { cert };
                }
                catch { }
            }
            handler.SslOptions = ssl;

            if (app.UseCustomProxy && !string.IsNullOrWhiteSpace(app.ProxyHost))
            {
                var proxy = new WebProxy(app.ProxyHost, app.ProxyPort);
                if (app.ProxyAuth)
                    proxy.Credentials = new NetworkCredential(app.ProxyUsername, app.ProxyPassword);
                if (!string.IsNullOrWhiteSpace(app.ProxyBypass))
                    proxy.BypassList = app.ProxyBypass.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = app.UseSystemProxy;
            }

            handler.ConnectCallback = async (context, token) =>
            {
                var probe = Probe.Value;
                var sw = Stopwatch.StartNew();
                IPAddress[] addresses;
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
                }
                catch
                {
                    addresses = Array.Empty<IPAddress>();
                }
                if (probe != null) { probe.Dns = sw.Elapsed.TotalMilliseconds; probe.Reused = false; }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    var connectStart = sw.Elapsed.TotalMilliseconds;
                    if (addresses.Length > 0)
                        await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token).ConfigureAwait(false);
                    else
                        await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                    if (probe != null)
                    {
                        probe.Connect = sw.Elapsed.TotalMilliseconds - connectStart;
                        probe.ConnectDoneAt = probe.Clock.Elapsed.TotalMilliseconds;
                    }
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };

            handler.PlaintextStreamFilter = (context, _) =>
            {
                var probe = Probe.Value;
                if (probe != null && probe.ConnectDoneAt >= 0)
                    probe.Tls = Math.Max(0, probe.Clock.Elapsed.TotalMilliseconds - probe.ConnectDoneAt);
                return ValueTask.FromResult(context.PlaintextStream);
            };

            var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.ExpectContinue = false;
            return client;
        });
    }

    /// <summary>A bare client used for OAuth token endpoints.</summary>
    public HttpClient TokenClient => GetClient(new RequestSettings(), new AppSettings());

    public void Dispose()
    {
        foreach (var c in _clients.Values) c.Dispose();
        _clients.Clear();
    }
}
