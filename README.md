# 日语学习助手 MVP

面向中文日语学习者的桌面 MVP。支持输入中文或日语，通过 Gemini、OpenAI、DeepSeek 或小米 MiMo 生成日语纠错、地道自然表达、朗读优化文本和中文问题讲解，并通过 Google Cloud Text-to-Speech 生成日语语音。

## 技术栈

- .NET 10
- Avalonia UI 12
- C#
- Gemini API
- OpenAI Responses API
- DeepSeek Chat Completions API
- Xiaomi MiMo OpenAI-compatible API
- Google Cloud Text-to-Speech REST

## 项目结构

```text
src/
  JapaneseLearningAssistant.App/   Avalonia 桌面应用
  JapaneseLearningAssistant.Core/  分析、TTS、缓存、历史记录等核心逻辑
  JapaneseLearningAssistant.Cli/   命令行验证入口
docs/
  project-architecture.md
```

## 本地配置

推荐使用本地配置文件，不需要每次设置环境变量。

复制模板：

```bash
cp appsettings.Local.example.json appsettings.Local.json
```

然后编辑 `appsettings.Local.json`：

```json
{
  "analysisProvider": "Gemini",
  "geminiApiKey": "你的 Gemini API Key",
  "geminiModel": "gemini-3.5-flash",
  "openAiApiKey": "你的 OpenAI API Key",
  "openAiModel": "gpt-4.1",
  "deepSeekApiKey": "你的 DeepSeek API Key",
  "deepSeekModel": "deepseek-v4-flash",
  "miMoApiKey": "你的小米 MiMo API Key",
  "miMoModel": "mimo-v2.5-pro",
  "googleTtsApiKey": "你的 Google Cloud Text-to-Speech API Key",
  "googleTtsVoiceName": "ja-JP-Neural2-C"
}
```

`analysisProvider` 支持：

- `Gemini`
- `OpenAI`
- `DeepSeek`
- `MiMo`

`appsettings.Local.json` 已被 `.gitignore` 排除，不会提交到 git。

也可以继续使用环境变量，程序会优先读取 `appsettings.Local.json`，缺失时再读取环境变量：

```bash
export ANALYSIS_PROVIDER="OpenAI"
export GEMINI_API_KEY="你的 Gemini API Key"
export GEMINI_MODEL="gemini-3.5-flash"
export OPENAI_API_KEY="你的 OpenAI API Key"
export OPENAI_MODEL="gpt-4.1"
export DEEPSEEK_API_KEY="你的 DeepSeek API Key"
export DEEPSEEK_MODEL="deepseek-v4-flash"
export MIMO_API_KEY="你的小米 MiMo API Key"
export MIMO_MODEL="mimo-v2.5-pro"
export GOOGLE_TTS_API_KEY="你的 Google Cloud Text-to-Speech API Key"
export GOOGLE_TTS_VOICE_NAME="ja-JP-Neural2-C"
```

如果只想看界面，可以不配置 Key；点击分析或播放时会提示缺少对应配置。

## API 供应商

- Gemini：使用 Google GenAI SDK，要求返回 `application/json`。
- OpenAI：使用 Responses API 和 `json_schema` 结构化输出。
- DeepSeek：使用 OpenAI-compatible Chat Completions，启用 `response_format: { "type": "json_object" }`。
- MiMo：使用小米 OpenAI-compatible Chat Completions，基础地址为 `https://api.xiaomimimo.com/v1`，鉴权 header 使用 `api-key`。

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
- Gemini、OpenAI、DeepSeek、MiMo 多供应商分析
- 修正版、自然版、朗读版
- 中文问题讲解
- Google 日语 TTS 生成 MP3
- 整段播放、逐句播放、循环和进度控制
- 最近历史记录和双击恢复

## 已知限制

- API Key 目前通过本地配置文件或环境变量配置，后续会加入设置页和更安全的本地存储。
- 历史记录使用本地 JSON，后续可升级 SQLite。
- 多供应商已经在代码层接入，仍建议分别用真实 API Key 做一次端到端验证。
