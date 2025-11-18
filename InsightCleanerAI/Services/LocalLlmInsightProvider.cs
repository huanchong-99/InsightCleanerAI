using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InsightCleanerAI.Infrastructure;
using InsightCleanerAI.Models;
using InsightCleanerAI.Resources;

namespace InsightCleanerAI.Services
{
    /// <summary>
    /// Connects to a user-provided local LLM service (Ollama/koboldcpp/OpenAI-compatible) via HTTP.
    /// </summary>
    public sealed class LocalLlmInsightProvider : IAiInsightProvider
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        public async Task<NodeInsight> DescribeAsync(
            StorageNode node,
            AiConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(configuration.LocalLlmEndpoint) ||
                string.IsNullOrWhiteSpace(configuration.LocalLlmModel))
            {
                DebugLog.Warning($"LocalLlmInsightProvider配置无效 - Endpoint={configuration.LocalLlmEndpoint}, Model={configuration.LocalLlmModel}");
                return NodeInsight.Empty(NodeClassification.Unknown);
            }

            DebugLog.Info($"LocalLlmInsightProvider开始处理 - Model={configuration.LocalLlmModel}, Path={node.FullPath}");

            var prompt = BuildPrompt(node);
            var requestBody = new LocalLlmRequest
            {
                Model = configuration.LocalLlmModel,
                Prompt = prompt,
                Stream = false
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, configuration.LocalLlmEndpoint);
                request.Headers.Accept.ParseAdd("application/json");
                if (!string.IsNullOrWhiteSpace(configuration.LocalLlmApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.LocalLlmApiKey);
                }

                request.Content = JsonContent.Create(requestBody);

                // 使用配置的超时时间,默认300秒
                var timeoutSeconds = configuration.LocalLlmRequestTimeoutSeconds > 0
                    ? configuration.LocalLlmRequestTimeoutSeconds
                    : 300;
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                Infrastructure.DebugLog.Info($"本地LLM请求：{node.Name} (超时={timeoutSeconds}秒)");
                using var response = await HttpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Infrastructure.DebugLog.Warning($"本地LLM请求失败：HTTP {(int)response.StatusCode}");
                    return NodeInsight.Empty(NodeClassification.Unknown);
                }

                var payload = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                Infrastructure.DebugLog.Info($"本地LLM响应：{payload.Substring(0, Math.Min(200, payload.Length))}...");

                var summary = TryExtractSummary(payload);
                if (string.IsNullOrWhiteSpace(summary))
                {
                    Infrastructure.DebugLog.Warning($"本地LLM响应解析失败，使用原始响应");
                    // 如果无法解析，至少返回截断的原始响应
                    summary = payload.Length > 300 ? payload.Substring(0, 300) + "..." : payload;
                }

                Infrastructure.DebugLog.Info($"本地LLM成功：{node.Name}");
                return new NodeInsight(
                    GuessClassification(summary),
                    summary.Trim(),
                    0.65,
                    Strings.LocalLlmSourceNote,
                    false);
            }
            catch (Exception ex)
            {
                Infrastructure.DebugLog.Error($"本地LLM异常：{node.Name}", ex);
                return NodeInsight.Empty(NodeClassification.Unknown);
            }
        }

        private static string BuildPrompt(StorageNode node)
        {
            var builder = new StringBuilder();
            var parentPath = node.FullPath is null
                ? null
                : Path.GetDirectoryName(node.FullPath);

            builder.AppendLine(Strings.LocalLlmPromptIntro);
            builder.AppendLine(string.Format(Strings.LocalLlmPromptName, node.Name));
            var typeLabel = node.IsDirectory ? Strings.LabelDirectory : Strings.LabelFile;
            builder.AppendLine(string.Format(Strings.LocalLlmPromptType, typeLabel));
            builder.AppendLine(string.Format(Strings.LocalLlmPromptPath, node.FullPath ?? node.DisplayPath));
            builder.AppendLine(string.Format(Strings.LocalLlmPromptSize, FormatSize(node.SizeBytes)));
            builder.AppendLine(string.Format(Strings.LocalLlmPromptParent, parentPath ?? Strings.LocalLlmUnknown));
            builder.AppendLine();
            builder.AppendLine(Strings.LocalLlmPromptInstruction);
            builder.AppendLine(Strings.LocalLlmPromptFormat);
            return builder.ToString();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return Strings.LocalLlmUnknown;
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.##}{units[unit]}";
        }

        private static string? TryExtractSummary(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                // Ollama格式: { "response": "..." }
                if (root.TryGetProperty("response", out var responseProperty))
                {
                    var text = responseProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                // OpenAI格式: { "choices": [{ "message": { "content": "..." } }] }
                if (root.TryGetProperty("choices", out var choicesProperty) &&
                    choicesProperty.ValueKind == JsonValueKind.Array)
                {
                    var first = choicesProperty.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                    {
                        if (first.TryGetProperty("message", out var messageProperty) &&
                            messageProperty.TryGetProperty("content", out var contentProperty))
                        {
                            var text = contentProperty.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return text;
                            }
                        }

                        // 有些实现直接在choice中有text字段
                        if (first.TryGetProperty("text", out var textProperty))
                        {
                            var text = textProperty.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return text;
                            }
                        }
                    }
                }

                // 直接content字段
                if (root.TryGetProperty("content", out var content))
                {
                    var text = content.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                // 尝试text字段
                if (root.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                // 尝试output字段
                if (root.TryGetProperty("output", out var outputProp))
                {
                    var text = outputProp.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                Infrastructure.DebugLog.Warning($"JSON解析异常: {ex.Message}");
                return null;
            }

            return null;
        }

        private static NodeClassification GuessClassification(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return NodeClassification.Unknown;
            }

            var text = summary.ToLowerInvariant();
            if (text.Contains("缓存") || text.Contains("cache"))
            {
                return NodeClassification.Cache;
            }

            if (text.Contains("日志") || text.Contains("log"))
            {
                return NodeClassification.Log;
            }

            if (text.Contains("临时") || text.Contains("temp"))
            {
                return NodeClassification.Temporary;
            }

            if (text.Contains("系统") || text.Contains("windows"))
            {
                return NodeClassification.OperatingSystem;
            }

            if (text.Contains("应用") || text.Contains("程序") || text.Contains("app"))
            {
                return NodeClassification.Application;
            }

            return NodeClassification.Unknown;
        }

        private record LocalLlmRequest
        {
            public string Model { get; init; } = string.Empty;

            public string Prompt { get; init; } = string.Empty;

            public bool Stream { get; init; }
        }
    }
}
