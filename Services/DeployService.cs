using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CFDeployer.Models;

namespace CFDeployer.Services
{
    public class DeployService
    {
        private readonly string _proxyUrl;
        private readonly string _proxyKey;

        // 单例 HttpClient
        private static readonly Lazy<HttpClient> _lazyClient = new(() =>
        {
            var handler = new HttpClientHandler
            {
                Proxy = null,
                UseProxy = false,
                UseDefaultCredentials = false,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                MaxConnectionsPerServer = 10
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            client.DefaultRequestHeaders.Add("User-Agent", "CFDeployer-WPF/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            return client;
        });

        private static HttpClient SharedClient => _lazyClient.Value;

        public DeployService(string proxyUrl, string proxyKey)
        {
            _proxyUrl = proxyUrl?.TrimEnd('/') ?? "";
            _proxyKey = proxyKey ?? "";

            if (string.IsNullOrWhiteSpace(_proxyUrl))
            {
                throw new ArgumentException(
                    "代理URL未配置。由于部署需要绕过浏览器CORS限制，必须通过Cloudflare Worker代理进行。请先配置代理Worker地址。",
                    nameof(proxyUrl));
            }

            if (!Uri.TryCreate(_proxyUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new ArgumentException(
                    $"代理URL格式无效: '{proxyUrl}'。必须是有效的HTTP/HTTPS地址，例如：https://your-proxy.your-subdomain.workers.dev",
                    nameof(proxyUrl));
            }
        }

        public async Task<DeployResult> DeploySingleAsync(DeployJob job)
        {
            var validationError = ValidateJob(job);
            if (validationError != null)
            {
                return new DeployResult { Success = false, Error = validationError };
            }

            var requestBody = new
            {
                accountId = job.AccountId,
                apiToken = job.ApiToken,
                workerName = job.WorkerName,
                script = job.Script,
                secrets = job.Secrets ?? new Dictionary<string, string>(),
                routes = job.Routes ?? new List<Route>(),
                subdomain = job.Subdomain
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var requestUrl = $"{_proxyUrl}/deploy/single";
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(_proxyKey))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _proxyKey);
            }

            try
            {
                Debug.WriteLine($"[Deploy] POST {requestUrl}");
                
                using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                var responseText = await response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false);

                Debug.WriteLine($"[Deploy] Response: {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = $"代理返回错误 HTTP {(int)response.StatusCode}";
                    if (!string.IsNullOrWhiteSpace(responseText))
                    {
                        errorMsg += $": {responseText}";
                    }
                    
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        errorMsg += " (请检查代理Worker是否正确部署，URL是否可访问)";
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        errorMsg += " (代理访问密钥错误)";
                    }

                    return new DeployResult { Success = false, Error = errorMsg };
                }

                var result = JsonSerializer.Deserialize<DeployResult>(responseText);
                return result ?? new DeployResult { Success = false, Error = "代理返回空响应" };
            }
            catch (TaskCanceledException)
            {
                return new DeployResult { Success = false, Error = "请求超时，请检查网络连接和代理Worker状态" };
            }
            catch (HttpRequestException ex)
            {
                return new DeployResult
                {
                    Success = false,
                    Error = $"网络请求失败: {ex.Message}。请检查网络连接和代理Worker URL"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Deploy] Exception: {ex}");
                return new DeployResult { Success = false, Error = $"部署异常: {ex.Message}" };
            }
        }

        public async Task<List<DeployResult>> DeployBatchAsync(
            List<DeployJob> jobs,
            int concurrency,
            IProgress<(int index, string status, string? error)> progress)
        {
            var results = new List<DeployResult>(new DeployResult[jobs.Count]);
            
            using var semaphore = new SemaphoreSlim(concurrency, concurrency);
            var tasks = new List<Task>();

            for (int i = 0; i < jobs.Count; i++)
            {
                var index = i;
                var job = jobs[index];

                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        progress?.Report((index, "running", null));
                        
                        var result = await DeploySingleAsync(job).ConfigureAwait(false);
                        results[index] = result;

                        progress?.Report((index, result.Success ? "success" : "error", result.Error));
                    }
                    catch (Exception ex)
                    {
                        results[index] = new DeployResult { Success = false, Error = ex.Message };
                        progress?.Report((index, "error", ex.Message));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return results;
        }

        private string? ValidateJob(DeployJob job)
        {
            if (job == null) return "部署任务不能为空";
            if (string.IsNullOrWhiteSpace(job.AccountId)) return "Account ID不能为空";
            if (string.IsNullOrWhiteSpace(job.ApiToken)) return "API Token不能为空";
            if (string.IsNullOrWhiteSpace(job.WorkerName)) return "Worker名称不能为空";
            if (string.IsNullOrWhiteSpace(job.Script)) return "脚本内容不能为空";
            return null;
        }
    }

    public class DeployResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public object? Data { get; set; }
    }
}
