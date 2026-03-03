using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using SimpleIPScanner.Models;

namespace SimpleIPScanner.Services
{
    public class SpeedTestService
    {
        /// <summary>
        /// Number of parallel TCP streams to open.
        /// A single stream is throttled by TCP's congestion window ÷ RTT.
        /// 8 parallel streams saturate the link the same way Ookla / fast.com do.
        /// </summary>
        private const int ParallelStreams = 8;

        // Download sources tried in order; first to respond with 200 is used for all streams.
        private static readonly (string Label, string Url)[] _downloadSources =
        {
            ("Cloudflare", "https://speed.cloudflare.com/__down?bytes=1073741824"),  // 1 GB stream
            ("Hetzner-US", "https://ash-speed.hetzner.com/1GB.bin"),                 // Ashburn, VA
            ("Hetzner-DE", "https://speed.hetzner.de/100MB.bin"),                    // Nuremberg, DE
        };

        private const string UploadUrl = "https://speed.cloudflare.com/__up";

        private static readonly (string Name, string IP)[] _pingTargets =
        {
            ("Cloudflare",  "1.1.1.1"),
            ("Google",      "8.8.8.8"),
            ("Quad9",       "9.9.9.9"),
            ("OpenDNS",     "208.67.222.222"),
            ("OpenDNS Alt", "208.67.220.220"),
        };

        private static HttpClient CreateClient(int timeoutSeconds)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleIPScanner/2.2");
            client.DefaultRequestHeaders.Add("Origin", "https://speed.cloudflare.com");
            client.DefaultRequestHeaders.Referrer = new Uri("https://speed.cloudflare.com/");
            return client;
        }

        // ── Download ──────────────────────────────────────────────────────────

        /// <summary>
        /// Opens <see cref="ParallelStreams"/> concurrent download streams for
        /// <paramref name="seconds"/>. Calls <paramref name="onSample"/> with the
        /// aggregate Mbps every ~250 ms. Returns the overall average Mbps.
        /// </summary>
        public async Task<double> RunDownloadAsync(int seconds, Action<double> onSample, CancellationToken ct)
        {
            // Quick probe to find the first reachable source
            string? workingUrl = null;
            Exception? lastEx = null;

            foreach (var (_, url) in _downloadSources)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var probeClient = CreateClient(8);
                    using var probeReq = new HttpRequestMessage(HttpMethod.Get, url);
                    using var probeResp = await probeClient.SendAsync(
                        probeReq, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (probeResp.IsSuccessStatusCode) { workingUrl = url; break; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { lastEx = ex; }
            }

            if (workingUrl == null)
                throw new InvalidOperationException("No download source available.", lastEx);

            var totalBytes     = new long[1]; // shared counter; use Interlocked for thread safety
            var bytesLastSample = 0L;
            var sw             = Stopwatch.StartNew();
            var sampleSw       = Stopwatch.StartNew();
            var deadline       = TimeSpan.FromSeconds(seconds);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var tasks = new Task[ParallelStreams];
            for (int i = 0; i < ParallelStreams; i++)
                tasks[i] = DownloadStreamAsync(workingUrl, deadline, sw, totalBytes, linked.Token);

            // Sample loop: fires every 250 ms, aggregates bytes across all streams
            while (sw.Elapsed < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);

                long   current = Interlocked.Read(ref totalBytes[0]);
                long   delta   = current - bytesLastSample;
                double elapsed = sampleSw.Elapsed.TotalSeconds;
                double mbps    = elapsed > 0 ? delta * 8.0 / 1_000_000.0 / elapsed : 0;
                onSample(Math.Max(0, mbps));
                bytesLastSample = current;
                sampleSw.Restart();
            }

            linked.Cancel();
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }

            double totalSec = sw.Elapsed.TotalSeconds;
            return totalSec > 0 ? Interlocked.Read(ref totalBytes[0]) * 8.0 / 1_000_000.0 / totalSec : 0;
        }

        private static async Task DownloadStreamAsync(
            string url, TimeSpan deadline, Stopwatch sw, long[] totalBytes, CancellationToken ct)
        {
            try
            {
                using var client   = CreateClient((int)deadline.TotalSeconds + 30);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return;

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[65_536];

                while (sw.Elapsed < deadline && !ct.IsCancellationRequested)
                {
                    int read;
                    try   { read = await stream.ReadAsync(buffer, ct); }
                    catch { break; }
                    if (read == 0) break;

                    Interlocked.Add(ref totalBytes[0], read);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        // ── Upload ────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs <see cref="ParallelStreams"/> concurrent upload streams for
        /// <paramref name="seconds"/>. Calls <paramref name="onSample"/> every ~250 ms.
        /// Returns the overall average Mbps.
        /// </summary>
        public async Task<double> RunUploadAsync(int seconds, Action<double> onSample, CancellationToken ct)
        {
            // One shared random payload; each stream reads it concurrently (read-only, no lock needed)
            var payload = new byte[1_048_576];
            Random.Shared.NextBytes(payload);

            var totalBytes      = new long[1];
            var bytesLastSample = 0L;
            var sw              = Stopwatch.StartNew();
            var sampleSw        = Stopwatch.StartNew();
            var deadline        = TimeSpan.FromSeconds(seconds);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var tasks = new Task[ParallelStreams];
            for (int i = 0; i < ParallelStreams; i++)
                tasks[i] = UploadStreamAsync(deadline, sw, payload, totalBytes, linked.Token);

            while (sw.Elapsed < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);

                long   current = Interlocked.Read(ref totalBytes[0]);
                long   delta   = current - bytesLastSample;
                double elapsed = sampleSw.Elapsed.TotalSeconds;
                double mbps    = elapsed > 0 ? delta * 8.0 / 1_000_000.0 / elapsed : 0;
                onSample(Math.Max(0, mbps));
                bytesLastSample = current;
                sampleSw.Restart();
            }

            linked.Cancel();
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }

            double totalSec = sw.Elapsed.TotalSeconds;
            return totalSec > 0 ? Interlocked.Read(ref totalBytes[0]) * 8.0 / 1_000_000.0 / totalSec : 0;
        }

        private static async Task UploadStreamAsync(
            TimeSpan deadline, Stopwatch sw, byte[] payload, long[] totalBytes, CancellationToken ct)
        {
            try
            {
                using var client = CreateClient((int)deadline.TotalSeconds + 30);

                while (sw.Elapsed < deadline && !ct.IsCancellationRequested)
                {
                    using var content = new ByteArrayContent(payload);
                    content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                    using var response = await client.PostAsync(UploadUrl, content, ct);
                    if (!response.IsSuccessStatusCode) break;

                    Interlocked.Add(ref totalBytes[0], payload.Length);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        // ── Ping ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Pings all five well-known servers in parallel, taking the best of 3 attempts each.
        /// </summary>
        public async Task<List<PingServerResult>> PingServersAsync(CancellationToken ct)
        {
            var tasks = new List<Task<PingServerResult>>();

            foreach (var (name, ip) in _pingTargets)
                tasks.Add(PingOneAsync(name, ip, ct));

            var results = await Task.WhenAll(tasks);
            return new List<PingServerResult>(results);
        }

        private static async Task<PingServerResult> PingOneAsync(string name, string ip, CancellationToken ct)
        {
            long best = long.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    using var ping  = new Ping();
                    var reply = await ping.SendPingAsync(ip, 1500);
                    if (reply.Status == IPStatus.Success && reply.RoundtripTime < best)
                        best = reply.RoundtripTime;
                }
                catch { /* timeout or unreachable */ }
            }

            return new PingServerResult
            {
                ServerName = name,
                IP         = ip,
                Latency    = best == long.MaxValue ? -1 : best,
            };
        }
    }
}
