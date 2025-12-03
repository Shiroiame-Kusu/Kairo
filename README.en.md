# Kairo
![Kairo banner](https://github.com/Shiroiame-Kusu/Kairo/raw/preview/Kairo/Assets/banner.png)

> A new-generation LoCyanFrp desktop client, built with C#/.NET and Avalonia.

_[中文 README](README.md)_

## Highlights
- **Unified LoCyanFrp workspace** – sign in, refresh tokens, perform daily check-ins, and inspect account stats in one place.
- **Tunnel lifecycle management** – create, edit, start/stop, delete, and batch-update tunnels with live status indicators.
- **Integrated frpc distribution** – automatically download and verify the correct frpc build per tunnel profile.
- **Built-in dashboards** – monitor traffic usage, node health, and Minecraft server state without leaving the app.
- **Fluent themes** – light/dark switching, custom title bar, and an interface tuned for both mouse and touch.
- **Crash-safe runtime** – crash interception, logging, and diagnostics helpers keep debugging friction low.

## Quick Start

### Binary download
1. Visit the repository’s **Releases** page.
2. Download the latest archive for your platform (Windows/macOS/Linux when available).
3. Extract and run `Kairo` (or the platform-specific executable).
4. First launch prompts for LoCyanFrp credentials or access keys.

### Build from source
Requires **.NET SDK 10.0** plus `git`.

```bash
git clone https://github.com/Shiroiame-Kusu/Kairo.git
cd Kairo
dotnet restore Kairo.sln
dotnet run --project Kairo/Kairo.csproj --configuration Release
```

Produce a self-contained bundle:

```bash
dotnet publish Kairo/Kairo.csproj -c Release -r win-x64 --self-contained true
```

> The build uses Avalonia 11, FluentAvaloniaUI, and a pre-build script (`Components/BuildInfo.sh`). Ensure the script is executable on your platform (`chmod +x`).

## Usage Overview
- **Authentication** – password login, token refresh, access-key retrieval.
- **Daily tasks** – LoCyanFrp check-ins and review traffic rewards.
- **Tunnel operations** – create, duplicate, update, delete, start, stop, and batch-update tunnels using built-in templates.
- **Node tools** – ping nodes, list domains, request random ports, and fetch node statistics.
- **Frpc lifecycle** – download the required release, verify checksum.

## Documentation
- All API/workflow docs are in `docs/` (Chinese). Examples:
  - `docs/获取隧道列表.md` – list and inspect tunnels.
  - `docs/创建隧道.md` – create and configure new tunnels.
  - `docs/创建 Minecraft 联机房间.md` – spin up Minecraft rooms via LoCyanFrp.
  - `docs/鉴权说明.md` & `docs/鉴权验证流程.md` – authentication details.
- Contributions that help translate or extend documentation are appreciated.

## Project Layout
- `Kairo/` – Avalonia UI application (App.axaml, MainWindow, components, utils).
- `Legacy/` – historical WPF client preserved for reference.
- `Updater/` – standalone updater project.
- `docs/` – user/API documentation (Chinese).
- `logs/` – runtime/crash logs (git-ignored).

## Contributing
1. Fork the repo and branch off `preview` for new work.
2. Keep UI changes accessible (light + dark) and document new commands/windows.
3. Run `dotnet format` (or your analyzer of choice) before submitting.
4. Open a PR with motivation, screenshots (for UI changes), and related issues.

Bug reports, feature requests, and localization help are welcome via GitHub Issues.

## License
Kairo is distributed under the **GNU General Public License v3.0**. See `LICENSE` for details.
