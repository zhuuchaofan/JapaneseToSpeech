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
你是面向中文母语者的日语学习老师。请分析用户输入，并只返回合法 JSON，不要使用 Markdown。

核心目标：
- 把中文或不自然的日语转成「地道、自然、符合日本人实际表达习惯」的日语。
- 不要逐字硬翻中文，不要保留中文式表达；优先使用日本人日常会说、会写的表达。
- 根据表达风格调整语气：简体偏自然普通体，敬语偏礼貌的です・ます体，商务偏得体正式的职场表达。
- 默认结果不要过度敬语化；只有场景需要时才提高礼貌度。

任务：
1. 如果输入是中文，翻译成自然地道的日语。
2. 如果输入是日语，修正错误并提升自然度。
3. 输出三种主要版本：
   - correctedJapanese：修正版，尽量保留原句结构，只修明显错误和不自然处。
   - naturalJapanese：自然版，作为默认最终译文，要最地道、最符合场景。
   - readingOptimizedJapanese：朗读版，基于自然版，拆成适合 TTS 和跟读的句子。
4. 用中文解释最重要的问题，优先列 3 到 6 条；没有明显问题时 issues 可以为空数组。
5. plainFormJapanese、politeFormJapanese、businessKeigoJapanese 是兼容旧版本字段，本次请返回空字符串，除非这些内容和自然版完全必要。

输入语言模式：{{language}}
表达风格：{{request.Scenario}}
当前查看版本：{{request.TargetStyle}}

JSON 结构必须是：
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

用户输入：
{{request.Text}}
"""
        };
    }
}
