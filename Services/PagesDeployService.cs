using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CFDeployer.Models;

namespace CFDeployer.Services
{
    /// <summary>
    /// Cloudflare Pages Direct Upload 部署服务
    /// 直连 api.cloudflare.com，无需代理 Worker
    /// 内置限流保护：串行/低并发、429 指数退避重试
    /// </summary>
    public class PagesDeployService
    {
        private static readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        private const string ApiBase = "https://api.cloudflare.com/client/v4";

        /// <summary>
        /// 批量部署 Pages 项目（带限流保护）
        /// </summary>
        public static async Task<BatchDeployResult> DeployBatchAsync(
            List<PagesDeployJob> jobs,
            int concurrency = 1,
            int delayMs = 1000,
            int maxRetries = 3,
            IProgress<(int index, string status, string? message)>? progress = null)
        {
            var result = new BatchDeployResult { Total = jobs.Count };
            var semaphore = new SemaphoreSlim(Math.Max(1, Math.Min(concurrency, 2)), Math.Max(1, Math.Min(concurrency, 2)));
            var tasks = new List<Task>();

            for (int i = 0; i < jobs.Count; i++)
            {
                int idx = i;

                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        progress?.Report((idx, "running", null));

                        var deployResult = await DeployWithRetryAsync(jobs[idx], maxRetries);
                        lock (result)
                        {
                            result.Details.Add(deployResult);
                            if (deployResult.Success)
                                result.Success++;
                            else if (deployResult.Error.Contains("429") || deployResult.Error.Contains("Too Many Requests"))
                                result.RateLimited++;
                            else
                                result.Failed++;
                        }

                        var status = deployResult.Success ? "success" : "error";
                        progress?.Report((idx, status, deployResult.Error));

                        if (delayMs > 0 && idx < jobs.Count - 1)
                        {
                            await Task.Delay(delayMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (result)
                        {
                            result.Failed++;
                            result.Details.Add(new DeployResult
                            {
                                Success = false,
                                Error = $"未捕获异常: {ex.Message}"
                            });
                        }
                        progress?.Report((idx, "error", ex.Message));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            return result;
        }

        /// <summary>
        /// 单项目部署（带重试）
        /// </summary>
        public static async Task<DeployResult> DeployWithRetryAsync(PagesDeployJob job, int maxRetries)
        {
            int attempt = 0;
            int delaySeconds = 2;

            while (true)
            {
                attempt++;
                var result = await DeploySingleAsync(job);

                if (result.Success)
                    return result;

                if (result.Error.Contains("429") || result.Error.Contains("Too Many Requests"))
                {
                    if (attempt > maxRetries)
                    {
                        return new DeployResult
                        {
                            Success = false,
                            Error = $"[429限流] 重试{maxRetries}次后仍失败。建议降低并发数或增加部署间隔。原始错误: {result.Error}"
                        };
                    }

                    await Task.Delay(delaySeconds * 1000);
                    delaySeconds *= 2;
                    continue;
                }

                return result;
            }
        }

        /// <summary>
        /// 单项目直连部署
        /// </summary>
        public static async Task<DeployResult> DeploySingleAsync(PagesDeployJob job)
        {
            try
            {
                // 1. 确保项目存在
                var projectResult = await EnsureProjectExistsAsync(job.AccountId, job.ApiToken, job.ProjectName);
                if (!projectResult.Success)
                    return projectResult;

                // 2. 创建 deployment，获取上传 URL
                var (deploymentId, uploadUrl, createError) = await CreateDeploymentAsync(job.AccountId, job.ApiToken, job.ProjectName, job.Branch);
                if (string.IsNullOrEmpty(uploadUrl))
                    return new DeployResult { Success = false, Error = createError ?? "无法获取上传URL" };

                // 3. 收集文件
                var files = CollectFiles(job);
                if (files.Count == 0)
                    return new DeployResult { Success = false, Error = "没有可上传的文件" };

                // 4. 上传文件
                using var content = new MultipartFormDataContent();
                foreach (var file in files)
                {
                    var fileContent = new StreamContent(file.Stream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    content.Add(fileContent, file.Name, file.Name);
                }

                var uploadResponse = await _client.PutAsync(uploadUrl, content);
                var uploadText = await uploadResponse.Content.ReadAsStringAsync();

                if (uploadResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return new DeployResult
                    {
                        Success = false,
                        Error = $"429 Too Many Requests: {uploadText}"
                    };
                }

                if (!uploadResponse.IsSuccessStatusCode)
                {
                    return new DeployResult
                    {
                        Success = false,
                        Error = $"上传文件失败 HTTP {(int)uploadResponse.StatusCode}: {uploadText}"
                    };
                }

                return new DeployResult
                {
                    Success = true,
                    Data = $"{{ \"deploymentId\": \"{deploymentId}\" }}"
                };
            }
            catch (TaskCanceledException)
            {
                return new DeployResult { Success = false, Error = "请求超时（120秒）" };
            }
            catch (HttpRequestException ex)
            {
                return new DeployResult { Success = false, Error = $"网络错误: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new DeployResult { Success = false, Error = $"Pages部署异常: {ex.Message}" };
            }
        }

        private static async Task<DeployResult> EnsureProjectExistsAsync(string accountId, string apiToken, string projectName)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{ApiBase}/accounts/{accountId}/pages/projects/{projectName}");
                request.Headers.Add("Authorization", $"Bearer {apiToken}");

                var response = await _client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var createReq = new HttpRequestMessage(HttpMethod.Post,
                        $"{ApiBase}/accounts/{accountId}/pages/projects");
                    createReq.Headers.Add("Authorization", $"Bearer {apiToken}");
                    createReq.Content = new StringContent(
                        JsonSerializer.Serialize(new { name = projectName }),
                        Encoding.UTF8, "application/json");

                    var createResp = await _client.SendAsync(createReq);
                    var createText = await createResp.Content.ReadAsStringAsync();
                    if (!createResp.IsSuccessStatusCode)
                    {
                        return new DeployResult
                        {
                            Success = false,
                            Error = $"创建项目失败 HTTP {(int)createResp.StatusCode}: {createText}"
                        };
                    }
                }
                else if (!response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync();
                    return new DeployResult { Success = false, Error = $"查询项目失败: {text}" };
                }

                return new DeployResult { Success = true };
            }
            catch (Exception ex)
            {
                return new DeployResult { Success = false, Error = $"确保项目存在时出错: {ex.Message}" };
            }
        }

        private static async Task<(string? deploymentId, string? uploadUrl, string? error)> CreateDeploymentAsync(
            string accountId, string apiToken, string projectName, string branch)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{ApiBase}/accounts/{accountId}/pages/projects/{projectName}/deployments");
                request.Headers.Add("Authorization", $"Bearer {apiToken}");
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { branch }),
                    Encoding.UTF8, "application/json");

                var response = await _client.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return (null, null, $"429 Too Many Requests: {text}");

                if (!response.IsSuccessStatusCode)
                    return (null, null, $"创建Deployment失败 HTTP {(int)response.StatusCode}: {text}");

                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    string? id = null;
                    string? url = null;

                    if (result.TryGetProperty("id", out var idProp))
                        id = idProp.GetString();

                    if (result.TryGetProperty("upload_url", out var urlProp))
                        url = urlProp.GetString();

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(url))
                        return (id, url, null);

                    return (null, null, "API响应中缺少deployment id或upload_url");
                }

                return (null, null, $"无法解析API响应: {text}");
            }
            catch (Exception ex)
            {
                return (null, null, $"创建Deployment异常: {ex.Message}");
            }
        }

        private static List<UploadFile> CollectFiles(PagesDeployJob job)
        {
            var files = new List<UploadFile>();
            var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 默认排除模式（类似 .gitignore）
            var excludePatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", ".gitignore", ".gitattributes",
                ".DS_Store", "Thumbs.db", "desktop.ini",
                ".idea", ".vscode", ".vs",
                "node_modules", "bower_components",
                "__pycache__", ".pytest_cache", ".mypy_cache",
                ".env", ".env.local", ".env.development", ".env.production",
                "*.log", "*.tmp", "*.temp", "*.bak", "*.swp", "*.swo", "*~",
                ".editorconfig", ".eslintcache", ".prettierignore",
            };

            // 检查文件名或路径是否匹配排除模式
            bool ShouldExclude(string path)
            {
                var name = Path.GetFileName(path);
                if (excludePatterns.Contains(name)) return true;
                
                // 检查路径中是否包含排除目录
                var parts = path.Split('/', '\\');
                foreach (var part in parts)
                {
                    if (excludePatterns.Contains(part)) return true;
                    if (part.StartsWith(".")) return true; // 隐藏文件/目录
                }
                
                // 检查扩展名
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext == ".log" || ext == ".tmp" || ext == ".bak" || ext == ".swp") return true;
                
                return false;
            }

            // 添加静态目录中的文件
            if (!string.IsNullOrEmpty(job.StaticDir) && Directory.Exists(job.StaticDir))
            {
                var dirInfo = new DirectoryInfo(job.StaticDir);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    var relativePath = GetRelativePath(dirInfo.FullName, file.FullName).Replace("\\", "/");
                    
                    // ⭐ 过滤排除的文件
                    if (ShouldExclude(relativePath)) continue;
                    
                    if (addedNames.Add(relativePath))
                    {
                        files.Add(new UploadFile
                        {
                            Name = relativePath,
                            Stream = file.OpenRead()
                        });
                    }
                }
            }

            // 如果没有静态文件，至少放一个 index.html
            if (files.Count == 0)
            {
                files.Add(new UploadFile
                {
                    Name = "index.html",
                    Stream = new MemoryStream(Encoding.UTF8.GetBytes("<html><body><h1>Cloudflare Pages</h1></body></html>"))
                });
            }

            // Pages Function 模式添加 _worker.js
            if (job.DeployType == PagesDeployType.PagesFunction && !string.IsNullOrEmpty(job.Script))
            {
                // 如果静态目录中已存在 _worker.js，先移除
                var existing = files.FirstOrDefault(f =>
                    string.Equals(f.Name, "_worker.js", StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Stream.Dispose();
                    files.Remove(existing);
                }

                files.Add(new UploadFile
                {
                    Name = "_worker.js",
                    Stream = new MemoryStream(Encoding.UTF8.GetBytes(job.Script))
                });
            }

            return files;
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
            var uri = new Uri(relativeTo.EndsWith("\\") ? relativeTo : relativeTo + "\\");
            var relativeUri = uri.MakeRelativeUri(new Uri(path));
            return Uri.UnescapeDataString(relativeUri.ToString());
        }

        private class UploadFile
        {
            public string Name { get; set; } = "";
            public Stream Stream { get; set; } = Stream.Null;
        }

        /// <summary>
        /// 批量部署结果
        /// </summary>
        public class BatchDeployResult
        {
            public int Total { get; set; }
            public int Success { get; set; }
            public int Failed { get; set; }
            public int RateLimited { get; set; }
            public List<DeployResult> Details { get; set; } = new();
        }
    }

    /// <summary>
    /// Pages 部署任务参数
    /// </summary>
    public class PagesDeployJob
    {
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Branch { get; set; } = "main";
        public string? StaticDir { get; set; }
        public string? Script { get; set; }
        public PagesDeployType DeployType { get; set; } = PagesDeployType.DirectUpload;
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    }
}
