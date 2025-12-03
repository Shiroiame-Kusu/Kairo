# Kairo
![Kairo banner](https://github.com/Shiroiame-Kusu/Kairo/raw/preview/Kairo/Assets/banner.png)

> 基于 C# / .NET / Avalonia 打造的新一代 LoCyanFrp 桌面客户端。

[English README](README.en.md)

## 亮点
- **统一工作台**：登录、刷新令牌、签到、账号统计集中管理。
- **隧道全生命周期**：创建/修改/启动/停止/删除/批量操作一站式完成。
- **frpc 集成管理**：自动识别平台并下载匹配的 frpc 版本，校验后即可使用。
- **可视化面板**：内置流量监控、节点状态、Minecraft 服务状态等仪表板。
- **Fluent UI 体验**：明暗主题切换、自定义标题栏，兼顾键鼠与触控。
- **崩溃防护**：异常拦截、日志与调试工具，快速定位问题。

## 快速上手

### 发行版下载
1. 打开仓库的 **Releases** 页面。
2. 根据平台下载最新压缩包（Windows/macOS/Linux）。
3. 解压后运行 `Kairo`（或对应平台的可执行文件）。
4. 首次启动会提示登录 LoCyanFrp 或输入访问密钥。

### 源码编译
依赖 **.NET SDK 10.0** 与 `git`。

```bash
git clone https://github.com/Shiroiame-Kusu/Kairo.git
cd Kairo
dotnet restore Kairo.sln
dotnet run --project Kairo/Kairo.csproj --configuration Release
```

生成自包含包：

```bash
dotnet publish Kairo/Kairo.csproj -c Release -r win-x64 --self-contained true
```

> 构建过程中会使用 Avalonia 11、FluentAvaloniaUI，并执行 `Components/BuildInfo.sh`。请确保脚本具备可执行权限（`chmod +x`）。

## 功能概览
- **账号与鉴权**：账号密码登录、刷新令牌、获取访问密钥。
- **签到与福利**：签到、查看流量数据。
- **隧道管理**：支持创建、复制、更新、删除、启停及批量更新。
- **节点工具**：节点连通性测试、域名列表、随机端口申请、统计信息查询。
- **frpc 生命周期**：下载指定版本、校验文件。

## 文档
- 所有 API 与流程文档位于 `docs/`（中文）。示例：
	- `docs/获取隧道列表.md`：查询并检查隧道状态。
	- `docs/创建隧道.md`：新建并配置隧道流程。
	- `docs/创建 Minecraft 联机房间.md`：通过 LoCyanFrp 搭建 Minecraft 房间。
	- `docs/鉴权说明.md` / `docs/鉴权验证流程.md`：鉴权模式与流程。
- 欢迎 PR 协助翻译或补充文档。

## 项目结构
- `Kairo/`：Avalonia UI 主程序（App.axaml、MainWindow、Components、Utils 等）。
- `Legacy/`：历史 WPF 客户端，保留参考。
- `Updater/`：独立更新器项目。
- `docs/`：用户及 API 文档（中文）。
- `logs/`：运行/崩溃日志（不纳入版本控制）。

## 参与贡献
1. Fork 仓库并从 `preview` 分支创建特性分支。
2. UI 变更需兼容明暗主题，并更新相关文档/截图。
3. 提交前执行 `dotnet format`（或等效分析器）。
4. 提交 PR 时请描述动机、影响范围及截图（如涉及界面）。

Bug 反馈、功能需求、翻译协助均可通过 GitHub Issues 提交。

## 许可证
项目以 **GNU General Public License v3.0** 开源，详情参见根目录 `LICENSE`。