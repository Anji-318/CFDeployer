using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using CFDeployer.Models;
using CFDeployer.Services;
using System.Net;  
using System.Security.Authentication;

namespace CFDeployer
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _proxyUrl = "";
        private string _proxyKey = "";
        private string _statusText = "就绪";
        private bool _isDeploying = false;
        private bool _isDarkMode = true;
        private Profile? _currentProfile;
        private AccountGroup? _currentGroup;
        private WorkerTemplate? _currentTemplate;
        private ObservableCollection<LogEntry> _logs = new();
        private bool _isUpdatingToken = false;
        
        // 添加日志筛选字段
        private string _currentLogFilter = "all";
        private ObservableCollection<LogEntry> _allLogs = new();
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public string ProxyUrl 
        { 
            get => _proxyUrl; 
            set 
            { 
                _proxyUrl = value; 
                OnPropertyChanged(nameof(ProxyUrl)); 
                UpdateProxyStatus();
                UpdateStatus();
            }
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
            set 
            { 
                _isDeploying = value; 
                OnPropertyChanged(nameof(IsDeploying));
                UpdateStatus();
            }
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                _isDarkMode = value;
                OnPropertyChanged(nameof(IsDarkMode));
                OnPropertyChanged(nameof(ThemeButtonContent));
                OnPropertyChanged(nameof(WindowBackground));
            }
        }

        public string ThemeButtonContent => IsDarkMode ? "🌙" : "☀️";
        
        public Brush WindowBackground => IsDarkMode 
            ? new SolidColorBrush(Color.FromRgb(15, 23, 42))
            : new SolidColorBrush(Color.FromRgb(241, 245, 249));
        
        public ObservableCollection<Profile> Profiles { get; set; } = new();
        public ObservableCollection<AccountGroup> AccountGroups { get; set; } = new();
        public ObservableCollection<WorkerTemplate> Templates { get; set; } = new();
        public ObservableCollection<LogEntry> Logs 
        { 
            get => _logs; 
            set { _logs = value; OnPropertyChanged(nameof(Logs)); }
        }
        
        public ObservableCollection<DeployMatrixItem> DeployMatrix { get; set; } = new();
        
        public Brush StatusColor
        {
            get
            {
                if (IsDeploying) return new SolidColorBrush(Color.FromRgb(245, 158, 11));
                if (string.IsNullOrEmpty(ProxyUrl)) return new SolidColorBrush(Color.FromRgb(239, 68, 68));
                return new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
        }

        public Brush ProxyStatusBrush => string.IsNullOrEmpty(ProxyUrl)
            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
            : new SolidColorBrush(Color.FromRgb(16, 185, 129));
        
        public string ProxyStatusText => string.IsNullOrEmpty(ProxyUrl) ? "未配置代理" : "代理就绪";
        
        public Brush ProxyWarningBrush => string.IsNullOrEmpty(ProxyUrl) 
            ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
            : new SolidColorBrush(Color.FromRgb(16, 185, 129));
            
        public Brush ProxyWarningBorder => string.IsNullOrEmpty(ProxyUrl)
            ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
            : new SolidColorBrush(Color.FromRgb(16, 185, 129));
            
        public string ProxyWarningIcon => string.IsNullOrEmpty(ProxyUrl) ? "⚠️" : "✅";
        public string ProxyWarningTitle => string.IsNullOrEmpty(ProxyUrl) ? "需要配置部署代理" : "代理已配置";
        public string ProxyWarningMessage => string.IsNullOrEmpty(ProxyUrl)
            ? "请登录 Cloudflare 部署代理 Worker。"
            : $"当前代理: {ProxyUrl}";
        public string ProxyButtonText => string.IsNullOrEmpty(ProxyUrl) ? "配置" : "修改";
        
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTheme();
            LoadData();
            RefreshUI();
            ApplyTheme();
            LogsItemsControl.ItemsSource = Logs;
            
            // 修复：订阅Worker模板编辑器的事件
            SubscribeTemplateEditorEvents();
            
            AddLog("应用已启动", "success");
        }
        
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveData();
            SaveTheme();
        }

        #region 主题管理
        private void LoadTheme()
        {
            try
            {
                var themeFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CFDeployer", "theme.txt");
                
                if (File.Exists(themeFile))
                {
                    var theme = File.ReadAllText(themeFile).Trim();
                    IsDarkMode = theme != "light";
                    AddLog($"主题已加载: {(IsDarkMode ? "深色" : "浅色")}模式", "info");
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载主题失败: {ex.Message}", "error");
            }
        }

        private void SaveTheme()
        {
            try
            {
                var themeFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CFDeployer", "theme.txt");
                
                Directory.CreateDirectory(Path.GetDirectoryName(themeFile)!);
                File.WriteAllText(themeFile, IsDarkMode ? "dark" : "light");
                AddLog("主题设置已保存", "info");
            }
            catch (Exception ex)
            {
                AddLog($"保存主题失败: {ex.Message}", "error");
            }
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            IsDarkMode = !IsDarkMode;
            ApplyTheme();
            AddLog($"已切换到{(IsDarkMode ? "深色" : "浅色")}主题", "info");
        }

        private void ApplyTheme()
        {
            if (IsDarkMode)
            {
                // 深色主题 - 深灰色输入框
                Resources["BgDark"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                Resources["BgCard"] = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                Resources["BgInput"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                Resources["InputBackground"] = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                Resources["InputForeground"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                Resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                Resources["Border"] = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            }
            else
            {
                // 浅色主题 - 浅灰色输入框
                Resources["BgDark"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                Resources["BgCard"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                Resources["BgInput"] = new SolidColorBrush(Color.FromRgb(241, 245, 249)); // 浅灰色背景
                Resources["InputBackground"] = new SolidColorBrush(Color.FromRgb(241, 245, 249)); // 浅灰色输入框
                Resources["InputForeground"] = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // 深色文本
                Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                Resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(71, 85, 105));
                Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                Resources["Border"] = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            }

            if (ThemeToggleBtn != null)
            {
                ThemeToggleBtn.Content = ThemeButtonContent;
            }
        }
        #endregion
        
        private void LoadData()
        {
            try
            {
                var data = StorageService.LoadAppData();
                if (data != null)
                {
                    ProxyUrl = data.ProxyUrl ?? "";
                    ProxyKey = data.ProxyKey ?? "";
                    
                    Profiles.Clear();
                    foreach (var p in data.Profiles ?? new List<Profile>())
                        Profiles.Add(p);
                        
                    AccountGroups.Clear();
                    foreach (var g in data.AccountGroups ?? new List<AccountGroup>())
                        AccountGroups.Add(g);
                        
                    Templates.Clear();
                    foreach (var t in data.Templates ?? new List<WorkerTemplate>())
                        Templates.Add(t);
                    
                    if (AccountGroups.Count == 0)
                    {
                        AccountGroups.Add(new AccountGroup 
                        { 
                            Id = Guid.NewGuid().ToString(),
                            Name = "示例账户组",
                            Accounts = new List<Account> 
                            { 
                                new Account { Name = "示例账户", AccountId = "", ApiToken = "" } 
                            }
                        });
                    }
                    
                    if (Templates.Count == 0)
                    {
                        Templates.Add(new WorkerTemplate
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = "示例模板",
                            WorkerNamePattern = "demo-worker-{{region}}",
                            Variables = new List<string> { "region" },
                            Code = GetDefaultWorkerCode(),
                            Secrets = new List<SecretTemplate>()
                        });
                    }
                    
                    AddLog($"配置数据已加载: {Profiles.Count}个配置, {AccountGroups.Count}个账户组, {Templates.Count}个模板", "success");
                }
                else
                {
                    AddLog("未找到现有配置，使用默认设置", "warning");
                }
                
                UpdateProxyStatus();
            }
            catch (Exception ex)
            {
                AddLog($"加载配置数据失败: {ex.Message}", "error");
            }
        }
        
        private void SaveData()
        {
            try
            {
                // 修复：保存前确保当前编辑的模板数据已同步
                SyncCurrentTemplateFromUI();
                
                var data = new AppData
                {
                    ProxyUrl = ProxyUrl,
                    ProxyKey = ProxyKey,
                    Profiles = Profiles.ToList(),
                    AccountGroups = AccountGroups.ToList(),
                    Templates = Templates.ToList()
                };
                StorageService.SaveAppData(data);
                AddLog("配置数据已保存", "success");
            }
            catch (Exception ex)
            {
                AddLog($"保存配置数据失败: {ex.Message}", "error");
            }
        }
        
        private string GetDefaultWorkerCode()
        {
            return @"export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    
    const corsHeaders = {
      ""Access-Control-Allow-Origin"": ""*"",
      ""Access-Control-Allow-Methods"": ""GET, POST, PUT, DELETE, OPTIONS"",
      ""Access-Control-Allow-Headers"": ""Content-Type, Authorization"",
    };
    
    if (request.method === ""OPTIONS"") {
      return new Response(null, { headers: corsHeaders });
    }
    
    return new Response(""Hello from {{region}} Worker!"", {
      headers: { ...corsHeaders, ""Content-Type"": ""text/plain"" },
    });
  },
};";
        }
        
        private void RefreshUI()
        {
            ProfileList.ItemsSource = null;
            ProfileList.ItemsSource = Profiles;
            
            AccountGroupsList.ItemsSource = null;
            AccountGroupsList.ItemsSource = AccountGroups;
            
            TemplatesList.ItemsSource = null;
            TemplatesList.ItemsSource = Templates;
            
            DeployGroupCombo.ItemsSource = null;
            DeployGroupCombo.ItemsSource = AccountGroups;
            
            DeployTemplateCombo.ItemsSource = null;
            DeployTemplateCombo.ItemsSource = Templates;
        }
        
        // 修复：刷新配置列表（用于名称修改后联动）
        private void RefreshProfileList()
        {
            // 强制刷新配置列表
            var temp = ProfileList.ItemsSource;
            ProfileList.ItemsSource = null;
            ProfileList.ItemsSource = temp;
        }
        
        // 修复：刷新账户组列表（用于名称修改后联动）
        private void RefreshAccountGroupList()
        {
            // 强制刷新账户组列表
            var temp = AccountGroupsList.ItemsSource;
            AccountGroupsList.ItemsSource = null;
            AccountGroupsList.ItemsSource = temp;
        }
        
        // 修复：刷新模板列表（用于名称修改后联动）
        private void RefreshTemplateList()
        {
            // 强制刷新模板列表
            var temp = TemplatesList.ItemsSource;
            TemplatesList.ItemsSource = null;
            TemplatesList.ItemsSource = temp;
        }
        
        // 增强的 AddLog 方法 - 修复深色模式文本颜色
        private void AddLog(string message, string type = "info", string? details = null)
        {
            Brush brush;
            Brush bgBrush;
            string icon;
            
            // 根据类型设置颜色
            switch (type)
            {
                case "success":
                    brush = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // 绿色
                    bgBrush = new SolidColorBrush(Color.FromArgb(30, 16, 185, 129));
                    icon = "✅";
                    break;
                case "error":
                    brush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // 红色
                    bgBrush = new SolidColorBrush(Color.FromArgb(30, 239, 68, 68));
                    icon = "❌";
                    break;
                case "warning":
                    brush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // 橙色
                    bgBrush = new SolidColorBrush(Color.FromArgb(30, 245, 158, 11));
                    icon = "⚠️";
                    break;
                case "debug":
                    // 修复：根据主题调整 debug 文本颜色
                    brush = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(148, 163, 184)) // 深色模式用亮色
                        : new SolidColorBrush(Color.FromRgb(100, 116, 139)); // 浅色模式用暗色
                    bgBrush = Brushes.Transparent;
                    icon = "🔍";
                    break;
                default: // info
                    // 修复：根据主题调整 info 文本颜色
                    brush = IsDarkMode 
                        ? new SolidColorBrush(Color.FromRgb(96, 165, 250)) // 深色模式用亮蓝色
                        : new SolidColorBrush(Color.FromRgb(59, 130, 246)); // 浅色模式用标准蓝
                    bgBrush = new SolidColorBrush(Color.FromArgb(30, 59, 130, 246));
                    icon = "ℹ️";
                    break;
            }
            
            var log = new LogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                Message = message,
                Details = details,
                Brush = brush,
                MessageColor = brush, // 确保设置 MessageColor
                LevelIcon = icon,
                BackgroundBrush = bgBrush,
                Type = type
            };
            
            Dispatcher.Invoke(() =>
            {
                _allLogs.Add(log);
                
                // 限制总日志数量
                if (_allLogs.Count > 1000)
                {
                    _allLogs.RemoveAt(0);
                }
                
                // 应用筛选
                ApplyLogFilter();
                
                // 更新计数
                if (LogCountText != null)
                {
                    LogCountText.Text = _allLogs.Count.ToString();
                }
                
                // 自动滚动
                if (EmptyLogText != null)
                {
                    EmptyLogText.Visibility = _allLogs.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                }
                
                LogsScrollViewer?.ScrollToEnd();
            });
        }

        // 应用日志筛选
        private void ApplyLogFilter()
        {
            Logs.Clear();
            var filtered = _currentLogFilter switch
            {
                "info" => _allLogs.Where(l => l.Type == "info"),
                "success" => _allLogs.Where(l => l.Type == "success"),
                "warning" => _allLogs.Where(l => l.Type == "warning"),
                "error" => _allLogs.Where(l => l.Type == "error"),
                _ => _allLogs.AsEnumerable()
            };
            
            foreach (var log in filtered)
            {
                Logs.Add(log);
            }
        }

        // 筛选按钮点击
        private void FilterLogs_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton clickedBtn)
            {
                // 获取所有筛选按钮的父容器
                var parent = clickedBtn.Parent as StackPanel;
                if (parent != null)
                {
                    foreach (var btn in parent.Children.OfType<ToggleButton>())
                    {
                        // 只有当前点击的按钮设为选中，其他都取消选中
                        btn.IsChecked = (btn == clickedBtn);
                    }
                }
                
                _currentLogFilter = clickedBtn.Tag?.ToString() ?? "all";
                ApplyLogFilter();
                AddLog($"日志筛选已切换至: {_currentLogFilter}", "debug");
            }
        }

        // 导出日志
        private void ExportLogs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"cf-deployer-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Cloudflare Deployer 日志导出");
                    sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"总日志数: {_allLogs.Count}");
                    sb.AppendLine(new string('=', 50));
                    sb.AppendLine();
                    
                    foreach (var log in _allLogs)
                    {
                        sb.AppendLine($"[{log.Time}] {log.LevelIcon} {log.Message}");
                        if (!string.IsNullOrEmpty(log.Details))
                        {
                            sb.AppendLine($"    详情: {log.Details}");
                        }
                    }
                    
                    File.WriteAllText(dialog.FileName, sb.ToString());
                    AddLog($"日志已导出到: {dialog.FileName}", "success");
                }
                catch (Exception ex)
                {
                    AddLog($"导出日志失败: {ex.Message}", "error");
                }
            }
        }

        // 清空日志
        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            _allLogs.Clear();
            Logs.Clear();
            if (LogCountText != null)
            {
                LogCountText.Text = "0";
            }
            if (EmptyLogText != null)
            {
                EmptyLogText.Visibility = Visibility.Visible;
            }
            AddLog("日志已清空", "info");
        }

        private void UpdateStatus()
        {
            if (IsDeploying)
            {
                StatusText = "部署中...";
            }
            else if (string.IsNullOrEmpty(ProxyUrl))
            {
                StatusText = "未配置代理";
            }
            else
            {
                StatusText = "就绪";
            }
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
        
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
                TabProfiles.IsChecked = false;
                TabAccounts.IsChecked = false;
                TabTemplates.IsChecked = false;
                TabDeploy.IsChecked = false;
                
                PageProfiles.Visibility = Visibility.Collapsed;
                PageAccounts.Visibility = Visibility.Collapsed;
                PageTemplates.Visibility = Visibility.Collapsed;
                PageDeploy.Visibility = Visibility.Collapsed;
                
                btn.IsChecked = true;
                var tag = btn.Tag?.ToString();
                
                switch (tag)
                {
                    case "Profiles":
                        PageProfiles.Visibility = Visibility.Visible;
                        AddLog("切换到配置管理页面", "debug");
                        break;
                    case "Accounts":
                        PageAccounts.Visibility = Visibility.Visible;
                        AddLog("切换到账户组页面", "debug");
                        break;
                    case "Templates":
                        PageTemplates.Visibility = Visibility.Visible;
                        AddLog("切换到Worker模板页面", "debug");
                        break;
                    case "Deploy":
                        PageDeploy.Visibility = Visibility.Visible;
                        AddLog("切换到部署矩阵页面", "debug");
                        break;
                }
            }
        }
        
        private void CreateProfile_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"新配置 {Profiles.Count + 1}",
            Code = GetDefaultWorkerCode()
        };
        Profiles.Add(profile);
        SelectProfile(profile);
        SaveData();
        AddLog($"创建新配置: {profile.Name}", "success");
    }
    catch (Exception ex)
    {
        AddLog($"创建配置失败: {ex.Message}", "error");
    }
}

// 关键修复：配置项选择事件 - 点击左侧列表项时同步到右侧编辑器
private void ProfileItem_Select(object sender, MouseButtonEventArgs e)
{
    // 如果点击的是按钮，不触发选择（避免点击部署/删除按钮时触发选择）
    if (e.OriginalSource is FrameworkElement element)
    {
        var parent = element;
        while (parent != null)
        {
            if (parent is Button)
                return; // 点击了按钮，不处理选择
            parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
        }
    }
    
    if (sender is Border border && border.DataContext is Profile profile)
    {
        SelectProfile(profile);
    }
}
        
private void SelectProfile(Profile profile)
{
    try
    {
        _currentProfile = profile;
        
        // 关键修复：先取消订阅，避免重复订阅事件
        UnsubscribeProfileEvents();
        
        ProfileName.Text = profile.Name;
        ProfileAccountId.Text = profile.AccountId;
        
        _isUpdatingToken = true;
        ProfileApiToken.Password = profile.ApiToken ?? "";
        ProfileApiTokenVisible.Text = profile.ApiToken ?? "";
        _isUpdatingToken = false;
        
        ProfileWorkerName.Text = profile.WorkerName;
        ProfileSubdomain.Text = profile.Subdomain ?? "";
        ProfileCode.Text = profile.Code ?? GetDefaultWorkerCode();
        
        SecretsList.ItemsSource = null;
        SecretsList.ItemsSource = profile.Secrets;
        
        // 关键修复：订阅所有输入框的变化事件，实现实时保存
        SubscribeProfileEvents();
        
        AddLog($"已选择配置: {profile.Name}", "debug");
    }
    catch (Exception ex)
    {
        AddLog($"选择配置失败: {ex.Message}", "error");
    }
}

// 关键修复：新增 - 订阅配置编辑事件
private void SubscribeProfileEvents()
{
    ProfileName.TextChanged += ProfileName_TextChanged;
    ProfileAccountId.TextChanged += ProfileAccountId_TextChanged;
    ProfileWorkerName.TextChanged += ProfileWorkerName_TextChanged;
    ProfileSubdomain.TextChanged += ProfileSubdomain_TextChanged;
    ProfileCode.TextChanged += ProfileCode_TextChanged;
    
    // API Token 用 PasswordChanged 和 TextChanged 事件
    ProfileApiToken.PasswordChanged += ProfileApiToken_Save;
    ProfileApiTokenVisible.TextChanged += ProfileApiTokenVisible_Save;
}

// 关键修复：新增 - 取消订阅配置编辑事件
private void UnsubscribeProfileEvents()
{
    ProfileName.TextChanged -= ProfileName_TextChanged;
    ProfileAccountId.TextChanged -= ProfileAccountId_TextChanged;
    ProfileWorkerName.TextChanged -= ProfileWorkerName_TextChanged;
    ProfileSubdomain.TextChanged -= ProfileSubdomain_TextChanged;
    ProfileCode.TextChanged -= ProfileCode_TextChanged;
    
    ProfileApiToken.PasswordChanged -= ProfileApiToken_Save;
    ProfileApiTokenVisible.TextChanged -= ProfileApiTokenVisible_Save;
}

// 关键修复：新增 - Account ID 变化保存
private void ProfileAccountId_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_currentProfile != null)
    {
        _currentProfile.AccountId = ProfileAccountId.Text;
        DebounceSave();
    }
}

// 关键修复：新增 - Worker 名称变化保存
private void ProfileWorkerName_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_currentProfile != null)
    {
        _currentProfile.WorkerName = ProfileWorkerName.Text;
        DebounceSave();
    }
}

// 关键修复：新增 - 自定义域名变化保存
private void ProfileSubdomain_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_currentProfile != null)
    {
        _currentProfile.Subdomain = ProfileSubdomain.Text;
        DebounceSave();
    }
}

// 关键修复：新增 - Worker 代码变化保存
private void ProfileCode_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_currentProfile != null)
    {
        _currentProfile.Code = ProfileCode.Text;
        DebounceSave();
    }
}

// 关键修复：新增 - API Token (PasswordBox) 变化保存
private void ProfileApiToken_Save(object sender, RoutedEventArgs e)
{
    if (_currentProfile != null && !_isUpdatingToken)
    {
        _currentProfile.ApiToken = ProfileApiToken.Password;
        DebounceSave();
    }
}

// 关键修复：新增 - API Token (TextBox) 变化保存
private void ProfileApiTokenVisible_Save(object sender, TextChangedEventArgs e)
{
    if (_currentProfile != null && !_isUpdatingToken)
    {
        _currentProfile.ApiToken = ProfileApiTokenVisible.Text;
        DebounceSave();
    }
}

// 修复：配置名称文本变化事件 - 实现实时联动
private void ProfileName_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_currentProfile != null)
    {
        _currentProfile.Name = ProfileName.Text;
        // 立即刷新列表实现联动
        RefreshProfileList();
        // 延迟保存到文件
        DebounceSave();
    }
}

private void DeleteProfile_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is Profile profile)
    {
        if (MessageBox.Show($"确定删除配置 \"{profile.Name}\" 吗？", "确认删除", 
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                Profiles.Remove(profile);
                if (_currentProfile == profile)
                {
                    _currentProfile = null;
                    // 关键修复：先取消订阅，避免内存泄漏
                    UnsubscribeProfileEvents();
                    
                    ProfileName.Text = "";
                    ProfileAccountId.Text = "";
                    ProfileApiToken.Password = "";
                    ProfileApiTokenVisible.Text = "";
                    ProfileWorkerName.Text = "";
                    ProfileSubdomain.Text = "";
                    ProfileCode.Text = "";
                }
                SaveData();
                RefreshUI();
                AddLog($"删除配置成功: {profile.Name}", "success");
            }
            catch (Exception ex)
            {
                AddLog($"删除配置失败: {ex.Message}", "error");
            }
        }
    }
}

private void DeployProfile_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is Profile profile)
    {
        _ = DeploySingleAsync(profile);
    }
}

private async void DeployCurrent_Click(object sender, RoutedEventArgs e)
{
    if (_currentProfile == null) return;
    
    // 关键修复：不再需要手动保存，因为所有字段都已实时绑定
    // 直接部署即可
    await DeploySingleAsync(_currentProfile);
}
        
        private void ToggleTokenVisibility_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ProfileApiToken.Visibility == Visibility.Visible)
                {
                    ProfileApiToken.Visibility = Visibility.Collapsed;
                    ProfileApiTokenVisible.Visibility = Visibility.Visible;
                    ProfileApiTokenVisible.Text = ProfileApiToken.Password;
                    ToggleTokenBtn.Content = "🙈";
                    AddLog("API Token 已显示", "debug");
                }
                else
                {
                    ProfileApiToken.Visibility = Visibility.Visible;
                    ProfileApiTokenVisible.Visibility = Visibility.Collapsed;
                    ProfileApiToken.Password = ProfileApiTokenVisible.Text;
                    ToggleTokenBtn.Content = "👁️";
                    AddLog("API Token 已隐藏", "debug");
                }
            }
            catch (Exception ex)
            {
                AddLog($"切换Token显示失败: {ex.Message}", "error");
            }
        }

        private void ProfileApiToken_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingToken) return;
            _isUpdatingToken = true;
            if (ProfileApiTokenVisible != null)
            {
                ProfileApiTokenVisible.Text = ProfileApiToken.Password;
            }
            _isUpdatingToken = false;
        }

        private void ProfileApiTokenVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingToken) return;
            _isUpdatingToken = true;
            if (ProfileApiToken != null)
            {
                ProfileApiToken.Password = ProfileApiTokenVisible.Text;
            }
            _isUpdatingToken = false;
        }
        
        private void AddSecret_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentProfile == null) return;
                _currentProfile.Secrets.Add(new Secret { Key = "", Value = "" });
                SecretsList.ItemsSource = null;
                SecretsList.ItemsSource = _currentProfile.Secrets;
                AddLog("添加环境变量", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"添加环境变量失败: {ex.Message}", "error");
            }
        }
        
        private void CreateGroup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var group = new AccountGroup
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"新账户组 {AccountGroups.Count + 1}",
                    Accounts = new List<Account>()
                };
                AccountGroups.Add(group);
                SelectGroup(group);
                SaveData();
                AddLog($"创建新账户组: {group.Name}", "success");
            }
            catch (Exception ex)
            {
                AddLog($"创建账户组失败: {ex.Message}", "error");
            }
        }
        
        private void SelectGroup(AccountGroup group)
        {
            try
            {
                _currentGroup = group;
                GroupName.Text = group.Name;
                
                // 修复：先取消订阅再订阅，避免重复
                GroupName.TextChanged -= GroupName_TextChanged;
                GroupName.TextChanged += GroupName_TextChanged;
                
                RefreshAccountsList();
                AddLog($"已选择账户组: {group.Name}", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"选择账户组失败: {ex.Message}", "error");
            }
        }
        
        // 修复：GroupName文本变化时自动保存并刷新列表实现联动
        private void GroupName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentGroup != null)
            {
                _currentGroup.Name = GroupName.Text;
                // 立即刷新列表实现联动
                RefreshAccountGroupList();
                // 延迟保存到文件
                DebounceSave();
            }
        }
        
        private void AccountGroup_Select(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is AccountGroup group)
            {
                SelectGroup(group);
            }
        }
        
        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AccountGroup group)
            {
                if (MessageBox.Show($"确定删除账户组 \"{group.Name}\" 吗？", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        AccountGroups.Remove(group);
                        if (_currentGroup == group)
                        {
                            _currentGroup = null;
                            GroupName.Text = "";
                            AccountsList.ItemsSource = null;
                        }
                        SaveData();
                        RefreshUI();
                        AddLog($"删除账户组成功: {group.Name}", "success");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"删除账户组失败: {ex.Message}", "error");
                    }
                }
            }
        }
        
        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentGroup == null) return;
                _currentGroup.Accounts.Add(new Account { Name = "", AccountId = "", ApiToken = "" });
                RefreshAccountsList();
                SaveData();
                AddLog("添加新账户", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"添加账户失败: {ex.Message}", "error");
            }
        }

        // 删除账户按钮点击事件
        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Account account)
            {
                try
                {
                    if (_currentGroup == null) return;
                    
                    _currentGroup.Accounts.Remove(account);
                    RefreshAccountsList();
                    SaveData();
                    AddLog($"删除账户: {account.Name}", "debug");
                }
                catch (Exception ex)
                {
                    AddLog($"删除账户失败: {ex.Message}", "error");
                }
            }
        }

        // 删除环境变量按钮点击事件
        private void DeleteSecret_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Secret secret)
            {
                try
                {
                    if (_currentProfile == null) return;
                    
                    _currentProfile.Secrets.Remove(secret);
                    SecretsList.ItemsSource = null;
                    SecretsList.ItemsSource = _currentProfile.Secrets;
                    SaveData();
                    AddLog("删除环境变量", "debug");
                }
                catch (Exception ex)
                {
                    AddLog($"删除环境变量失败: {ex.Message}", "error");
                }
            }
        }
        
        private void RefreshAccountsList()
        {
            if (_currentGroup == null) return;
            
            try
            {
                AccountsList.Items.Clear();
                for (int i = 0; i < _currentGroup.Accounts.Count; i++)
                {
                    var account = _currentGroup.Accounts[i];
                    var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                    
                    var header = new Grid();
                    header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 2, 8, 2),
                        Child = new TextBlock 
                        { 
                            Text = $"账户 {i + 1}", 
                            Foreground = Brushes.White,
                            FontSize = 11
                        }
                    };
                    Grid.SetColumn(badge, 0);
                    
                    var deleteBtn = new Button
                    {
                        Content = "删除",
                        Style = (Style)FindResource("DangerButton"),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Tag = i
                    };
                    deleteBtn.Click += (s, ev) => 
                    {
                        try
                        {
                            int idx = (int)((Button)s).Tag;
                            _currentGroup.Accounts.RemoveAt(idx);
                            RefreshAccountsList();
                            SaveData();
                            AddLog("删除账户", "debug");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"删除账户失败: {ex.Message}", "error");
                        }
                    };
                    Grid.SetColumn(deleteBtn, 1);
                    
                    header.Children.Add(badge);
                    header.Children.Add(deleteBtn);
                    panel.Children.Add(header);
                    
                    panel.Children.Add(new TextBlock 
                    { 
                        Text = "账户标识", 
                        Foreground = (Brush)FindResource("TextSecondary"),
                        FontSize = 12,
                        Margin = new Thickness(0, 8, 0, 4)
                    });
                    var nameBox = new TextBox 
                    { 
                        Text = account.Name,
                        Style = (Style)FindResource("ModernTextBox"),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    nameBox.TextChanged += (s, ev) => { account.Name = ((TextBox)s).Text; SaveData(); };
                    panel.Children.Add(nameBox);
                    
                    panel.Children.Add(new TextBlock 
                    { 
                        Text = "Account ID", 
                        Foreground = (Brush)FindResource("TextSecondary"),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                    var idBox = new TextBox 
                    { 
                        Text = account.AccountId,
                        Style = (Style)FindResource("ModernTextBox"),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    idBox.TextChanged += (s, ev) => { account.AccountId = ((TextBox)s).Text; SaveData(); };
                    panel.Children.Add(idBox);
                    
                    panel.Children.Add(new TextBlock 
                    { 
                        Text = "API Token", 
                        Foreground = (Brush)FindResource("TextSecondary"),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                    var tokenBox = new PasswordBox 
                    { 
                        Password = account.ApiToken,
                        Style = (Style)FindResource("ModernPasswordBox")
                    };
                    tokenBox.PasswordChanged += (s, ev) => { account.ApiToken = ((PasswordBox)s).Password; SaveData(); };
                    panel.Children.Add(tokenBox);
                    
                    var container = new Border
                    {
                        Style = (Style)FindResource("DynamicAccountCardStyle"),
                        Child = panel
                    };
                    
                    AccountsList.Items.Add(container);
                }
            }
            catch (Exception ex)
            {
                AddLog($"刷新账户列表失败: {ex.Message}", "error");
            }
        }
        
        private void CreateTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var template = new WorkerTemplate
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"新模板 {Templates.Count + 1}",
                    WorkerNamePattern = "worker-{{region}}",
                    Variables = new List<string> { "region" },
                    Code = GetDefaultWorkerCode(),
                    Secrets = new List<SecretTemplate>()
                };
                Templates.Add(template);
                SelectTemplate(template);
                SaveData();
                AddLog($"创建新模板: {template.Name}", "success");
            }
            catch (Exception ex)
            {
                AddLog($"创建模板失败: {ex.Message}", "error");
            }
        }
        
        // 删除模板按钮点击事件
        private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            // 阻止事件冒泡，防止触发 Template_Select
            e.Handled = true;
            
            if (sender is Button btn && btn.Tag is WorkerTemplate template)
            {
                // 确认删除对话框
                var result = MessageBox.Show(
                    $"确定要删除模板 \"{template.Name}\" 吗？\n\n此操作不可恢复！", 
                    "确认删除",
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning);
                    
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // 从列表中移除
                        Templates.Remove(template);
                        
                        // 如果删除的是当前正在编辑的模板，清空编辑器
                        if (_currentTemplate == template)
                        {
                            _currentTemplate = null;
                            TemplateName.Text = "";
                            TemplatePattern.Text = "";
                            TemplateCode.Text = "";
                            VariablesList.Items.Clear();
                        }
                        
                        // 保存数据并刷新UI
                        SaveData();
                        RefreshUI();
                        
                        AddLog($"删除模板成功: {template.Name}", "success");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"删除模板失败: {ex.Message}", "error");
                    }
                }
            }
        }

        private void SelectTemplate(WorkerTemplate template)
        {
            try
            {
                // 修复：先取消订阅之前的事件，避免重复订阅
                UnsubscribeTemplateEditorEvents();
                
                _currentTemplate = template;
                TemplateName.Text = template.Name;
                TemplatePattern.Text = template.WorkerNamePattern;
                TemplateCode.Text = template.Code;
                
                // 修复：重新订阅事件
                SubscribeTemplateEditorEvents();
                
                RefreshVariablesList();
                AddLog($"已选择模板: {template.Name}", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"选择模板失败: {ex.Message}", "error");
            }
        }
        
        // 修复：订阅模板编辑器事件
        private void SubscribeTemplateEditorEvents()
        {
            if (TemplateName != null)
                TemplateName.TextChanged += TemplateName_TextChanged;
            if (TemplatePattern != null)
                TemplatePattern.TextChanged += TemplatePattern_TextChanged;
            if (TemplateCode != null)
                TemplateCode.TextChanged += TemplateCode_TextChanged;
        }
        
        // 修复：取消订阅模板编辑器事件
        private void UnsubscribeTemplateEditorEvents()
        {
            if (TemplateName != null)
                TemplateName.TextChanged -= TemplateName_TextChanged;
            if (TemplatePattern != null)
                TemplatePattern.TextChanged -= TemplatePattern_TextChanged;
            if (TemplateCode != null)
                TemplateCode.TextChanged -= TemplateCode_TextChanged;
        }
        
        // 修复：模板名称文本变化事件 - 实现实时联动
        private void TemplateName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentTemplate != null)
            {
                _currentTemplate.Name = TemplateName.Text;
                // 立即刷新列表实现联动
                RefreshTemplateList();
                // 延迟保存到文件
                DebounceSave();
            }
        }
        
        // 修复：Worker名称模式文本变化事件
        private void TemplatePattern_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentTemplate != null)
            {
                _currentTemplate.WorkerNamePattern = TemplatePattern.Text;
                DebounceSave();
            }
        }
        
        // 修复：代码编辑器文本变化事件 - 这是关键修复！
        private void TemplateCode_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentTemplate != null)
            {
                _currentTemplate.Code = TemplateCode.Text;
                DebounceSave();
            }
        }
        
        // 修复：防抖保存定时器
        private System.Windows.Threading.DispatcherTimer? _saveTimer;
        
        // 修复：防抖保存方法 - 避免频繁写入文件
        private void DebounceSave()
        {
            if (_saveTimer == null)
            {
                _saveTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _saveTimer.Tick += (s, e) =>
                {
                    _saveTimer.Stop();
                    SaveData();
                };
            }
            
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        
        // 修复：同步当前模板数据从UI（用于确保保存前数据最新）
        private void SyncCurrentTemplateFromUI()
        {
            if (_currentTemplate != null)
            {
                _currentTemplate.Name = TemplateName.Text;
                _currentTemplate.WorkerNamePattern = TemplatePattern.Text;
                _currentTemplate.Code = TemplateCode.Text;
            }
        }
        
        private void Template_Select(object sender, MouseButtonEventArgs e)
        {
            // 如果点击的是按钮，不触发选择
            if (e.OriginalSource is FrameworkElement element)
            {
                // 检查是否点击了按钮或其子元素
                var parent = element;
                while (parent != null)
                {
                    if (parent is Button)
                        return; // 点击了按钮，不处理选择
                    parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
                }
            }
            
            if (sender is Border border && border.DataContext is WorkerTemplate template)
            {
                SelectTemplate(template);
            }
        }
        
        private void AddVariable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentTemplate == null) return;
                _currentTemplate.Variables.Add("");
                RefreshVariablesList();
                SaveData();
                AddLog("添加变量", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"添加变量失败: {ex.Message}", "error");
            }
        }
        
        private void RefreshVariablesList()
        {
            if (_currentTemplate == null) return;
            
            try
            {
                VariablesList.Items.Clear();
                for (int i = 0; i < _currentTemplate.Variables.Count; i++)
                {
                    int idx = i;
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    
                    var box = new TextBox 
                    { 
                        Text = _currentTemplate.Variables[i],
                        Style = (Style)FindResource("ModernTextBox"),
                        Margin = new Thickness(0, 0, 8, 8)
                    };
                    box.TextChanged += (s, ev) => 
                    { 
                        _currentTemplate.Variables[idx] = ((TextBox)s).Text;
                        DebounceSave();
                    };
                    Grid.SetColumn(box, 0);
                    
                    var btn = new Button
                    {
                        Content = "删除",
                        Style = (Style)FindResource("DangerButton"),
                        Tag = i
                    };
                    btn.Click += (s, ev) =>
                    {
                        try
                        {
                            int index = (int)((Button)s).Tag;
                            _currentTemplate.Variables.RemoveAt(index);
                            RefreshVariablesList();
                            SaveData();
                            AddLog("删除变量", "debug");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"删除变量失败: {ex.Message}", "error");
                        }
                    };
                    Grid.SetColumn(btn, 1);
                    
                    grid.Children.Add(box);
                    grid.Children.Add(btn);
                    VariablesList.Items.Add(grid);
                }
            }
            catch (Exception ex)
            {
                AddLog($"刷新变量列表失败: {ex.Message}", "error");
            }
        }
        
        private void OpenProxyConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Dialogs.ProxyConfigDialog(ProxyUrl, ProxyKey);
                if (dialog.ShowDialog() == true)
                {
                    ProxyUrl = dialog.ProxyUrl;
                    ProxyKey = dialog.ProxyKey;
                    SaveData();
                    AddLog("代理配置已更新", "success");
                }
            }
            catch (Exception ex)
            {
                AddLog($"打开代理配置失败: {ex.Message}", "error");
            }
        }
        
        private void UpdateProxyStatus()
        {
            OnPropertyChanged(nameof(ProxyStatusText));
            OnPropertyChanged(nameof(ProxyStatusBrush));
            OnPropertyChanged(nameof(ProxyWarningBrush));
            OnPropertyChanged(nameof(ProxyWarningBorder));
            OnPropertyChanged(nameof(ProxyWarningIcon));
            OnPropertyChanged(nameof(ProxyWarningTitle));
            OnPropertyChanged(nameof(ProxyWarningMessage));
            OnPropertyChanged(nameof(ProxyButtonText));
        }
        
        private void DeployConfig_Changed(object sender, SelectionChangedEventArgs e)
        {
            RefreshDeployMatrix();
        }
        
        private void RefreshDeployMatrix()
        {
            try
            {
                var group = DeployGroupCombo.SelectedItem as AccountGroup;
                var template = DeployTemplateCombo.SelectedItem as WorkerTemplate;
                
                if (group == null || template == null)
                {
                    VariablesConfigPanel.Visibility = Visibility.Collapsed;
                    MatrixPreviewPanel.Visibility = Visibility.Collapsed;
                    return;
                }
                
                AddLog($"刷新部署矩阵: 账户组={group.Name}, 模板={template.Name}", "debug");
                
                VariablesConfigPanel.Visibility = Visibility.Visible;
                DeployVariablesList.Items.Clear();
                
                foreach (var variable in template.Variables)
                {
                    var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                    var header = new StackPanel { Orientation = Orientation.Horizontal };
                    header.Children.Add(new TextBlock 
                    { 
                        Text = variable, 
                        Foreground = (Brush)FindResource("TextSecondary"),
                        FontSize = 12
                    });
                    header.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(6, 182, 212)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        Child = new TextBlock 
                        { 
                            Text = "生成 N 个 Worker", 
                            Foreground = Brushes.White,
                            FontSize = 10
                        }
                    });
                    stack.Children.Add(header);
                    
                    var box = new TextBox 
                    { 
                        Tag = variable,
                        Style = (Style)FindResource("ModernTextBox"),
                        Text = "us-east,us-west,eu-central"
                    };
                    box.TextChanged += (s, ev) => UpdateMatrixPreview();
                    stack.Children.Add(box);
                    
                    DeployVariablesList.Items.Add(stack);
                }
                
                UpdateMatrixPreview();
            }
            catch (Exception ex)
            {
                AddLog($"刷新部署矩阵失败: {ex.Message}", "error");
            }
        }
        
        private void UpdateMatrixPreview()
        {
            try
            {
                var group = DeployGroupCombo.SelectedItem as AccountGroup;
                var template = DeployTemplateCombo.SelectedItem as WorkerTemplate;
                
                if (group == null || template == null) return;
                
                var varValues = new Dictionary<string, List<string>>();
                foreach (StackPanel panel in DeployVariablesList.Items)
                {
                    var box = panel.Children[1] as TextBox;
                    var varName = box?.Tag?.ToString();
                    if (varName != null && box != null)
                    {
                        var values = box.Text.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        varValues[varName] = values.Any() ? values : new List<string> { "" };
                    }
                }
                
                DeployMatrix.Clear();
                GenerateCombinations(new Dictionary<string, string>(), 0, template.Variables, varValues, group, template);
                
                MatrixPreviewPanel.Visibility = DeployMatrix.Any() ? Visibility.Visible : Visibility.Collapsed;
                MatrixCountText.Text = $"共 {DeployMatrix.Count} 个部署任务 ({DeployMatrix.Count(i => i.Selected)} 已选择)";
                
                AddLog($"部署矩阵已更新: {DeployMatrix.Count}个任务", "debug");
                
                RefreshMatrixGrid();
            }
            catch (Exception ex)
            {
                AddLog($"更新矩阵预览失败: {ex.Message}", "error");
            }
        }
        
        private void GenerateCombinations(Dictionary<string, string> current, int index, 
            List<string> variables, Dictionary<string, List<string>> varValues, 
            AccountGroup group, WorkerTemplate template)
        {
            if (index >= variables.Count)
            {
                foreach (var account in group.Accounts)
                {
                    var workerName = ReplaceVars(template.WorkerNamePattern, current);
                    DeployMatrix.Add(new DeployMatrixItem
                    {
                        AccountId = account.AccountId,
                        AccountName = account.Name ?? account.AccountId[..Math.Min(8, account.AccountId.Length)],
                        ApiToken = account.ApiToken,
                        WorkerName = workerName,
                        Variables = new Dictionary<string, string>(current),
                        Selected = true
                    });
                }
                return;
            }
            
            var varName = variables[index];
            var values = varValues.ContainsKey(varName) ? varValues[varName] : new List<string> { "" };
            
            foreach (var val in values)
            {
                current[varName] = val;
                GenerateCombinations(current, index + 1, variables, varValues, group, template);
            }
        }
        
        private string ReplaceVars(string template, Dictionary<string, string> vars)
        {
            string result = template;
            foreach (var kv in vars)
            {
                result = result.Replace($"{{{{{kv.Key}}}}}", kv.Value);
            }
            return result;
        }
        
        private void RefreshMatrixGrid()
        {
            try
            {
                MatrixItemsPanel.Children.Clear();
                
                for (int i = 0; i < DeployMatrix.Count; i++)
                {
                    int idx = i;
                    var item = DeployMatrix[i];
                    
                    var border = new Border
                    {
                        Background = item.Selected 
                            ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) 
                            : (Brush)FindResource("BgCard"),
                        BorderBrush = item.Selected 
                            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                            : (Brush)FindResource("Border"),
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 0, 12, 12),
                        Cursor = Cursors.Hand,
                        Width = 200
                    };
                    
                    border.MouseLeftButtonDown += (s, e) =>
                    {
                        DeployMatrix[idx].Selected = !DeployMatrix[idx].Selected;
                        RefreshMatrixGrid();
                        MatrixCountText.Text = $"共 {DeployMatrix.Count} 个部署任务 ({DeployMatrix.Count(i => i.Selected)} 已选择)";
                    };
                    
                    var stack = new StackPanel();
                    stack.Children.Add(new TextBlock 
                    { 
                        Text = item.AccountName, 
                        Foreground = (Brush)FindResource("TextMuted"),
                        FontSize = 11
                    });
                    stack.Children.Add(new TextBlock 
                    { 
                        Text = item.WorkerName, 
                        Foreground = item.Selected ? Brushes.White : (Brush)FindResource("TextPrimary"),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 4, 0, 4)
                    });
                    
                    var varsText = string.Join(" ", item.Variables.Select(v => $"{v.Key}={v.Value}"));
                    stack.Children.Add(new TextBlock 
                    { 
                        Text = varsText, 
                        Foreground = new SolidColorBrush(Color.FromRgb(6, 182, 212)),
                        FontSize = 10,
                        FontFamily = new FontFamily("Consolas")
                    });
                    
                    border.Child = stack;
                    MatrixItemsPanel.Children.Add(border);
                }
            }
            catch (Exception ex)
            {
                AddLog($"刷新矩阵网格失败: {ex.Message}", "error");
            }
        }
        
        private void SelectAllMatrix_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var item in DeployMatrix) item.Selected = true;
                RefreshMatrixGrid();
                AddLog("已全选所有部署任务", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"全选失败: {ex.Message}", "error");
            }
        }
        
        private void SelectNoneMatrix_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var item in DeployMatrix) item.Selected = false;
                RefreshMatrixGrid();
                AddLog("已取消全选", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"取消全选失败: {ex.Message}", "error");
            }
        }
        
                private async void StartMatrixDeploy_Click(object sender, RoutedEventArgs e)
        {
            var items = DeployMatrix.Where(i => i.Selected).ToList();
            if (!items.Any()) return;
            
            if (string.IsNullOrEmpty(ProxyUrl))
            {
                MessageBox.Show("请先配置代理", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("部署失败: 未配置代理", "error");
                return;
            }

            var startButton = sender as Button;
            if (startButton != null) startButton.IsEnabled = false;
            
            IsDeploying = true;
            StatusText = "部署中...";
            AddLog($"开始矩阵部署: {items.Count} 个任务", "info");

            var template = DeployTemplateCombo.SelectedItem as WorkerTemplate;
            var semaphore = new SemaphoreSlim(3);
            int completed = 0;
            int success = 0;
            int failed = 0;

            try
            {
                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        string code = template?.Code ?? "";
                        foreach (var v in item.Variables)
                        {
                            code = code.Replace($"{{{{{v.Key}}}}}", v.Value);
                        }

                        var secrets = new Dictionary<string, string>();
                        if (template?.Secrets != null)
                        {
                            foreach (var s in template.Secrets)
                            {
                                if (!string.IsNullOrEmpty(s.Key))
                                {
                                    secrets[s.Key] = ReplaceVars(s.Value, item.Variables);
                                }
                            }
                        }

                        await DeployToCloudflare(item.AccountId, item.ApiToken, item.WorkerName, code, secrets);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            success++;
                            completed++;
                            AddLog($"✅ {item.WorkerName} 部署成功", "success");
                            StatusText = $"部署中... {completed}/{items.Count}";
                        });
                    }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            failed++;
                            completed++;
                            AddLog($"❌ {item.WorkerName} 失败: {ex.Message}", "error");
                            StatusText = $"部署中... {completed}/{items.Count}";
                        });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
                
                AddLog($"矩阵部署完成: 成功 {success} 个, 失败 {failed} 个", success > 0 ? "success" : "warning");
                StatusText = $"部署完成: 成功 {success}, 失败 {failed}";
            }
            catch (Exception ex)
            {
                AddLog($"部署过程异常: {ex.Message}", "error");
                MessageBox.Show($"部署失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsDeploying = false;
                if (startButton != null) startButton.IsEnabled = true;
                UpdateStatus();
            }
        }
        
        private async Task DeploySingleAsync(Profile profile)
        {
            if (string.IsNullOrEmpty(ProxyUrl))
            {
                MessageBox.Show("请先配置代理", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("部署失败: 未配置代理", "error");
                return;
            }

            if (string.IsNullOrEmpty(profile.AccountId) || 
                string.IsNullOrEmpty(profile.ApiToken) || 
                string.IsNullOrEmpty(profile.WorkerName))
            {
                MessageBox.Show("请填写完整的配置信息", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("部署失败: 配置信息不完整", "warning");
                return;
            }

            IsDeploying = true;
            StatusText = "部署中...";
            AddLog($"开始部署: {profile.WorkerName}...", "info");

            try
            {
                var secrets = profile.Secrets?.ToDictionary(s => s.Key, s => s.Value) ?? new Dictionary<string, string>();
                
                await Task.Run(async () =>
                {
                    await DeployToCloudflareInternal(profile.AccountId, profile.ApiToken, profile.WorkerName, 
                        profile.Code ?? GetDefaultWorkerCode(), secrets);
                });

                AddLog($"✅ {profile.WorkerName} 部署成功", "success");
                StatusText = "部署成功";
            }
            catch (Exception ex)
            {
                AddLog($"❌ {profile.WorkerName} 失败: {ex.Message}", "error");
                StatusText = "部署失败";
            }
            finally
            {
                IsDeploying = false;
                UpdateStatus();
            }
        }

                    private async Task DeployToCloudflareInternal(string accountId, string apiToken, string workerName, 
            string script, Dictionary<string, string> secrets)
        {
            IWebProxy? proxy = null;
            
            try 
            {
                var sysProxy = WebRequest.GetSystemWebProxy();
                var testUri = new Uri(ProxyUrl);
                var proxyUri = sysProxy.GetProxy(testUri);
                if (proxyUri != null && proxyUri.Host != testUri.Host)
                {
                    proxy = sysProxy;
                    System.Diagnostics.Debug.WriteLine($"[Deploy] 使用系统代理: {proxyUri}");
                }
            }
            catch { }

            if (proxy == null)
            {
                var localProxyUrls = new[] 
                { 
                    "http://127.0.0.1:7890",
                    "http://127.0.0.1:7891",
                    "http://127.0.0.1:10808",
                    "http://127.0.0.1:1080",
                    "http://127.0.0.1:8118",
                };

                foreach (var proxyUrl in localProxyUrls)
                {
                    try
                    {
                        var testProxy = new WebProxy(proxyUrl);
                        using var testClient = new HttpClient(new HttpClientHandler 
                        { 
                            Proxy = testProxy, 
                            UseProxy = true 
                        })
                        { 
                            Timeout = TimeSpan.FromSeconds(5) 
                        };
                        
                        var testResponse = await testClient.GetAsync("https://1.1.1.1");
                        if (testResponse.StatusCode == System.Net.HttpStatusCode.OK || 
                            (int)testResponse.StatusCode == 530)
                        {
                            proxy = testProxy;
                            System.Diagnostics.Debug.WriteLine($"[Deploy] 检测到可用代理: {proxyUrl}");
                            break;
                        }
                    }
                    catch { }
                }
            }

            var handler = new HttpClientHandler
            {
                UseProxy = proxy != null,
                Proxy = proxy,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            var request = new
            {
                accountId,
                apiToken,
                workerName,
                script,
                secrets,
                routes = new List<object>(),
                subdomain = false
            };
            
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            if (!string.IsNullOrEmpty(ProxyKey))
            {
                content.Headers.Add("Authorization", $"Bearer {ProxyKey}");
            }
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Deploy] 正在请求: {ProxyUrl}/deploy/single");
                
                var response = await client.PostAsync($"{ProxyUrl}/deploy/single", content);
                var responseText = await response.Content.ReadAsStringAsync();
                
                System.Diagnostics.Debug.WriteLine($"[Deploy] 原始响应: {responseText}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode}: {responseText}");
                }
                
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                };
                
                var result = JsonSerializer.Deserialize<DeployResponse>(responseText, options);
                
                System.Diagnostics.Debug.WriteLine($"[Deploy] 解析结果: Success={result?.Success}, Error={result?.Error}, Result={result?.Result != null}");

                if (result?.Success != true)
                {
                    var errorMsg = "部署失败";
                    if (!string.IsNullOrEmpty(result?.Error))
                        errorMsg = result.Error;
                    else if (result?.Errors != null && result.Errors.Any())
                        errorMsg = string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"));
                    
                    throw new Exception(errorMsg);
                }
                
                System.Diagnostics.Debug.WriteLine($"[Deploy] 部署成功确认");
            }
            catch (TaskCanceledException)
            {
                throw new Exception("请求超时（60秒），请检查网络连接和代理设置");
            }
            catch (HttpRequestException ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "";
                if (innerMsg.Contains("由于连接方在一段时间后没有正确答复"))
                {
                    throw new Exception("连接被重置或阻断。请确保系统代理已开启（Clash/V2Ray），或尝试切换代理节点");
                }
                throw new Exception($"网络错误: {ex.Message}");
            }
        }

        private async Task DeployToCloudflare(string accountId, string apiToken, string workerName, 
            string script, Dictionary<string, string> secrets)
        {
            IWebProxy? proxy = null;
            
            try 
            {
                var sysProxy = WebRequest.GetSystemWebProxy();
                var testUri = new Uri(ProxyUrl);
                var proxyUri = sysProxy.GetProxy(testUri);
                if (proxyUri != null && proxyUri.Host != testUri.Host)
                {
                    proxy = sysProxy;
                    AddLog($"使用系统代理: {proxyUri}", "debug");
                }
            }
            catch { }

            if (proxy == null)
            {
                var localProxyUrls = new[] 
                { 
                    "http://127.0.0.1:7890",
                    "http://127.0.0.1:7891",
                    "http://127.0.0.1:10808",
                    "http://127.0.0.1:1080",
                    "http://127.0.0.1:8118",
                };

                foreach (var proxyUrl in localProxyUrls)
                {
                    try
                    {
                        var testProxy = new WebProxy(proxyUrl);
                        using var testClient = new HttpClient(new HttpClientHandler 
                        { 
                            Proxy = testProxy, 
                            UseProxy = true 
                        })
                        { 
                            Timeout = TimeSpan.FromSeconds(5) 
                        };
                        
                        var testResponse = await testClient.GetAsync("https://1.1.1.1");
                        if (testResponse.StatusCode == System.Net.HttpStatusCode.OK || 
                            (int)testResponse.StatusCode == 530)
                        {
                            proxy = testProxy;
                            AddLog($"检测到可用代理: {proxyUrl}", "success");
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (proxy == null)
            {
                AddLog("警告: 未检测到代理，直接连接可能失败", "warning");
            }

            var handler = new HttpClientHandler
            {
                UseProxy = proxy != null,
                Proxy = proxy,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            var request = new
            {
                accountId,
                apiToken,
                workerName,
                script,
                secrets,
                routes = new List<object>(),
                subdomain = false
            };
            
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            if (!string.IsNullOrEmpty(ProxyKey))
            {
                content.Headers.Add("Authorization", $"Bearer {ProxyKey}");
            }
            
            try
            {
                AddLog($"正在请求: {ProxyUrl}/deploy/single (通过代理: {proxy != null})", "debug");
                
                var response = await client.PostAsync($"{ProxyUrl}/deploy/single", content);
                var responseText = await response.Content.ReadAsStringAsync();
                
                System.Diagnostics.Debug.WriteLine($"[Deploy] 原始响应: {responseText}");
                
                AddLog($"响应状态: {(int)response.StatusCode}", "debug");
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode}: {responseText}");
                }
                
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                };
                
                var result = JsonSerializer.Deserialize<DeployResponse>(responseText, options);
                
                System.Diagnostics.Debug.WriteLine($"[Deploy] 解析结果: Success={result?.Success}, Error={result?.Error}");

                if (result?.Success != true)
                {
                    var errorMsg = "部署失败";
                    if (!string.IsNullOrEmpty(result?.Error))
                        errorMsg = result.Error;
                    else if (result?.Errors != null && result.Errors.Any())
                        errorMsg = string.Join("; ", result.Errors.Select(e => $"[{e.Code}] {e.Message}"));
                    
                    throw new Exception(errorMsg);
                }
            }
            catch (TaskCanceledException)
            {
                throw new Exception("请求超时（60秒），请检查网络连接和代理设置");
            }
            catch (HttpRequestException ex)
            {
                var innerMsg = ex.InnerException?.Message ?? "";
                if (innerMsg.Contains("由于连接方在一段时间后没有正确答复"))
                {
                    throw new Exception("连接被重置或阻断。请确保系统代理已开启（Clash/V2Ray），或尝试切换代理节点");
                }
                throw new Exception($"网络错误: {ex.Message}");
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var data = new AppData
                {
                    ProxyUrl = ProxyUrl,
                    ProxyKey = ProxyKey,
                    Profiles = Profiles.ToList(),
                    AccountGroups = AccountGroups.ToList(),
                    Templates = Templates.ToList()
                };
                
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"cf-deploy-backup-{DateTime.Now:yyyyMMdd}.json"
                };
                
                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, json);
                    AddLog("配置已导出", "success");
                }
            }
            catch (Exception ex)
            {
                AddLog($"导出配置失败: {ex.Message}", "error");
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var data = JsonSerializer.Deserialize<AppData>(json);
                    
                    if (data != null)
                    {
                        ProxyUrl = data.ProxyUrl ?? "";
                        ProxyKey = data.ProxyKey ?? "";
                        
                        Profiles.Clear();
                        foreach (var p in data.Profiles ?? new List<Profile>())
                            Profiles.Add(p);
                            
                        AccountGroups.Clear();
                        foreach (var g in data.AccountGroups ?? new List<AccountGroup>())
                            AccountGroups.Add(g);
                            
                        Templates.Clear();
                        foreach (var t in data.Templates ?? new List<WorkerTemplate>())
                            Templates.Add(t);
                        
                        SaveData();
                        RefreshUI();
                        AddLog("配置已导入", "success");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    AddLog($"导入配置失败: {ex.Message}", "error");
                }
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定重置所有数据吗？此操作不可恢复！", "确认重置",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    StorageService.Reset();
                    Profiles.Clear();
                    AccountGroups.Clear();
                    Templates.Clear();
                    ProxyUrl = "";
                    ProxyKey = "";
                    RefreshUI();
                    AddLog("数据已重置", "warning");
                }
                catch (Exception ex)
                {
                    AddLog($"重置数据失败: {ex.Message}", "error");
                }
            }
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class DeployResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<ErrorDetail>? Errors { get; set; }
        public object? Result { get; set; }
    }

    public class ErrorDetail
    {
        public int Code { get; set; }
        public string? Message { get; set; }
    }
}