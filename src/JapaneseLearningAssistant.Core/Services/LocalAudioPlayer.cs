using System.Diagnostics;

namespace JapaneseLearningAssistant.Core.Services;

public static class LocalAudioPlayer
{
    public static void Play(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("音频文件不存在。", filePath);
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("afplay", $"\"{filePath}\"") { UseShellExecute = false });
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo("xdg-open", $"\"{filePath}\"") { UseShellExecute = false });
    }
}
