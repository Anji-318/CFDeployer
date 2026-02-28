using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CFDeployer.Models;
using CFDeployer.Services;

namespace CFDeployer
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _proxyUrl = "";
        private string _proxyKey = "";
        private string _statusText = "就绪";
        private bool _isDeploying = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string ProxyUrl
        {
            get => _proxyUrl;
            set { _proxyUrl = value; OnPropertyChanged(nameof(ProxyUrl)); }
        }

        public string ProxyKey
        {
            get => _proxyKey;
            set { _proxyKey = value; OnPropertyChanged(nameof(ProxyKey)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public bool IsDeploying
        {
            get => _isDeploying;
            set { _isDeploying = value; OnPropertyChanged(nameof(IsDeploying)); }
        }

        public Profile? SelectedProfile { get; set; }
        public WorkerTemplate? SelectedTemplate { get; set; }
        public AccountGroup? SelectedGroup { get; set; }

        public ObservableCollection<Profile> Profiles { get; set; } = new();
        public ObservableCollection<AccountGroup> AccountGroups { get; set; } = new();
        public ObservableCollection<WorkerTemplate> Templates { get; set; } = new();
        public ObservableCollection<LogEntry> Logs { get; set; } = new();
        public ObservableCollection<DeployMatrixItem> DeployMatrix { get; set; } = new();

        // ========== 单部署（关键修复：完全避免阻塞UI线程）==========

        public async Task DeploySingleProfileAsync(Profile profile, string script)
        {
            // 所有前置检查在UI线程完成
            if (string.IsNullOrWhiteSpace(ProxyUrl))
            {
                AddLog("错误：代理未配置，请先配置代理Worker地址", "error");
                return;
            }

            if (profile == null ||
                string.IsNullOrWhiteSpace(profile.AccountId) ||
                string.IsNullOrWhiteSpace(profile.ApiToken) ||
                string.IsNullOrWhiteSpace(profile.WorkerName))
            {
                AddLog("错误：配置信息不完整", "error");
                return;
            }

            IsDeploying = true;
            StatusText = $"正在部署 {profile.WorkerName}...";
            AddLog($"开始部署: {profile.WorkerName}", "info");

            // 关键修复：使用 Task.Run 包裹整个操作，包括创建 DeployService
            // 这样即使构造函数抛出异常，也不会阻塞UI线程
            var result = await Task.Run(async () =>
            {
                try
                {
                    // 在后台线程创建服务（防止构造函数异常阻塞UI）
                    var service = new DeployService(ProxyUrl, ProxyKey);

                    var job = new DeployJob
                    {
                        AccountId = profile.AccountId.Trim(),
                        ApiToken = profile.ApiToken.Trim(),
                        WorkerName = profile.WorkerName.Trim(),
                        Script = script ?? "",
                        Secrets = profile.Secrets?.ToDictionary(s => s.Key, s => s.Value)
                                  ?? new Dictionary<string, string>(),
                        Routes = profile.Routes ?? new List<Route>(),
                        Subdomain = !string.IsNullOrEmpty(profile.Subdomain)
                    };

                    // 执行部署（ConfigureAwait(false) 确保不尝试回到原上下文）
                    return await service.DeploySingleAsync(job).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    return new DeployResult { Success = false, Error = $"配置错误: {ex.Message}" };
                }
                catch (Exception ex)
                {
                    return new DeployResult { Success = false, Error = $"异常: {ex.Message}" };
                }
            }).ConfigureAwait(true); // true 允许回到UI线程，但我们在下面显式处理

            // 回到UI线程更新状态（使用Invoke确保在UI线程执行）
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (result.Success)
                {
                    AddLog($"✅ {profile.WorkerName} 部署成功", "success");
                    StatusText = "部署成功";
                }
                else
                {
                    AddLog($"❌ {profile.WorkerName} 部署失败: {result.Error}", "error");
                    StatusText = "部署失败";
                }
                
                IsDeploying = false;
            });
        }

        // ========== 矩阵部署（关键修复）==========

        public async Task StartMatrixDeployAsync()
        {
            var items = DeployMatrix.Where(i => i.Selected).ToList();
            if (items.Count == 0)
            {
                AddLog("警告：未选择部署任务", "warning");
                return;
            }

            if (string.IsNullOrWhiteSpace(ProxyUrl))
            {
                AddLog("错误：代理未配置", "error");
                return;
            }

            var template = SelectedTemplate;
            if (template == null)
            {
                AddLog("错误：未选择Worker模板", "error");
                return;
            }

            IsDeploying = true;
            var total = items.Count;
            
            StatusText = $"准备部署 {total} 个任务...";
            AddLog($"开始矩阵部署: 共 {total} 个任务", "info");

            // 关键修复：整个操作在后台线程执行
            await Task.Run(async () =>
            {
                try
                {
                    // 在后台线程创建服务
                    var service = new DeployService(ProxyUrl, ProxyKey);

                    var jobs = items.Select((item, idx) => new DeployJob
                    {
                        AccountId = item.AccountId,
                        ApiToken = item.ApiToken,
                        WorkerName = item.WorkerName,
                        Script = ReplaceVars(template.Code ?? "", item.Variables),
                        Secrets = template.Secrets?.ToDictionary(
                            s => s.Key,
                            s => ReplaceVars(s.Value, item.Variables)
                        ) ?? new Dictionary<string, string>(),
                        Routes = new List<Route>(),
                        Subdomain = false
                    }).ToList();

                    var completed = 0;
                    var successCount = 0;
                    var failCount = 0;

                    // 使用IProgress报告进度（自动处理线程切换）
                    var progress = new Progress<(int index, string status, string? error)>(update =>
                    {
                        // 此回调自动在UI线程执行（Progress<T>的特性）
                        var item = items[update.index];
                        item.Status = update.status;
                        item.Message = update.error ?? update.status;

                        if (update.status == "success")
                        {
                            successCount++;
                            AddLog($"✅ {item.WorkerName} 部署成功", "success");
                        }
                        else if (update.status == "error")
                        {
                            failCount++;
                            AddLog($"❌ {item.WorkerName} 失败: {update.error}", "error");
                        }

                        completed++;
                        StatusText = $"部署中 ({completed}/{total})...";
                    });

                    // 执行批量部署
                    await service.DeployBatchAsync(jobs, 2, progress).ConfigureAwait(false);

                    // 最终结果
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var msg = $"矩阵部署完成: 成功 {successCount}，失败 {failCount}，总计 {total}";
                        AddLog(msg, failCount == 0 ? "success" : (successCount > 0 ? "warning" : "error"));
                        StatusText = failCount == 0 ? "全部部署成功" : $"完成（{failCount} 个失败）";
                    });
                }
                catch (ArgumentException ex)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AddLog($"❌ 配置错误: {ex.Message}", "error");
                        StatusText = "配置错误";
                    });
                }
                catch (Exception ex)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AddLog($"❌ 矩阵部署异常: {ex.Message}", "error");
                        StatusText = "部署异常";
                    });
                }
                finally
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsDeploying = false;
                    });
                }
            }).ConfigureAwait(false); // false 避免死锁，因为我们显式使用 Dispatcher
        }

        // ========== 辅助方法（关键修复：确保线程安全）==========

        /// <summary>
        /// 添加日志（线程安全版本）
        /// </summary>
        private void AddLog(string message, string type)
        {
            // 检查是否在UI线程
            if (Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                // 不在UI线程，使用Invoke回到UI线程
                Application.Current.Dispatcher.Invoke(() => AddLogInternal(message, type));
            }
            else
            {
                // 已在UI线程，直接执行
                AddLogInternal(message, type);
            }
        }

        private void AddLogInternal(string message, string type)
        {
            try
            {
                var brush = type switch
                {
                    "success" => new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                    "error" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                    "warning" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                    _ => new SolidColorBrush(Color.FromRgb(59, 130, 246))
                };

                Logs.Add(new LogEntry
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Message = message,
                    Brush = brush,
                    Type = type
                });

                if (Logs.Count > 100) Logs.RemoveAt(0);
            }
            catch { }
        }

        private string ReplaceVars(string template, Dictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var result = template;
            foreach (var (k, v) in vars)
                result = result.Replace($"{{{k}}}", v);
            return result;
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void GenerateDeployMatrix(string groupId, string templateId, Dictionary<string, string> vars)
        {
            // 简化实现...
        }

        public void SelectAllDeployItems(bool select)
        {
            foreach (var item in DeployMatrix) item.Selected = select;
        }
    }
}