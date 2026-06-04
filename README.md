# 日语学习助手 MVP

面向中文日语学习者的桌面 MVP。支持输入中文或日语，通过 Gemini 生成日语纠错、自然表达、简体/丁寧語/商务敬语版本，并通过 Google Cloud Text-to-Speech 生成日语语音。

## 技术栈

- .NET 10
- Avalonia UI 12
- C#
- Gemini API REST
- Google Cloud Text-to-Speech REST

## 项目结构

```text
src/
  JapaneseLearningAssistant.App/   Avalonia 桌面应用
  JapaneseLearningAssistant.Core/  Gemini、TTS、历史记录等核心逻辑
  JapaneseLearningAssistant.Cli/   命令行验证入口
docs/
  project-architecture.md
```

## 本地配置

推荐使用本地配置文件，不需要每次 `export`。

复制模板：

```bash
cp appsettings.Local.example.json appsettings.Local.json
```

然后编辑 `appsettings.Local.json`：

```json
{
  "geminiApiKey": "你的 Gemini API Key",
  "googleTtsApiKey": "你的 Google Cloud Text-to-Speech API Key",
  "geminiModel": "gemini-3.5-flash",
  "googleTtsVoiceName": "ja-JP-Neural2-B"
}
```

`appsettings.Local.json` 已被 `.gitignore` 排除，不会提交到 git。

也可以继续使用环境变量，程序会优先读取 `appsettings.Local.json`，缺失时再读取：

```bash
export GEMINI_API_KEY="你的 Gemini API Key"
export GOOGLE_TTS_API_KEY="你的 Google Cloud Text-to-Speech API Key"
```

如果只想看界面，可以不配置 Key；点击分析或生成语音时会提示缺少对应配置。

## 构建

```bash
dotnet build src/JapaneseLearningAssistant.Core/JapaneseLearningAssistant.Core.csproj --no-restore
dotnet build src/JapaneseLearningAssistant.Cli/JapaneseLearningAssistant.Cli.csproj --no-restore
dotnet build src/JapaneseLearningAssistant.App/JapaneseLearningAssistant.App.csproj --no-restore
```

## 运行桌面应用

```bash
dotnet run --project src/JapaneseLearningAssistant.App/JapaneseLearningAssistant.App.csproj
```

## 运行 CLI 验证

```bash
dotnet run --project src/JapaneseLearningAssistant.Cli/JapaneseLearningAssistant.Cli.csproj
```

输入文本后，按空行开始分析。

## 本地数据

历史记录和音频默认保存到系统应用数据目录：

- macOS: `~/Library/Application Support/JapaneseLearningAssistant`
- Windows: `%APPDATA%/JapaneseLearningAssistant`
- Linux: `~/.config/JapaneseLearningAssistant`

## 当前 MVP 功能

- 中文/日语输入
- 自动/手动语言模式
- Gemini 结构化 JSON 分析
- 日语修正版、自然版、普通体、丁寧語、商务敬语、朗读优化版
- 中文错误讲解
- Google 日语 TTS 生成 MP3
- 本地音频播放
- 最近历史记录

## 已知限制

- API Key 目前通过本地配置文件或环境变量配置，后续应加设置页和安全存储。
- 历史记录使用本地 JSON，后续可升级 SQLite。
- 当前工具环境无法稳定启动 macOS GUI，已通过项目级构建验证；请在本机桌面会话中运行 Avalonia 应用。
