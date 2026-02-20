<img width="1913" height="1027" alt="image" src="https://github.com/user-attachments/assets/d96c2b5e-f0db-4192-a6b3-78fd3cf870ee" />
<img width="1915" height="629" alt="image" src="https://github.com/user-attachments/assets/ae6ceb76-0d73-45f5-9d7f-1cd6fb8f13cc" />
<img width="1918" height="1003" alt="image" src="https://github.com/user-attachments/assets/4cbc116a-94fe-4f69-9370-bc8fedceda8f" />

# Simple IP Scanner & DNS Benchmark

A modern, high-performance WPF application for network discovery, DNS performance testing, and visual traceroute monitoring.

![Version](https://img.shields.io/badge/Version-1.2.0-10B981)
![Platform](https://img.shields.io/badge/Platform-Windows-0078d4)
![Framework](https://img.shields.io/badge/Framework-.NET%208-512bd4)

---

## 📥 Download & Quick Start

### 1. Download the Executable
Go to the [**Releases**](https://github.com/ChaseCorbin/SimpleIPScanner/releases) page and download the latest `SimpleIPScanner.exe`.

### 2. Verify Security (Optional but Recommended)
To ensure the file hasn't been tampered with, verify its SHA-256 hash. The current build hash is listed in `SHA256SUM.txt` on the release page.

Open PowerShell in your download folder and run:
```powershell
Get-FileHash .\SimpleIPScanner.exe
```
Compare the resulting hash with the one provided in `SHA256SUM.txt` on the Release page.

### 3. Run the App
- No installation required. This is a **portable** single-file executable.
- Simply double-click `SimpleIPScanner.exe` to start.
- *Note: On first run, Windows SmartScreen may show a warning because the app is not code-signed. Click "More Info" → "Run anyway".*

---

## ✨ Features

### ⚡ Network Scanner
- **Multi-Subnet Scanning**: Add multiple CIDR ranges as chips and scan them all in one session. Results are merged into a single list with a Subnet column.
- **Auto-Detect Subnets**: Automatically discovers all connected subnets from your active network interfaces.
- **Fast Discovery**: Concurrent async pinging with semaphore throttling (up to 50 simultaneous probes).
- **OS Fingerprinting**: Detects Windows, Apple, and Linux devices via TTL heuristics.
- **Vendor Detection**: Identifies device manufacturers from MAC OUI (IEEE database).
- **Hostname Resolution**: Two-step fallback — standard DNS PTR lookup, then NetBIOS Node Status (UDP 137) for Windows machines not registered in DNS.
- **Automatic Port Scanning**: Identifies common open ports (HTTP, HTTPS, SSH, RDP, SMB, etc.).

### 🚀 DNS Benchmark
- **Live Latency Testing**: Compare cached and uncached response times across major DNS providers (Google, Cloudflare, OpenDNS, Quad9, and more).
- **Uncached Queries**: Direct UDP queries with randomized transaction IDs and unique subdomains to bypass caches and measure true resolver speed.
- **Configurable Duration**: Choose 5, 15, or 30 second test runs.
- **Real-time Stats**: Min, Max, and Average latency tracked live.

### 🗺️ Visual Traceroute
- **Live Hop-by-Hop Monitoring**: Continuous traceroute to one or more targets simultaneously.
- **Performance Timeline**: Interactive latency chart with 1m / 5m / 10m / 1h / 2h / 8h zoom windows.
- **Multi-Target**: Add any number of hosts and monitor them in parallel.
- **Packet Loss & Jitter**: Track packet loss percentage and average latency per session.

---

## 🛠️ For Developers

### Building from Source
1. Clone the repository: `git clone https://github.com/ChaseCorbin/SimpleIPScanner.git`
2. Open the solution in **Visual Studio 2022** or use the CLI.
3. Ensure **.NET 8 SDK** is installed.
4. Build: `dotnet build` or build in Release mode in Visual Studio.

### Publishing a Release
Run the included script to produce a self-contained single-file executable with SHA-256 hash:
```powershell
.\publish_release.ps1
```
Output goes to `bin\Release\Publish\`.

### Technology Stack
- **Language**: C# 12
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Runtime**: .NET 8 (self-contained, no install required)
- **Networking**: `System.Net.Sockets`, `System.Net.NetworkInformation`, `System.Net.Ping`
- **No external NuGet packages**

---

## 📋 Changelog

### v1.2.0
- **Multi-subnet / VLAN scanning** — add multiple CIDR ranges as chips; sequential scan merges results into one list with a Subnet column
- **Auto-detect subnets** — discovers all connected NICs and populates the chip list automatically
- **Visual Traceroute tab** — continuous multi-target traceroute with live latency timeline chart, packet loss, and per-session stats
- **NetBIOS hostname fallback** — resolves Windows machine names (UDP 137) when DNS PTR returns nothing
- **Switch stack logo** — vector icon rendered in-app; no image file dependency; appears in header, title bar, and taskbar
- **Emerald color theme** — replaced lime-green accent with emerald (#10B981 / #059669)
- **Dark-themed ComboBox** — DNS duration picker now matches the app style
- **Performance**: async DNS resolution, `ConcurrentBag` for results, yield-return IP range, cached `SortKey`, `Dispatcher.BeginInvoke` in hot paths
- **Reliability**: randomized DNS transaction IDs, `volatile` OUI map reference, `Ping` disposal fix in traceroute service

### v1.0.0
- Initial release: Network Scanner, DNS Benchmark, OS detection, MAC vendor lookup, port scanning

---

Developed by **Chase Corbin**
