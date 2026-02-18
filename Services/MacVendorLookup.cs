using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SimpleIPScanner.Services
{
    /// <summary>
    /// Downloads and caches the full IEEE OUI database (~36,000 entries) for MAC vendor lookups.
    /// Falls back to a small built-in dictionary if the download fails.
    /// </summary>
    public static class MacVendorLookup
    {
        private static Dictionary<string, string>? _ouiMap;
        private static readonly object _lock = new();
        private static readonly string CacheFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "oui_cache.txt");

        // IEEE OUI CSV — the authoritative, complete list of all registered MAC vendors
        private const string OuiUrl = "https://standards-oui.ieee.org/oui/oui.csv";
        private const long MaxCacheSize = 10 * 1024 * 1024; // 10MB limit for safety

        /// <summary>
        /// Initialize the OUI database. Call this at app startup (async, non-blocking).
        /// Downloads from IEEE if no local cache exists or if cache is older than 30 days.
        /// </summary>
        public static async Task InitializeAsync()
        {
            try
            {
                bool needsDownload = true;

                if (File.Exists(CacheFile))
                {
                    var age = DateTime.Now - File.GetLastWriteTime(CacheFile);
                    if (age.TotalDays < 30)
                        needsDownload = false;
                }

                if (needsDownload)
                {
                    await DownloadOuiDatabaseAsync();
                }

                LoadFromCache();
            }
            catch
            {
                // If anything goes wrong, fall back to the built-in dictionary
                LoadFallback();
            }
        }

        private static async Task DownloadOuiDatabaseAsync()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            
            using var response = await client.GetAsync(OuiUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaxCacheSize)
                throw new InvalidOperationException("IEEE OUI database is too large.");

            string csv = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(csv) || !csv.Contains("Registry,Assignment"))
                throw new InvalidOperationException("Invalid IEEE OUI database format.");

            await File.WriteAllTextAsync(CacheFile, csv);
        }

        private static void LoadFromCache()
        {
            if (!File.Exists(CacheFile))
            {
                LoadFallback();
                return;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(CacheFile);

            foreach (var line in lines)
            {
                // IEEE CSV format: Registry,Assignment,Organization Name,Organization Address
                // Example: MA-L,00001C,The Trumpion Microelectronics INC.,"addr..."
                // We need columns 1 (Assignment = 6 hex chars) and 2 (Organization Name)
                try
                {
                    var parts = ParseCsvLine(line);
                    if (parts.Count < 3) continue;

                    string assignment = parts[1].Trim().ToUpperInvariant();
                    string orgName = parts[2].Trim().Trim('"');

                    if (assignment.Length != 6) continue;

                    // Convert "AABBCC" to "AA-BB-CC"
                    string oui = $"{assignment[..2]}-{assignment[2..4]}-{assignment[4..6]}";

                    // Shorten common long names for display
                    orgName = ShortenVendorName(orgName);

                    map.TryAdd(oui, orgName);
                }
                catch { }
            }

            if (map.Count > 100)
            {
                lock (_lock) { _ouiMap = map; }
            }
            else
            {
                LoadFallback();
            }
        }

        /// <summary>
        /// Simple CSV line parser that handles quoted fields with commas inside.
        /// </summary>
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            int start = 0;

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') inQuote = !inQuote;
                else if (line[i] == ',' && !inQuote)
                {
                    result.Add(line[start..i]);
                    start = i + 1;
                }
            }
            result.Add(line[start..]);
            return result;
        }

        /// <summary>
        /// Shortens common verbose vendor names for cleaner display in the UI.
        /// </summary>
        private static string ShortenVendorName(string name)
        {
            // Map of partial matches to short display names
            if (name.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return "Apple";
            if (name.Contains("Samsung", StringComparison.OrdinalIgnoreCase)) return "Samsung";
            if (name.Contains("Ubiquiti", StringComparison.OrdinalIgnoreCase)) return "Ubiquiti";
            if (name.Contains("Google", StringComparison.OrdinalIgnoreCase)) return "Google";
            if (name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return "Microsoft";
            if (name.Contains("Intel ", StringComparison.OrdinalIgnoreCase)) return "Intel";
            if (name.Contains("Dell ", StringComparison.OrdinalIgnoreCase)) return "Dell";
            if (name.Contains("Hewlett Packard", StringComparison.OrdinalIgnoreCase)) return "HP";
            if (name.Contains("HP Inc", StringComparison.OrdinalIgnoreCase)) return "HP";
            if (name.Contains("Cisco", StringComparison.OrdinalIgnoreCase)) return "Cisco";
            if (name.Contains("Netgear", StringComparison.OrdinalIgnoreCase)) return "Netgear";
            if (name.Contains("TP-Link", StringComparison.OrdinalIgnoreCase)) return "TP-Link";
            if (name.Contains("TP-LINK", StringComparison.OrdinalIgnoreCase)) return "TP-Link";
            if (name.Contains("Linksys", StringComparison.OrdinalIgnoreCase)) return "Linksys";
            if (name.Contains("Amazon", StringComparison.OrdinalIgnoreCase)) return "Amazon";
            if (name.Contains("Sonos", StringComparison.OrdinalIgnoreCase)) return "Sonos";
            if (name.Contains("Roku", StringComparison.OrdinalIgnoreCase)) return "Roku";
            if (name.Contains("Raspberry Pi", StringComparison.OrdinalIgnoreCase)) return "Raspberry Pi";
            if (name.Contains("Espressif", StringComparison.OrdinalIgnoreCase)) return "Espressif";
            if (name.Contains("ASUS", StringComparison.OrdinalIgnoreCase)) return "ASUS";
            if (name.Contains("ASUSTek", StringComparison.OrdinalIgnoreCase)) return "ASUS";
            if (name.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) return "Lenovo";
            if (name.Contains("Aruba", StringComparison.OrdinalIgnoreCase)) return "Aruba";
            if (name.Contains("Huawei", StringComparison.OrdinalIgnoreCase)) return "Huawei";
            if (name.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase)) return "Xiaomi";
            if (name.Contains("VMware", StringComparison.OrdinalIgnoreCase)) return "VMware";
            if (name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)) return "Hyper-V";
            if (name.Contains("Sony", StringComparison.OrdinalIgnoreCase)) return "Sony";
            if (name.Contains("LG Elec", StringComparison.OrdinalIgnoreCase)) return "LG";
            if (name.Contains("Motorola", StringComparison.OrdinalIgnoreCase)) return "Motorola";
            if (name.Contains("D-Link", StringComparison.OrdinalIgnoreCase)) return "D-Link";
            if (name.Contains("Belkin", StringComparison.OrdinalIgnoreCase)) return "Belkin";
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "Nvidia";
            if (name.Contains("Synology", StringComparison.OrdinalIgnoreCase)) return "Synology";
            if (name.Contains("QNAP", StringComparison.OrdinalIgnoreCase)) return "QNAP";
            if (name.Contains("Nest Labs", StringComparison.OrdinalIgnoreCase)) return "Nest";
            if (name.Contains("Ring LLC", StringComparison.OrdinalIgnoreCase)) return "Ring";
            if (name.Contains("OnePlus", StringComparison.OrdinalIgnoreCase)) return "OnePlus";
            if (name.Contains("Wyze", StringComparison.OrdinalIgnoreCase)) return "Wyze";
            if (name.Contains("Hon Hai", StringComparison.OrdinalIgnoreCase)) return "Foxconn";
            if (name.Contains("Foxconn", StringComparison.OrdinalIgnoreCase)) return "Foxconn";
            if (name.Contains("Murata", StringComparison.OrdinalIgnoreCase)) return "Murata";
            if (name.Contains("Realtek", StringComparison.OrdinalIgnoreCase)) return "Realtek";
            if (name.Contains("Broadcom", StringComparison.OrdinalIgnoreCase)) return "Broadcom";
            if (name.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase)) return "Qualcomm";
            if (name.Contains("MediaTek", StringComparison.OrdinalIgnoreCase)) return "MediaTek";

            // For others, truncate long names
            if (name.Length > 20)
            {
                // Try to get just the company name (before "Inc.", "LLC", "Corp", etc.)
                int cut = name.IndexOfAny(new[] { ',', ';' });
                if (cut > 0 && cut <= 25) return name[..cut].Trim();

                foreach (var suffix in new[] { " Inc", " LLC", " Corp", " Ltd", " Co.", " GmbH", " S.A" })
                {
                    int idx = name.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                    if (idx > 0 && idx <= 25) return name[..idx].Trim();
                }

                return name.Length > 25 ? name[..22] + "..." : name;
            }

            return name;
        }

        private static void LoadFallback()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Minimal fallback — just the most common vendors
                {"00-50-56","VMware"},{"00-0C-29","VMware"},{"00-15-5D","Hyper-V"},
                {"00-00-0C","Cisco"},{"B8-27-EB","Raspberry Pi"},{"DC-A6-32","Raspberry Pi"},
            };
            lock (_lock) { _ouiMap = map; }
        }

        /// <summary>
        /// Look up the vendor/manufacturer from a MAC address string like "AB-CD-EF-12-34-56".
        /// </summary>
        public static string Lookup(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac) || mac == "N/A") return "";

            // Ensure database is loaded (sync fallback if InitializeAsync hasn't run)
            if (_ouiMap == null)
            {
                LoadFallback();
            }

            string normalized = mac.Replace(":", "-").ToUpperInvariant();
            var parts = normalized.Split('-');
            if (parts.Length < 3) return "";

            string oui = $"{parts[0]}-{parts[1]}-{parts[2]}";

            lock (_lock)
            {
                return _ouiMap!.TryGetValue(oui, out string? vendor) ? vendor : "";
            }
        }
    }
}
