using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InsightCleanerAI.Infrastructure;

namespace InsightCleanerAI.Services
{
    /// <summary>
    /// 模型列表获取服务
    /// </summary>
    public class ModelListService
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// 从云端API获取可用模型列表（OpenAI兼容接口）
        /// </summary>
        public async Task<List<string>> GetCloudModelsAsync(string endpoint, string? apiKey, CancellationToken cancellationToken = default)
        {
            var models = new List<string>();

            try
            {
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return models;
                }

                // 尝试构建 /models 接口地址
                var modelsEndpoint = BuildModelsEndpoint(endpoint);

                using var request = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);
                request.Headers.Accept.ParseAdd("application/json");

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                DebugLog.Info($"正在获取云端模型列表：{modelsEndpoint}");
                using var response = await HttpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    DebugLog.Warning($"获取云端模型列表失败：HTTP {(int)response.StatusCode}");
                    return models;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = ParseModelsResponse(content);
                models.AddRange(parsed);

                DebugLog.Info($"成功获取 {models.Count} 个云端模型");
            }
            catch (Exception ex)
            {
                DebugLog.Error("获取云端模型列表异常", ex);
            }

            return models;
        }

        /// <summary>
        /// 从本地LLM服务获取可用模型列表（Ollama / OpenAI兼容）
        /// </summary>
        public async Task<List<string>> GetLocalModelsAsync(string endpoint, string? apiKey, CancellationToken cancellationToken = default)
        {
            var models = new List<string>();

            try
            {
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    return models;
                }

                // 先尝试Ollama风格的接口
                if (await TryGetOllamaModelsAsync(endpoint, models, cancellationToken))
                {
                    return models;
                }

                // 否则尝试OpenAI风格的接口
                if (await TryGetOpenAIStyleModelsAsync(endpoint, apiKey, models, cancellationToken))
                {
                    return models;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Error("获取本地模型列表异常", ex);
            }

            return models;
        }

        private async Task<bool> TryGetOllamaModelsAsync(string baseEndpoint, List<string> models, CancellationToken cancellationToken)
        {
            try
            {
                // Ollama API: GET /api/tags
                var uri = new Uri(baseEndpoint);
                var ollamaEndpoint = new Uri(uri, "/api/tags").ToString();

                using var request = new HttpRequestMessage(HttpMethod.Get, ollamaEndpoint);
                request.Headers.Accept.ParseAdd("application/json");

                DebugLog.Info($"尝试Ollama接口获取模型列表：{ollamaEndpoint}");
                using var response = await HttpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var nameElement))
                        {
                            var name = nameElement.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                models.Add(name);
                            }
                        }
                    }

                    DebugLog.Info($"Ollama接口成功获取 {models.Count} 个模型");
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Warning($"Ollama接口失败：{ex.Message}");
            }

            return false;
        }

        private async Task<bool> TryGetOpenAIStyleModelsAsync(string baseEndpoint, string? apiKey, List<string> models, CancellationToken cancellationToken)
        {
            try
            {
                var modelsEndpoint = BuildModelsEndpoint(baseEndpoint);

                using var request = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);
                request.Headers.Accept.ParseAdd("application/json");

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                DebugLog.Info($"尝试OpenAI风格接口获取模型列表：{modelsEndpoint}");
                using var response = await HttpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = ParseModelsResponse(content);
                models.AddRange(parsed);

                DebugLog.Info($"OpenAI风格接口成功获取 {models.Count} 个模型");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Warning($"OpenAI风格接口失败：{ex.Message}");
            }

            return false;
        }

        private static string BuildModelsEndpoint(string endpoint)
        {
            // 如果已经是完整的/models接口，直接返回
            if (endpoint.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            // 移除可能的chat/completions路径
            var uri = new Uri(endpoint);
            var path = uri.AbsolutePath;

            if (path.Contains("/chat/completions"))
            {
                path = path.Replace("/chat/completions", "/models");
            }
            else if (path.Contains("/v1/chat"))
            {
                path = path.Replace("/v1/chat", "/v1/models");
            }
            else if (!path.EndsWith("/models"))
            {
                // 尝试添加/v1/models
                if (path.EndsWith("/"))
                {
                    path += "v1/models";
                }
                else
                {
                    path += "/v1/models";
                }
            }

            return new UriBuilder(uri) { Path = path }.ToString();
        }

        private static List<string> ParseModelsResponse(string content)
        {
            var models = new List<string>();

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // OpenAI格式：{ "data": [ { "id": "model-name" }, ... ] }
                if (root.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                models.Add(id);
                            }
                        }
                    }
                }
                // 或者直接是数组
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                models.Add(id);
                            }
                        }
                        else if (item.ValueKind == JsonValueKind.String)
                        {
                            var str = item.GetString();
                            if (!string.IsNullOrWhiteSpace(str))
                            {
                                models.Add(str);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Warning($"解析模型列表响应失败：{ex.Message}");
            }

            return models;
        }
    }
}
