namespace JapaneseLearningAssistant.Core.Services;

internal static class JapaneseAnalysisJsonSchema
{
    public static object Create() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[]
        {
            "detectedLanguage",
            "translatedJapanese",
            "correctedJapanese",
            "naturalJapanese",
            "plainFormJapanese",
            "politeFormJapanese",
            "businessKeigoJapanese",
            "readingOptimizedJapanese",
            "summaryZh",
            "issues",
            "ambiguities",
            "studyTips"
        },
        properties = new
        {
            detectedLanguage = StringSchema(),
            translatedJapanese = StringSchema(),
            correctedJapanese = StringSchema(),
            naturalJapanese = StringSchema(),
            plainFormJapanese = StringSchema(),
            politeFormJapanese = StringSchema(),
            businessKeigoJapanese = StringSchema(),
            readingOptimizedJapanese = StringSchema(),
            summaryZh = StringSchema(),
            issues = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[]
                    {
                        "type",
                        "severity",
                        "original",
                        "suggestion",
                        "explanationZh",
                        "jlptLevel",
                        "exampleJapanese",
                        "exampleChinese",
                        "isHardError"
                    },
                    properties = new
                    {
                        type = StringSchema(),
                        severity = StringSchema(),
                        original = StringSchema(),
                        suggestion = StringSchema(),
                        explanationZh = StringSchema(),
                        jlptLevel = StringSchema(),
                        exampleJapanese = StringSchema(),
                        exampleChinese = StringSchema(),
                        isHardError = new { type = "boolean" }
                    }
                }
            },
            ambiguities = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "sourceExpression", "riskZh", "recommendedJapanese" },
                    properties = new
                    {
                        sourceExpression = StringSchema(),
                        riskZh = StringSchema(),
                        recommendedJapanese = StringSchema()
                    }
                }
            },
            studyTips = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "titleZh", "contentZh", "jlptLevel" },
                    properties = new
                    {
                        titleZh = StringSchema(),
                        contentZh = StringSchema(),
                        jlptLevel = StringSchema()
                    }
                }
            }
        }
    };

    private static object StringSchema() => new { type = "string" };
}
