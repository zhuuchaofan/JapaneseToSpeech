# 日语学习桌面应用项目架构设计方案

版本：0.1  
日期：2026-06-04  
定位：面向中文用户的日语写作纠错、表达讲解、敬语转换与朗读训练桌面应用

## 1. 产品定位

本项目不是普通翻译器，而是一个面向日语学习者的 AI 学习工作台。

用户可以输入中文或日语。系统会将中文翻译为日语，或直接分析日语文本，然后给出语法、歧义、自然度、简体/敬语等方面的修改建议，并生成适合跟读训练的日语语音。

核心目标：

- 帮用户写出自然、准确、符合场景的日语。
- 用中文解释错误原因，帮助用户学习，而不是只给结果。
- 支持逐句朗读、慢速跟读、历史复习和常错点统计。

## 2. 目标用户

主要用户：

- 中文母语的日语学习者。
- JLPT N5-N1 学习者。
- 需要练习日语作文、邮件、口语稿、自我介绍的用户。
- 希望通过 AI 纠错和日语语音提高表达能力的用户。

用户痛点：

- 不知道自己的日语哪里不自然。
- 机器翻译给出结果，但不解释为什么这样翻。
- 助词、动词变形、敬语、简体和丁寧語容易混用。
- 想听标准日语朗读，但又希望逐句慢速练习。
- 学过的错误没有沉淀，重复犯同类错误。

## 3. 核心功能蓝图

### 3.1 文本输入

MVP：

- 支持粘贴中文或日语文本。
- 自动识别输入语言。
- 支持用户手动指定输入语言，避免误判。
- 支持按句拆分。

后续：

- Word、PDF、TXT 文件导入。
- 图片 OCR。
- 剪贴板监听。
- 多文本项目管理。

### 3.2 中文转日语

当输入为中文时：

- 先生成直译版，帮助学习者理解对应关系。
- 再生成自然日语版。
- 标注关键表达和可替换说法。
- 提示中文原文中可能造成日语歧义的位置。

### 3.3 日语文本分析

分析维度：

- 语法错误：助词、动词变形、时态、否定、条件、接续。
- 词汇搭配：不自然搭配、中式日语、过硬直译。
- 歧义：主语不清、指代不明、请求对象不清、时间关系不清。
- 文体：普通体、丁寧語、尊敬語、謙譲語、商务表达。
- 自然度：母语者更常用的表达方式。
- 学习等级：标注大致 JLPT 难度。

### 3.4 多版本改写

每次分析后建议输出：

- 修正版：只修正明显问题。
- 自然版：表达更像日语母语者。
- 普通体版：适合朋友、日记、口语练习。
- 丁寧語版：适合一般交流。
- 商务敬语版：适合邮件、客户沟通。
- 朗读优化版：适合 TTS 和跟读。

### 3.5 中文讲解

每个问题都应该包含：

- 原表达。
- 建议表达。
- 问题类型。
- 中文解释。
- 例句。
- JLPT 难度。
- 是否属于硬错误，还是自然度优化。

### 3.6 日语语音与跟读

MVP：

- 对修正版或朗读优化版生成日语语音。
- 支持整段播放。
- 支持逐句播放。
- 支持慢速播放。
- 支持音频下载。

后续：

- 逐句循环播放。
- 跟读录音。
- 用户录音与标准音频对比。
- SRT 字幕导出。
- 假名读音辅助。

### 3.7 学习记录

MVP：

- 保存输入文本、分析结果、音频记录。
- 记录错误类型。
- 支持历史查看。

后续：

- 常错点统计。
- 自动错题卡。
- 间隔复习。
- 按 JLPT 等级整理语法点。

## 4. 技术选型结论

### 4.1 推荐技术栈

推荐主方案：

- 桌面框架：Avalonia UI
- 运行时：.NET 10 LTS
- 语言：C# 14 或项目可用的最新 C# 版本
- UI 架构：MVVM
- 本地数据库：SQLite
- ORM：Entity Framework Core
- AI 分析：Gemini API
- Gemini 接入：优先 Google.GenAI；保留 REST 适配器
- TTS：Google Cloud Text-to-Speech 为主，Azure AI Speech 为备选
- 音频播放：NAudio 或 Avalonia 支持的跨平台音频方案
- 配置与密钥：本地加密配置，后续商业版改为后端代理

### 4.2 为什么推荐 Avalonia

项目是桌面应用，但未来可能希望支持 Windows、macOS、Linux。Avalonia 的优势是：

- 跨平台桌面支持更直接。
- 使用 XAML + MVVM，C# 开发体验接近 WPF。
- 可以在 macOS 上开发并构建 Windows 版本。
- 目标框架可以直接使用 `net10.0`，不需要平台专用 TargetFramework。

如果你明确只做 Windows，并且希望用成熟生态，可以选择 WPF。但本项目后续可能会面向更多学习者，跨平台价值较高，因此推荐 Avalonia。

### 4.3 备选方案对比

| 方案 | 优点 | 缺点 | 结论 |
| --- | --- | --- | --- |
| Avalonia UI | 跨平台、C#、MVVM、适合现代桌面 | 生态小于 WPF | 推荐 |
| WPF | Windows 成熟稳定、资料多 | 仅 Windows | Windows-only 时可选 |
| WinUI 3 | Windows 现代 UI | 仅 Windows，工程复杂度更高 | 不作为 MVP 首选 |
| .NET MAUI | 移动和桌面统一 | 桌面体验和复杂工具型应用不如 Avalonia/WPF 直接 | 不作为桌面 MVP 首选 |

## 5. 总体架构

采用桌面端分层架构：

```text
┌────────────────────────────────────────────┐
│ Desktop UI: Avalonia Views                 │
│ 输入、分析结果、改写版本、语音、历史记录      │
└──────────────────────┬─────────────────────┘
                       │
┌──────────────────────▼─────────────────────┐
│ Presentation: ViewModels                    │
│ 状态管理、命令、表单验证、进度展示           │
└──────────────────────┬─────────────────────┘
                       │
┌──────────────────────▼─────────────────────┐
│ Application Services                       │
│ AnalyzeText、Rewrite、GenerateSpeech、Review │
└──────────────────────┬─────────────────────┘
                       │
┌──────────────────────▼─────────────────────┐
│ Domain Model                               │
│ TextSubmission、AnalysisResult、Issue、Audio │
└──────────────────────┬─────────────────────┘
                       │
┌──────────────────────▼─────────────────────┐
│ Infrastructure                             │
│ Gemini Client、TTS Client、SQLite、FileStore │
└────────────────────────────────────────────┘
```

设计原则：

- UI 不直接调用 Gemini 或 TTS。
- AI 和 TTS 都通过接口注入，方便替换供应商。
- Gemini 返回必须走结构化 JSON，避免 UI 解析自然语言。
- 本地数据库保存学习数据，音频文件保存到本地应用数据目录。
- API Key 不写入代码，不提交仓库。

## 6. 推荐项目结构

```text
JapaneseLearningAssistant/
  src/
    JapaneseLearningAssistant.App/
      Views/
      ViewModels/
      Controls/
      Assets/
      App.axaml
      Program.cs

    JapaneseLearningAssistant.Application/
      UseCases/
      Services/
      DTOs/
      Interfaces/
      Validation/

    JapaneseLearningAssistant.Domain/
      Entities/
      ValueObjects/
      Enums/
      Events/

    JapaneseLearningAssistant.Infrastructure/
      Gemini/
      TextToSpeech/
      Persistence/
      Audio/
      Security/
      FileStorage/

    JapaneseLearningAssistant.Tests/
      Application/
      Infrastructure/
      Domain/

  docs/
    project-architecture.md
    prompts.md
    api-contracts.md
```

## 7. 核心模块设计

### 7.1 TextInputService

职责：

- 清洗输入文本。
- 判断中文/日语。
- 分句。
- 限制最大长度。
- 生成分析请求。

接口示例：

```csharp
public interface ITextInputService
{
    PreparedText Prepare(string rawText, InputLanguageMode languageMode);
}
```

### 7.2 JapaneseAnalysisService

职责：

- 编排中文翻译、日语纠错、歧义分析、文体转换。
- 调用 GeminiClient。
- 校验 AI 返回结构。
- 输出应用层 DTO。

```csharp
public interface IJapaneseAnalysisService
{
    Task<JapaneseAnalysisResult> AnalyzeAsync(
        AnalyzeTextRequest request,
        CancellationToken cancellationToken);
}
```

### 7.3 GeminiClient

职责：

- 封装 Gemini API 调用。
- 支持结构化输出。
- 支持重试、超时、错误处理。
- 记录 token 使用量和成本估算。

```csharp
public interface IGeminiClient
{
    Task<TResponse> GenerateStructuredAsync<TResponse>(
        GeminiStructuredRequest request,
        CancellationToken cancellationToken);
}
```

实现建议：

- `GoogleGenAiGeminiClient`：使用 `Google.GenAI`。
- `RestGeminiClient`：使用 `HttpClient` 直接调用 REST API。
- 初期可以先实现 REST 版本，因为结构可控，便于日志和调试。

### 7.4 RewriteService

职责：

- 管理不同文体转换。
- 保存用户偏好。
- 允许用户对同一文本重新生成某个版本。

文体枚举：

```csharp
public enum JapaneseStyle
{
    Corrected,
    Natural,
    Plain,
    Polite,
    BusinessKeigo,
    ReadingOptimized
}
```

### 7.5 TextToSpeechService

职责：

- 将日语文本转为音频。
- 支持供应商切换。
- 支持声音、语速、音调、格式。
- 支持按句生成音频。

```csharp
public interface ITextToSpeechService
{
    Task<AudioGenerationResult> GenerateAsync(
        TtsRequest request,
        CancellationToken cancellationToken);
}
```

Provider：

- `GoogleCloudTextToSpeechProvider`
- `AzureSpeechTextToSpeechProvider`

### 7.6 StudyRecordService

职责：

- 保存历史提交。
- 保存分析结果。
- 保存错误项。
- 统计用户常犯错误。
- 生成复习卡片。

## 8. 数据模型设计

### 8.1 核心实体

```csharp
public class TextSubmission
{
    public Guid Id { get; set; }
    public string OriginalText { get; set; } = "";
    public string DetectedLanguage { get; set; } = "";
    public string UserSelectedScenario { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class AnalysisResult
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public string TranslatedJapanese { get; set; } = "";
    public string CorrectedJapanese { get; set; } = "";
    public string NaturalJapanese { get; set; } = "";
    public string PlainFormJapanese { get; set; } = "";
    public string PoliteFormJapanese { get; set; } = "";
    public string BusinessKeigoJapanese { get; set; } = "";
    public string ReadingOptimizedJapanese { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class JapaneseIssue
{
    public Guid Id { get; set; }
    public Guid AnalysisResultId { get; set; }
    public string IssueType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string OriginalExpression { get; set; } = "";
    public string SuggestedExpression { get; set; } = "";
    public string ExplanationZh { get; set; } = "";
    public string JlptLevel { get; set; } = "";
    public string ExampleJapanese { get; set; } = "";
    public string ExampleChinese { get; set; } = "";
}

public class AudioRecord
{
    public Guid Id { get; set; }
    public Guid AnalysisResultId { get; set; }
    public string SourceText { get; set; } = "";
    public string Provider { get; set; } = "";
    public string VoiceName { get; set; } = "";
    public double SpeakingRate { get; set; }
    public string FilePath { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
```

### 8.2 错误类型枚举

```csharp
public enum JapaneseIssueType
{
    Particle,
    VerbConjugation,
    Tense,
    Politeness,
    Ambiguity,
    WordChoice,
    Collocation,
    ChineseLikeExpression,
    Naturalness,
    Punctuation,
    Other
}
```

## 9. Gemini 分析输出契约

Gemini 应返回结构化 JSON，建议契约如下：

```json
{
  "detectedLanguage": "Chinese",
  "translatedJapanese": "",
  "correctedJapanese": "",
  "naturalJapanese": "",
  "plainFormJapanese": "",
  "politeFormJapanese": "",
  "businessKeigoJapanese": "",
  "readingOptimizedJapanese": "",
  "summaryZh": "",
  "issues": [
    {
      "type": "Particle",
      "severity": "Error",
      "original": "",
      "suggestion": "",
      "explanationZh": "",
      "jlptLevel": "N4",
      "exampleJapanese": "",
      "exampleChinese": "",
      "isHardError": true
    }
  ],
  "ambiguities": [
    {
      "sourceExpression": "",
      "riskZh": "",
      "recommendedJapanese": ""
    }
  ],
  "studyTips": [
    {
      "titleZh": "",
      "contentZh": "",
      "jlptLevel": "N4"
    }
  ]
}
```

关键要求：

- Prompt 中明确用户是日语学习者。
- 所有解释使用中文。
- 不要只润色，要保留学习价值。
- 区分语法错误和自然度优化。
- 如果输入中文，先指出中文原文可能的歧义，再给日语。
- 输出必须符合 JSON Schema。

## 10. AI 工作流

### 10.1 中文输入流程

```text
用户输入中文
  -> 语言识别
  -> 中文歧义初筛
  -> Gemini 翻译为日语
  -> Gemini 生成自然日语和学习讲解
  -> 生成多文体版本
  -> 生成朗读优化文本
  -> 保存分析结果
  -> 用户选择文本生成语音
```

### 10.2 日语输入流程

```text
用户输入日语
  -> 语言识别
  -> 分句
  -> Gemini 语法和自然度分析
  -> Gemini 生成修正版、多文体版本、中文讲解
  -> 生成朗读优化文本
  -> 保存分析结果
  -> 用户选择文本生成语音
```

### 10.3 失败处理

- Gemini 返回 JSON 解析失败：自动重试一次，并附加“只返回 JSON”的修复提示。
- 超时：允许用户取消，保留草稿。
- 内容过长：按段落拆分，分批分析。
- TTS 失败：保留文本结果，提示用户稍后重试。
- API Key 缺失：显示设置入口。

## 11. TTS 架构

推荐主供应商：Google Cloud Text-to-Speech。

理由：

- 与 Gemini 同属 Google 技术栈，账号和计费管理相对集中。
- 支持日语 `ja-JP` 多种声音，包括 Standard、Wavenet、Neural2、Chirp3-HD 等。
- 支持 SSML，可控制停顿、读音、语速。

备选供应商：Azure AI Speech。

理由：

- C# SDK 和企业环境支持较好。
- 支持 SSML、语速、音调、音频格式控制。
- 适合后续做更强的语音训练功能。

TTS 请求模型：

```csharp
public class TtsRequest
{
    public string Text { get; set; } = "";
    public string LanguageCode { get; set; } = "ja-JP";
    public string VoiceName { get; set; } = "";
    public double SpeakingRate { get; set; } = 0.85;
    public double Pitch { get; set; } = 0;
    public string AudioFormat { get; set; } = "mp3";
    public bool UseSsml { get; set; }
}
```

朗读优化建议：

- 长句拆短。
- 为句号、逗号、顿号添加合理停顿。
- 对数字、英文缩写、专有名词做读音提示。
- 给用户提供“学习原文”和“TTS 优化文本”的切换。

## 12. UI 信息架构

### 12.1 主界面

建议采用三栏或两栏布局：

- 左侧：输入区和参数。
- 中间：AI 分析结果。
- 右侧：学习记录和音频控制。

MVP 可以简化为上下结构：

```text
顶部：场景、文体、语言模式、分析按钮
左侧：输入文本
右侧：输出文本和改写版本
底部：问题列表、中文解释、语音播放
```

### 12.2 主要页面

- 工作台：输入、分析、改写、朗读。
- 历史记录：按日期查看分析结果。
- 错误统计：助词、敬语、动词变形等常错类型。
- 复习卡片：从错误项生成复习内容。
- 设置：API Key、TTS Provider、声音、语速、数据目录。

### 12.3 交互重点

- 分析过程显示进度和可取消按钮。
- 结果区支持版本切换。
- 问题列表点击后高亮原句和建议句。
- 每句旁边有播放按钮。
- 每个错误项可加入复习。

## 13. 本地数据与隐私

MVP 采用本地优先：

- SQLite 保存文本、分析结果、错误项、音频记录。
- 音频文件保存到应用数据目录。
- API Key 使用系统安全存储或本地加密保存。
- 用户可以一键清空历史和音频。

需要明确告知：

- 文本会发送到 Gemini API 进行分析。
- 生成语音时文本会发送到所选 TTS 服务。
- 本地历史默认不上传。

商业版建议：

- 增加后端 API Gateway。
- 桌面端不直接持有 Gemini/TTS 服务密钥。
- 后端负责计费、限流、审计、模型路由。
- 用户数据同步需要明确授权。

## 14. 配置设计

```json
{
  "ai": {
    "provider": "Gemini",
    "model": "gemini-3.5-flash",
    "timeoutSeconds": 60,
    "maxInputCharacters": 8000
  },
  "tts": {
    "provider": "GoogleCloud",
    "languageCode": "ja-JP",
    "voiceName": "ja-JP-Neural2-B",
    "speakingRate": 0.85,
    "audioFormat": "mp3"
  },
  "app": {
    "saveHistory": true,
    "saveAudio": true,
    "theme": "System"
  }
}
```

## 15. MVP 范围

第一版建议只做这些：

1. Avalonia 桌面壳。
2. 文本输入。
3. 自动/手动语言选择。
4. Gemini 分析中文或日语。
5. 结构化展示修正版、自然版、普通体、丁寧語、商务敬语、朗读优化版。
6. 中文错误解释。
7. Google Cloud TTS 生成日语音频。
8. 逐句播放。
9. SQLite 保存历史。
10. 设置页配置 API Key 和 TTS。

暂缓：

- 账号系统。
- 云同步。
- OCR。
- PDF/Word 导入。
- 跟读录音评分。
- 复杂会员计费。

## 16. 里程碑计划

### M1：技术验证

目标：证明核心链路可用。

- 创建 Avalonia 项目。
- 实现 Gemini REST 调用。
- 实现结构化 JSON 解析。
- 实现 Google TTS 调用。
- 播放生成的 MP3。

验收：

- 输入一句中文，输出日语分析。
- 输入一句日语，输出纠错解释。
- 点击按钮生成并播放日语语音。

### M2：MVP 工作台

目标：形成可用的桌面应用。

- 完成主界面。
- 支持多版本输出。
- 支持问题列表和中文解释。
- 支持逐句播放。
- 支持历史记录。

验收：

- 用户能完成一次完整学习流程。
- 分析结果可保存和再次打开。

### M3：学习能力增强

目标：让应用从工具变成学习系统。

- 错误分类统计。
- 复习卡片。
- JLPT 标签。
- 常见错误聚合。
- 用户偏好保存。

### M4：商业化准备

目标：支持发布和后续扩展。

- 后端 API Gateway。
- 用户账号。
- 用量统计。
- 付费套餐。
- 安装包签名。
- 自动更新。

## 17. 测试策略

单元测试：

- 文本分句。
- 语言识别。
- JSON 解析。
- 错误类型映射。
- 配置读取。

集成测试：

- GeminiClient。
- TtsProvider。
- SQLite Repository。

UI 测试：

- 主流程按钮状态。
- 分析中取消。
- 错误项点击高亮。
- 历史记录加载。

人工验收样例：

- 中文输入：`我明天把资料发给你，请确认一下。`
- 日语输入：`明日、資料を送るので、確認してください。`
- 敬语测试：`先生がこの本を読みました。`
- 歧义测试：`这个问题我们确认一下。`

## 18. 风险与对策

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| AI 返回不稳定 | UI 展示失败 | 使用 JSON Schema、重试、结果校验 |
| API Key 泄露 | 安全风险 | 本地加密，商业版改后端代理 |
| TTS 成本增加 | 使用成本高 | 缓存音频，按句复用，限制重生成 |
| 日语讲解不准确 | 学习误导 | Prompt 固化，加入人工测试样例，后续引入规则校验 |
| 文本过长 | 超时或费用高 | 分段分析、字符限制、进度提示 |
| 桌面跨平台兼容 | 发布复杂 | MVP 优先 Windows/macOS，Linux 后续验证 |

## 19. 推荐开发顺序

1. 创建解决方案和 Avalonia 项目。
2. 定义 Domain 实体和 DTO。
3. 实现 Gemini REST 结构化输出调用。
4. 实现一个固定 Prompt 的分析流程。
5. 实现主界面 ViewModel。
6. 实现结果展示。
7. 接入 Google TTS。
8. 实现 SQLite 保存。
9. 加入设置页。
10. 做第一轮人工测试。

## 20. 参考资料

- Microsoft .NET Support Policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- .NET MAUI supported platforms: https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms
- Avalonia Windows platform guide: https://docs.avaloniaui.net/docs/platform-specific-guides/windows
- Gemini API libraries: https://ai.google.dev/gemini-api/docs/libraries
- Gemini structured output: https://ai.google.dev/gemini-api/docs/structured-output
- Google Cloud Text-to-Speech voices: https://cloud.google.com/text-to-speech/docs/voices
- Azure AI Speech TTS quickstart: https://docs.azure.cn/en-us/ai-services/speech-service/get-started-text-to-speech
