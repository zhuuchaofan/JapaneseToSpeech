using System.Net;
using System.Text.Json;

namespace JapaneseLearningAssistant.Core.Services;

internal static class GoogleApiErrorFormatter
{
    public static string Format(string serviceName, HttpStatusCode statusCode, string? reasonPhrase, string responseText)
    {
        var code = (int)statusCode;
        var status = "";
        var message = "";

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("status", out var statusElement))
                {
                    status = statusElement.GetString() ?? "";
                }

                if (error.TryGetProperty("message", out var messageElement))
                {
                    message = messageElement.GetString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            message = responseText;
        }

        var friendly = status switch
        {
            "RESOURCE_EXHAUSTED" => $"{serviceName} 额度不足或请求过多。当前错误表示 API Key 是有效的，但对应项目的额度/预付费余额已经耗尽，或触发了限流。请到 Google AI Studio / Google Cloud Console 检查 Billing、配额和项目余额。",
            "PERMISSION_DENIED" => $"{serviceName} 权限不足。请确认 API 已启用、Key 属于当前项目，并且该 Key 没有被来源或 API 范围限制挡住。",
            "UNAUTHENTICATED" => $"{serviceName} 认证失败。请检查 API Key 是否填写正确。",
            "INVALID_ARGUMENT" => $"{serviceName} 请求参数有误。请检查模型名、语音名或输入内容。",
            _ when code == 400 => $"{serviceName} 请求格式有误。请检查配置和输入内容。",
            _ when code == 401 => $"{serviceName} API Key 无效或未授权。请检查本地配置文件。",
            _ when code == 403 => $"{serviceName} 权限不足或服务未启用。请检查项目权限、API 启用状态和 Key 限制。",
            _ when code == 429 => $"{serviceName} 额度不足或请求过多。请检查账单、预付费余额和 API 配额。",
            _ => $"{serviceName} 调用失败：{code} {reasonPhrase}"
        };

        if (!string.IsNullOrWhiteSpace(message))
        {
            return $"{friendly}\n\nGoogle 返回：{message}";
        }

        return friendly;
    }

    public static string FormatSdkException(string serviceName, Exception exception)
    {
        var message = exception.Message;

        if (message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("prepayment credits are depleted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.OrdinalIgnoreCase))
        {
            return $"{serviceName} 额度不足或请求过多。API Key 可能是有效的，但对应项目的额度/预付费余额不可用，或触发了限流。\n\nSDK 返回：{message}";
        }

        if (message.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return $"{serviceName} API Key 无效或未授权。请检查 appsettings.Local.json。\n\nSDK 返回：{message}";
        }

        if (message.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return $"{serviceName} 权限不足或服务未启用。请检查项目权限、API 启用状态和 Key 限制。\n\nSDK 返回：{message}";
        }

        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            return $"{serviceName} 模型不存在或当前 Key 无权访问该模型。请检查 geminiModel 配置。\n\nSDK 返回：{message}";
        }

        return $"{serviceName} 调用失败。\n\nSDK 返回：{message}";
    }
}
