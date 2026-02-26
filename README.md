<img width="1913" height="1027" alt="image" src="https://github.com/user-attachments/assets/d96c2b5e-f0db-4192-a6b3-78fd3cf870ee" />
<img width="1915" height="629" alt="image" src="https://github.com/user-attachments/assets/ae6ceb76-0d73-45f5-9d7f-1cd6fb8f13cc" />
<img width="1917" height="1003" alt="image" src="https://github.com/user-attachments/assets/b2109585-7b33-479a-b404-0ed0033b9f8e" />


# Simple IP Scanner & DNS Benchmark

A modern, high-performance WPF application for network discovery, DNS performance testing, and visual traceroute monitoring.

![Version](https://img.shields.io/badge/Version-1.4.2-10B981)
![Platform](https://img.shields.io/badge/Platform-Windows-0078d4)
![Framework](https://img.shields.io/badge/Framework-.NET%208-512bd4)

---

## 📥 Download & Quick Start

### 1. Download the Installer
Go to the [**Releases**](https://github.com/ChaseCorbin/SimpleIPScanner/releases) page and download `SimpleIPScanner-win-Setup.exe`.

### 2. Run the Installer
Double-click `SimpleIPScanner-win-Setup.exe`. It will install the app to `%LocalAppData%\SimpleIPScanner\` and create a Start Menu shortcut automatically.

*Note: Windows SmartScreen may show a warning because the app is not code-signed. Click "More Info" → "Run anyway".*

### 3. Automatic Updates
Once installed, the app silently checks for new releases on startup. When an update is available, a banner appears at the top of the window — click **Update & Restart** to apply it in seconds. No manual downloads needed.

> **Portable version**: A `SimpleIPScanner-win-Portable.zip` is also available on the Releases page for users who prefer a no-install option. Note that the portable version does not support automatic updates.

---

## ✨ Features

### ⚡ Network Scanner
- **Multi-Subnet Scanning**: Add multiple CIDR ranges as chips and scan them all in one session. Results are merged into a single list; each IP shows its subnet prefix inline (e.g. `192.168.0.25/24`).
- **Flexible CIDR Support**: Scan any range from /24 down to /32 — including single-host (/32) and point-to-point (/31) targets.
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
Requires the `vpk` CLI (one-time setup: `dotnet tool install -g vpk`).

```powershell
.\publish_release.ps1 -Version "1.5.0" -GitHubToken "ghp_xxxx"
```

This publishes a self-contained build, packages it with Velopack (`vpk pack`), and uploads the installer and update feed to GitHub Releases (`vpk upload github`). Output goes to `bin\Release\VelopackOutput\`.

### Technology Stack
- **Language**: C# 12
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Runtime**: .NET 8 (self-contained)
- **Networking**: `System.Net.Sockets`, `System.Net.NetworkInformation`, `System.Net.Ping`
- **Auto-Update**: [Velopack](https://velopack.io) — installer packaging and GitHub Releases update feed

---

## 📋 Changelog

### v1.4.2
- **Right-click remote tools** — right-click any scanned device to launch **Remote Desktop (RDP)** or open a **PowerShell remote session** (`Enter-PSSession`) directly from the results grid; uses hostname when available, falls back to IP
- **IP column with CIDR prefix** — subnet is now shown inline as part of the IP (e.g. `192.168.0.25/24`), removing the redundant Subnet column
- **Extended CIDR range** — scanner now accepts /24 through /32; /32 scans a single host, /31 scans both point-to-point addresses (RFC 3021)
- **Traceroute chart pan** — click and drag the latency chart to scroll through historical data; "↩ Live" button snaps back to the live view
- **Traceroute X-axis accuracy** — time labels now reflect the actual visible window instead of raw data timestamps
- UI fixes: dark-themed right-click menu with no icon gutter and green accent hover

### v1.4.1
- Patch release — internal fixes and update pipeline verification

### v1.4.0
- **Auto-update** — app silently checks GitHub Releases on startup; an in-app banner lets you apply updates with one click (powered by Velopack)
- **Installer distribution** — replaced portable single-file exe with `SimpleIPScanner-win-Setup.exe`; installs to `%LocalAppData%`, creates Start Menu shortcut, and handles future updates automatically
- Portable zip still provided for no-install users

### v1.3.0
- **Custom DNS servers** — add your own DNS servers to the benchmark alongside the built-in providers
- **Chart X-axis labels** — traceroute latency timeline now shows readable time markers on the X-axis
- UI polish across multiple panels

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
