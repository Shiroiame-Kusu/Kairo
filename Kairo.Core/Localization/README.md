# 多语言与翻译

Kairo（GUI）、kairo-cli 与 Kairo.Core 共用一套多语言系统，目前提供 **简体中文**（默认、原文）与 **English**。

## 切换语言

- **GUI**：「设置 → 外观 → 界面语言」，或登录窗口左下角的语言选择。切换后立即生效，无需重启。
- **CLI**：按以下顺序决定语言：
  1. `--lang <语言>` 参数，例如 `kairo-cli --lang en-US status`
  2. `KAIRO_LANG` 环境变量
  3. 配置文件中的 `language`（与 GUI 共用）
  4. 默认简体中文
- 可用的值：`zh-CN`、`en-US`，或 `system`（跟随系统语言；系统不是中文时使用英文）。

语言保存在 `Settings.json` 的 `language` 字段中。

## 文本放在哪里

每个项目在 `Localization/Languages/<语言代码>.json` 中保存自己的文本，作为嵌入资源编译进程序（兼容 Native AOT）：

| 项目 | 文件 | 内容 |
| --- | --- | --- |
| Kairo.Core | `Kairo.Core/Localization/Languages/*.json` | 接口错误、frpc 下载进度、启动提示等（键以 `core.` 开头） |
| Kairo | `Kairo/Localization/Languages/*.json` | 图形界面文本 |
| Kairo.Cli | `Kairo.Cli/Localization/Languages/*.json` | 命令行输出（键以 `cli.` 开头） |

JSON 可以按模块嵌套，嵌套的层级用点号连接成键：

```json
{
  "tunnels": {
    "title": "隧道列表",
    "deleteConfirm": "确定要删除隧道「{0}」吗？删除后无法恢复。"
  }
}
```

上例的键为 `tunnels.title`、`tunnels.deleteConfirm`。当前语言缺少某个键时会显示简体中文原文，两者都没有时显示键名本身。

### 占位符

`{0}`、`{1}` … 由代码填入（数量、名称等），翻译时必须保留，顺序可以调整。也支持 .NET 格式，例如 `{0:0.00}`。

### 单复数

英文等语言需要区分单复数时，把值写成带 `one` / `other` 的对象；中文直接写字符串即可：

```json
"summary": { "one": "{0} tunnel", "other": "{0} tunnels" }
```

单数形式可以不写数量（例如 `"Started the tunnel"`），但不能出现原文没有的占位符。

### 列表

值为数组时按下标展开（`core.tips.0`、`core.tips.1` …），用于随机提示等场景。

## 在代码中使用

- **XAML**：引入 `xmlns:l="using:Kairo.Localization"`，然后 `Text="{l:Loc tunnels.title}"`。生成的是编译绑定，切换语言后自动刷新。
- **C#**：`L.T("tunnels.title")`、`L.T("tunnels.deleteConfirm", name)`、`L.Plural("tunnels.summary", count)`（命名空间 `Kairo.Core.Localization`）。
- **视图模型**：继承 `ViewModelBase` 的属性里直接调用 `L.T`，切换语言时会自动重新读取。保存在字段里的文本（例如一次性的状态提示）不会自动更新，需要时重写 `OnLanguageChanged`。
- **下拉框选项**：使用 `LocalizedOption`，切换语言时只刷新显示文字，不会重置选中项。
- 日志（`Logger`、`AppLogger` 等）面向开发者，保持中文即可，不需要翻译。

## 添加一种语言

1. 在 `Kairo.Core/Localization/Localizer.cs` 的 `Languages` 列表中加入语言代码和该语言自己的名称，例如 `new LanguageInfo("ja-JP", "日本語")`。
2. 复制三个项目中的 `en-US.json`（或 `zh-CN.json`）为 `<语言代码>.json` 并翻译其中的值，键名保持不变。
3. 运行检查脚本，确认没有遗漏：

```bash
python3 scripts/check-i18n.py
```

脚本会列出代码中用到但语言文件缺少的键、未翻译或多余的键，以及占位符不一致的翻译；加上 `--unused` 还会列出没有被引用的键。
