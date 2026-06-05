namespace JapaneseLearningAssistant.Core.Models;

public sealed class PromptProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
}
