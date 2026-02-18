# Simple IP Scanner & DNS Benchmark

A modern, high-performance WPF application for network discovery and DNS performance testing.

![Aesthetic](https://img.shields.io/badge/Aesthetic-Premium-94c744)
![Platform](https://img.shields.io/badge/Platform-Windows-0078d4)
![Framework](https://img.shields.io/badge/Framework-.NET%208-512bd4)

---

## 📥 Download & Quick Start

### 1. Download the Executable
Go to the [**Releases**](https://github.com/ChaseCorbin/SimpleIPScanner/releases) page and download the latest `SimpleIPScanner.exe`. 

### 2. Verify Security (Optional but Recommended)
To ensure the file hasn't been tampered with, you can verify its SHA-256 hash. Open PowerShell in your download folder and run:
```powershell
Get-FileHash .\SimpleIPScanner.exe
```
Compare the resulting hash with the one provided in the `SHA256SUM.txt` file or on the Release page.

### 3. Run the App
- No installation is required. This is a **portable** app.
- Simply double-click `SimpleIPScanner.exe` to start.
- *Note: On first run, Windows SmartScreen may show a warning because the app is not signed with a paid developer certificate. Click "More Info" -> "Run anyway".*

---

## ✨ Key Features

### ⚡ Network Scanner
- **Fast Discovery**: Optimized asynchronous pinging for entire subnets.
- **OS Fingerprinting**: Detects Windows, Apple, and Linux (using the custom **Bow Tie icon**).
- **Vendor Detection**: Instant identification of device manufacturers (Apple, Dell, Sony, etc.).
- **Automatic Port Scanning**: Identifies common open ports (Web, SSH, SMB) automatically.

### 🚀 DNS Benchmark
- **Live Latency Testing**: Compare response times of major DNS providers (Google, Cloudflare, etc.).
- **Uncached Queries**: Uses direct UDP queries with unique subdomains to bypass recursive caches and see "true" server response time.
- **Real-time Stats**: Track Min, Max, and Average latency over a set duration.

---

## 🛠️ For Developers

### Building from Source
1. Clone the repository: `git clone https://github.com/ChaseCorbin/SimpleIPScanner.git`
2. Open the solution in **Visual Studio 2022**.
3. Ensure **.NET 8 SDK** is installed.
4. Build in **Release** mode.

### Technology Stack
- **Language**: C# 12
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Runtime**: .NET 8
- **Networking**: `System.Net.Sockets`, `System.Net.NetworkInformation`

---
Developed by **Chase Corbin**
