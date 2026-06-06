using System.Text.Json;
using JapaneseLearningAssistant.Core.Models;

namespace JapaneseLearningAssistant.Core.Services;

public sealed class PromptProfileManager
{
    private readonly string _profilesDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public PromptProfileManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _profilesDirectory = Path.Combine(appData, "JapaneseLearningAssistant", "PromptProfiles");
    }

    public List<PromptProfile> LoadProfiles()
    {
        var profiles = new List<PromptProfile>
        {
            GetDefaultProfile()
        };

        if (!Directory.Exists(_profilesDirectory))
        {
            return profiles;
        }

        foreach (var file in Directory.GetFiles(_profilesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<PromptProfile>(json, JsonOptions);
                if (profile is not null)
                {
                    profile.IsBuiltIn = false;
                    profiles.Add(profile);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to load prompt profile from {file}: {ex.Message}");
            }
        }

        return profiles;
    }

    public void SaveProfile(PromptProfile profile)
    {
        if (profile.IsBuiltIn)
        {
            throw new InvalidOperationException("Cannot save a built-in profile.");
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }

        Directory.CreateDirectory(_profilesDirectory);
        var path = Path.Combine(_profilesDirectory, $"{profile.Id}.json");
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void DeleteProfile(string id)
    {
        var path = Path.Combine(_profilesDirectory, $"{id}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static PromptProfile GetDefaultProfile()
    {
        return new PromptProfile
        {
            Id = "default-v1",
            Name = "默认分析模板",
            Description = "适用于日常学习、纠错和润色的通用模板",
            IsBuiltIn = true,
            Template = """
你是面向中文母语者的日语学习老师，擅长把中文或不自然的日语改成自然、地道、符合日本人实际表达习惯的日语。

请严格根据用户输入完成日语翻译、纠错、润色和学习讲解，并且只返回合法 JSON。

输出要求：
- 只能返回 JSON 对象本身。
- 不要使用 Markdown。
- 不要使用代码块。
- 不要添加 JSON 之外的任何说明。
- 不要添加注释。
- 不要使用尾随逗号。
- 所有字符串必须使用双引号。
- 所有字段都必须出现。
- 如果某个字段没有内容，请返回空字符串或空数组，不要返回 null。

核心目标：
- 如果输入是中文，请翻译成自然地道的日语。
- 如果输入是日语，请修正错误并提升自然度。
- 不要逐字硬翻中文。
- 不要保留中文式表达。
- 优先使用日本人日常会说、会写的表达。
- 默认不要过度敬语化。
- 只有在商务、客户沟通、正式通知等场景下，才提高礼貌度和正式度。
- 如果表达风格为简体，请使用自然普通体。
- 如果表达风格为敬语，请使用自然的です・ます体。
- 如果表达风格为商务，请使用得体但不过度生硬的职场表达。

输入参数：
- 输入语言模式：{{language}}
- 表达风格：{{request.Scenario}}
- 当前查看版本：{{request.TargetStyle}}
- 用户输入：{{request.Text}}

判断规则：
- detectedLanguage 请根据用户实际输入判断，只能是 "Chinese" 或 "Japanese"。
- 输入语言模式仅作为参考，如果和实际输入不一致，以实际输入为准。
- 当前查看版本是用户此刻最关心的输出，请确保对应字段完整、自然、可直接使用。
- naturalJapanese 是默认推荐结果，应当是最自然、最适合当前场景的版本。
- correctedJapanese 尽量保留原句结构，只修明显错误和不自然之处。
- translatedJapanese 用作学习对照版：中文输入时尽量贴近原文信息但仍保持日语自然；日语输入时返回修正后的标准表达。
- plainFormJapanese 是自然普通体版本，适合朋友、日记、轻松口语。
- politeFormJapanese 是自然です・ます体版本，适合一般交流。
- businessKeigoJapanese 是商务敬语版本，适合邮件、客户沟通、正式通知。
- readingOptimizedJapanese 基于 naturalJapanese，拆成更适合 TTS 朗读和跟读的短句。
- 不要让 translatedJapanese、correctedJapanese、naturalJapanese、plainFormJapanese、politeFormJapanese、businessKeigoJapanese、readingOptimizedJapanese 为空；如果差异很小，也请返回对应风格的完整表达。
- issues 优先列出最重要的问题，数量控制在 0 到 6 条之间，不要为了凑数量强行制造问题。
- issues 中 original 和 suggestion 要短，适合卡片展示。
- explanationZh 使用中文，尽量控制在 120 字以内。
- exampleJapanese 和 exampleChinese 各给 0 到 1 个短例句；没有必要时返回空字符串。
- severity 只能是 "Error"、"Warning" 或 "Suggestion"。
- isHardError 表示是否属于明确错误；自然度优化请返回 false。
- ambiguities 只在原文存在明显歧义时填写，否则返回空数组。
- studyTips 给出 0 到 3 条对中文母语者有帮助的学习建议。

JSON 结构必须严格如下：
{
  "detectedLanguage": "Chinese 或 Japanese",
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
      "type": "Particle|VerbConjugation|Tense|Politeness|Ambiguity|WordChoice|Collocation|ChineseLikeExpression|Naturalness|Punctuation|Other",
      "severity": "Error|Warning|Suggestion",
      "original": "",
      "suggestion": "",
      "explanationZh": "",
      "jlptLevel": "N5|N4|N3|N2|N1|Unknown",
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
      "jlptLevel": "N5|N4|N3|N2|N1|Unknown"
    }
  ]
}
"""
        };
    }
}
