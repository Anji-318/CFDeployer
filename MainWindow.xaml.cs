using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        private PagesProject? _currentPagesProject;
        private ObservableCollection<LogEntry> _logs = new();
        private bool _isUpdatingToken = false;
        private bool _isUpdatingPagesToken = false;
        
        // 添加日志筛选字段
        private string _currentLogFilter = "all";
        private ObservableCollection<LogEntry> _allLogs = new();

        // ===== 代码处理相关字段 =====
        private WorkerCodeProcessor _codeProcessor = new WorkerCodeProcessor();
        private ContextMenu _profileDecodeMenu = null!;
        private ContextMenu _profileObfuscateMenu = null!;
        private ContextMenu _profileFormatMenu = null!;
        private ContextMenu _templateDecodeMenu = null!;
        private ContextMenu _templateObfuscateMenu = null!;
        private ContextMenu _templateFormatMenu = null!;
        
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
        public ObservableCollection<PagesProject> PagesProjects { get; set; } = new();
        public ObservableCollection<LogEntry> Logs 
        { 
            get => _logs; 
            set { _logs = value; OnPropertyChanged(nameof(Logs)); }
        }
        
        public ObservableCollection<DeployMatrixItem> DeployMatrix { get; set; } = new();
        public ObservableCollection<DeployMatrixItem> PagesDeployMatrix { get; set; } = new();
        
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

            // 初始化代码菜单（必须在UI加载完成后）
            InitializeCodeMenus();
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

        #region 代码工具按钮功能

        private void InitializeCodeMenus()
        {
            // Profile 代码菜单
            _profileDecodeMenu = new ContextMenu();
            AddMenuItem(_profileDecodeMenu, "🔓 Base64 解码", () => ApplyToProfileCode(_codeProcessor.DecodeBase64));
            AddMenuItem(_profileDecodeMenu, "🔓 Unicode 解码", () => ApplyToProfileCode(_codeProcessor.DecodeUnicode));
            AddMenuItem(_profileDecodeMenu, "🔓 十六进制解码", () => ApplyToProfileCode(_codeProcessor.DecodeHex));
            _profileDecodeMenu.Items.Add(new Separator());
            AddMenuItem(_profileDecodeMenu, "✨ 智能反混淆", () => ApplyToProfileCode(_codeProcessor.Deobfuscate));

            _profileObfuscateMenu = new ContextMenu();
            AddMenuItem(_profileObfuscateMenu, "🔒 轻度混淆", () => ApplyToProfileCode(_codeProcessor.ObfuscateLight));
            AddMenuItem(_profileObfuscateMenu, "🔒 中度混淆", () => ApplyToProfileCode(_codeProcessor.ObfuscateMedium));

            _profileFormatMenu = new ContextMenu();
            AddMenuItem(_profileFormatMenu, "📝 格式化代码", () => ApplyToProfileCode(_codeProcessor.Format));
            AddMenuItem(_profileFormatMenu, "📦 压缩代码", () => ApplyToProfileCode(_codeProcessor.Minify));

            // Template 代码菜单
            _templateDecodeMenu = new ContextMenu();
            AddMenuItem(_templateDecodeMenu, "🔓 Base64 解码", () => ApplyToTemplateCode(_codeProcessor.DecodeBase64));
            AddMenuItem(_templateDecodeMenu, "🔓 Unicode 解码", () => ApplyToTemplateCode(_codeProcessor.DecodeUnicode));
            AddMenuItem(_templateDecodeMenu, "✨ 智能反混淆", () => ApplyToTemplateCode(_codeProcessor.Deobfuscate));

            _templateObfuscateMenu = new ContextMenu();
            AddMenuItem(_templateObfuscateMenu, "🔒 轻度混淆", () => ApplyToTemplateCode(_codeProcessor.ObfuscateLight));
            AddMenuItem(_templateObfuscateMenu, "🔒 中度混淆", () => ApplyToTemplateCode(_codeProcessor.ObfuscateMedium));

            _templateFormatMenu = new ContextMenu();
            AddMenuItem(_templateFormatMenu, "📝 格式化代码", () => ApplyToTemplateCode(_codeProcessor.Format));
            AddMenuItem(_templateFormatMenu, "📦 压缩代码", () => ApplyToTemplateCode(_codeProcessor.Minify));
        }

        private void AddMenuItem(ContextMenu menu, string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        private void ApplyToProfileCode(Func<string, string> transform)
        {
            try
            {
                ProfileCode.Text = transform(ProfileCode.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyToTemplateCode(Func<string, string> transform)
        {
            try
            {
                TemplateCode.Text = transform(TemplateCode.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowMenu(ContextMenu menu, Button? button)
{
    if (button == null) return;
    menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
    menu.PlacementTarget = button;
    menu.IsOpen = true;
}

                // ========== Profile 代码按钮事件 ==========

                private void ProfileAnalyzeBtn_Click(object sender, RoutedEventArgs e)
        {
            var code = ProfileCode.Text;
            var result = WorkerCodeAnalyzer.Analyze(code);
            
            // 输出分析结果到日志
            AddLog($"📊 代码分析 - 总行数:{result.TotalLines} 代码行:{result.CodeLines} 复杂度:{result.Complexity}", "info");
            
            if (result.DetectedPatterns.Any())
            {
                AddLog($"🔍 检测到模式: {string.Join(", ", result.DetectedPatterns)}", "debug");
            }
            
            if (result.Suggestions.Any())
            {
                foreach (var suggestion in result.Suggestions)
                {
                    AddLog($"💡 {suggestion}", "warning");
                }
            }
            else
            {
                AddLog("✅ 代码结构良好，暂无建议", "success");
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📊 代码分析报告");
            sb.AppendLine(new string('=', 30));
            sb.AppendLine($"总行数: {result.TotalLines}");
            sb.AppendLine($"代码行: {result.CodeLines}");
            sb.AppendLine($"注释行: {result.CommentLines}");
            sb.AppendLine($"空行: {result.EmptyLines}");
            sb.AppendLine($"复杂度: {result.Complexity}");
            sb.AppendLine();
            
            if (result.DetectedPatterns.Any())
            {
                sb.AppendLine("🔍 检测到的模式:");
                foreach (var pattern in result.DetectedPatterns)
                    sb.AppendLine($"  • {pattern}");
                sb.AppendLine();
            }
            
            if (result.Suggestions.Any())
            {
                sb.AppendLine("💡 建议:");
                foreach (var suggestion in result.Suggestions)
                    sb.AppendLine($"  • {suggestion}");
            }
            else
            {
                sb.AppendLine("✅ 代码结构良好，暂无建议");
            }

            MessageBox.Show(sb.ToString(), "代码分析结果", MessageBoxButton.OK, MessageBoxImage.Information);
            AddLog("已完成代码分析", "success");
        }

        private void ProfileDecodeBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowMenu(_profileDecodeMenu, sender as Button);
        }

        private void ProfileObfuscateBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowMenu(_profileObfuscateMenu, sender as Button);
        }

        private void ProfileFormatBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowMenu(_profileFormatMenu, sender as Button);
        }

        // ========== Template 代码按钮事件 ==========

                private void TemplateAnalyzeBtn_Click(object sender, RoutedEventArgs e)
        {
            var code = TemplateCode.Text;
            var result = WorkerCodeAnalyzer.Analyze(code);
            
            // 输出分析结果到日志
            AddLog($"📊 代码模板分析 - 总行数:{result.TotalLines} 代码行:{result.CodeLines} 复杂度:{result.Complexity}", "info");
            
            // 检查模板变量
            var varMatches = System.Text.RegularExpressions.Regex.Matches(code, @"\{\{(\w+)\}\}");
            var vars = varMatches.Select(m => m.Groups[1].Value).Distinct().ToList();
            if (vars.Any())
            {
                AddLog($"📝 模板变量: {string.Join(", ", vars.Select(v => $"{{{{{v}}}}}"))}", "debug");
            }
            
            if (result.DetectedPatterns.Any())
            {
                AddLog($"🔍 检测到模式: {string.Join(", ", result.DetectedPatterns)}", "debug");
            }
            
            if (result.Suggestions.Any())
            {
                foreach (var suggestion in result.Suggestions)
                {
                    AddLog($"💡 {suggestion}", "warning");
                }
            }
            else
            {
                AddLog("✅ 模板结构良好，暂无建议", "success");
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📊 代码模板分析报告");
            sb.AppendLine(new string('=', 30));
            sb.AppendLine($"总行数: {result.TotalLines}");
            sb.AppendLine($"代码行: {result.CodeLines}");
            sb.AppendLine($"注释行: {result.CommentLines}");
            sb.AppendLine($"空行: {result.EmptyLines}");
            sb.AppendLine($"复杂度: {result.Complexity}");
            sb.AppendLine();
            
            if (result.DetectedPatterns.Any())
            {
                sb.AppendLine("🔍 检测到的模式:");
                foreach (var pattern in result.DetectedPatterns)
                    sb.AppendLine($"  • {pattern}");
                sb.AppendLine();
            }
            
            if (vars.Any())
            {
                sb.AppendLine("📝 模板变量:");
                foreach (var v in vars)
                    sb.AppendLine($"  • {{{{{v}}}}}");
                sb.AppendLine();
            }
            
            if (result.Suggestions.Any())
            {
                sb.AppendLine("💡 建议:");
                foreach (var suggestion in result.Suggestions)
                    sb.AppendLine($"  • {suggestion}");
            }
            else
            {
                sb.AppendLine("✅ 模板结构良好，暂无建议");
            }

            MessageBox.Show(sb.ToString(), "代码模板分析结果", MessageBoxButton.OK, MessageBoxImage.Information);
            AddLog("已完成代码模板分析", "success");
        }

        private void TemplateDecodeBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowMenu(_templateDecodeMenu, sender as Button);
        }

        private void TemplateObfuscateBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowMenu(_templateObfuscateMenu, sender as Button);
        }

        private void TemplateFormatBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowMenu(_templateFormatMenu, sender as Button);
        }

        private void VersionText_Click(object sender, MouseButtonEventArgs e)
        {
            var aboutWindow = new Window
            {
                Title = "关于 Cloudflare Deployer",
                Width = 420,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = IsDarkMode ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 标题
            var title = new TextBlock
            {
                Text = "Cloudflare Deployer",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = IsDarkMode ? new SolidColorBrush(Color.FromRgb(248, 250, 252)) : new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            Grid.SetRow(title, 0);

            // 版本号
            var version = new TextBlock
            {
                Text = "版本: v1.1.0",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(version, 1);

            // 简介
            var desc = new TextBlock
            {
                Text = "多账户批量部署系统，支持 Worker / Pages 矩阵部署、代理中转、代码处理等功能。",
                FontSize = 13,
                Foreground = IsDarkMode ? new SolidColorBrush(Color.FromRgb(148, 163, 184)) : new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0)
            };
            Grid.SetRow(desc, 2);

            // 更新简介
            var updateTitle = new TextBlock
            {
                Text = "v1.1.0 更新内容:",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = IsDarkMode ? new SolidColorBrush(Color.FromRgb(248, 250, 252)) : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                Margin = new Thickness(0, 16, 0, 4)
            };
            Grid.SetRow(updateTitle, 3);

            var updateContent = new TextBlock
            {
                Text = "• 新增 Cloudflare Pages 项目部署与矩阵批量部署\n• 修复 IsDeploying 状态、Base64 编码、AccountId 空值等 BUG\n• 统一部署入口，移除重复部署代码\n• Pages 直连 api.cloudflare.com，内置 429 限流保护",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(updateContent, 4);

            // 项目地址链接
            var linkPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            var linkText = new TextBlock
            {
                Text = "项目地址",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand
            };
            linkText.MouseLeftButtonDown += (s, ev) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/Anji-318/CFDeployer",
                    UseShellExecute = true
                });
            };
            linkPanel.Children.Add(linkText);
            linkPanel.Children.Add(new TextBlock 
            { 
                Text = " →", 
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(59, 130, 246))
            });
            Grid.SetRow(linkPanel, 5);
            linkPanel.VerticalAlignment = VerticalAlignment.Bottom;

            grid.Children.Add(title);
            grid.Children.Add(version);
            grid.Children.Add(desc);
            grid.Children.Add(updateTitle);
            grid.Children.Add(updateContent);
            grid.Children.Add(linkPanel);

            aboutWindow.Content = grid;
            aboutWindow.ShowDialog();
            
            AddLog("打开关于窗口", "debug");
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

                    PagesProjects.Clear();
                    foreach (var p in data.PagesProjects ?? new List<PagesProject>())
                        PagesProjects.Add(p);
                    
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
                            Secrets = new List<Secret>(),
                            EnvironmentVariables = new List<Secret>()
                        });
                    }
                    
                    AddLog($"配置数据已加载: {Profiles.Count}个配置, {AccountGroups.Count}个账户组, {Templates.Count}个模板, {PagesProjects.Count}个Pages项目", "success");
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
                // 同步当前 Pages 项目
                SyncCurrentPagesProjectFromUI();

                var data = new AppData
                {
                    ProxyUrl = ProxyUrl,
                    ProxyKey = ProxyKey,
                    Profiles = Profiles.ToList(),
                    AccountGroups = AccountGroups.ToList(),
                    Templates = Templates.ToList(),
                    PagesProjects = PagesProjects.ToList()
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

            PagesProjectsList.ItemsSource = null;
            PagesProjectsList.ItemsSource = PagesProjects;
            
            DeployGroupCombo.ItemsSource = null;
            DeployGroupCombo.ItemsSource = AccountGroups;
            
            DeployTemplateCombo.ItemsSource = null;
            DeployTemplateCombo.ItemsSource = Templates;

            // 绑定 Pages 矩阵部署的账户组下拉框（如果已存在）
            if (PagesDeployGroupCombo != null)
            {
                PagesDeployGroupCombo.ItemsSource = null;
                PagesDeployGroupCombo.ItemsSource = AccountGroups;
            }
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
                TabPages.IsChecked = false;
                
                PageProfiles.Visibility = Visibility.Collapsed;
                PageAccounts.Visibility = Visibility.Collapsed;
                PageTemplates.Visibility = Visibility.Collapsed;
                PageDeploy.Visibility = Visibility.Collapsed;
                PagePages.Visibility = Visibility.Collapsed;
                
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
                    case "Pages":
                        PagePages.Visibility = Visibility.Visible;
                        AddLog("切换到Pages项目页面", "debug");
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
            Code = GetDefaultWorkerCode(),
            Secrets = new List<Secret>(),
            EnvironmentVariables = new List<Secret>(),
            Routes = new List<Route>()
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
        
        // 刷新 Secrets 列表
        SecretsList.ItemsSource = null;
        SecretsList.ItemsSource = profile.Secrets;
        
        // 刷新环境变量列表
        ProfileEnvVarsList.ItemsSource = null;
        ProfileEnvVarsList.ItemsSource = profile.EnvironmentVariables;
        
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
                    SecretsList.ItemsSource = null;
                    ProfileEnvVarsList.ItemsSource = null;
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
                SaveData();
                AddLog("添加 Secret", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"添加 Secret 失败: {ex.Message}", "error");
            }
        }

        // 新增：添加 Profile 环境变量
        private void AddProfileEnvVar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentProfile == null)
                {
                    MessageBox.Show("请先选择一个配置", "提示", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _currentProfile.EnvironmentVariables.Add(new Secret 
                { 
                    Key = "VAR_NAME", 
                    Value = "" 
                });
                ProfileEnvVarsList.ItemsSource = null;
                ProfileEnvVarsList.ItemsSource = _currentProfile.EnvironmentVariables;
                SaveData();
                AddLog("添加环境变量到配置", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"添加环境变量失败: {ex.Message}", "error");
            }
        }

        #region Pages 项目管理

        private void CreatePagesProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var project = new PagesProject
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"新Pages项目 {PagesProjects.Count + 1}",
                    Branch = "main",
                    EnvironmentVariables = new List<Secret>()
                };
                PagesProjects.Add(project);
                SelectPagesProject(project);
                SaveData();
                AddLog($"创建新Pages项目: {project.Name}", "success");
            }
            catch (Exception ex)
            {
                AddLog($"创建Pages项目失败: {ex.Message}", "error");
            }
        }

        private void PagesProjectItem_Select(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element)
            {
                var parent = element;
                while (parent != null)
                {
                    if (parent is Button)
                        return;
                    parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
                }
            }

            if (sender is Border border && border.DataContext is PagesProject project)
            {
                SelectPagesProject(project);
            }
        }

        private void SelectPagesProject(PagesProject project)
        {
            try
            {
                _currentPagesProject = project;
                UnsubscribePagesProjectEvents();

                PagesProjectName.Text = project.Name;
                PagesAccountId.Text = project.AccountId;

                _isUpdatingPagesToken = true;
                PagesApiToken.Password = project.ApiToken ?? "";
                PagesApiTokenVisible.Text = project.ApiToken ?? "";
                _isUpdatingPagesToken = false;

                PagesProjectProjectName.Text = project.ProjectName;
                PagesBranch.Text = project.Branch;
                PagesDeployTypeCombo.SelectedIndex = project.DeployType == PagesDeployType.PagesFunction ? 1 : 0;
                PagesStaticDir.Text = project.StaticDir ?? "";
                PagesCode.Text = project.Code ?? "";

                PagesEnvVarsList.ItemsSource = null;
                PagesEnvVarsList.ItemsSource = project.EnvironmentVariables;

                SubscribePagesProjectEvents();
                AddLog($"已选择Pages项目: {project.Name}", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"选择Pages项目失败: {ex.Message}", "error");
            }
        }

        private void SubscribePagesProjectEvents()
        {
            PagesProjectName.TextChanged += PagesProjectName_TextChanged;
            PagesAccountId.TextChanged += PagesAccountId_TextChanged;
            PagesProjectProjectName.TextChanged += PagesProjectProjectName_TextChanged;
            PagesBranch.TextChanged += PagesBranch_TextChanged;
            PagesStaticDir.TextChanged += PagesStaticDir_TextChanged;
            PagesCode.TextChanged += PagesCode_TextChanged;
            PagesDeployTypeCombo.SelectionChanged += PagesDeployTypeCombo_SelectionChanged;

            PagesApiToken.PasswordChanged += PagesApiToken_Save;
            PagesApiTokenVisible.TextChanged += PagesApiTokenVisible_Save;
        }

        private void UnsubscribePagesProjectEvents()
        {
            PagesProjectName.TextChanged -= PagesProjectName_TextChanged;
            PagesAccountId.TextChanged -= PagesAccountId_TextChanged;
            PagesProjectProjectName.TextChanged -= PagesProjectProjectName_TextChanged;
            PagesBranch.TextChanged -= PagesBranch_TextChanged;
            PagesStaticDir.TextChanged -= PagesStaticDir_TextChanged;
            PagesCode.TextChanged -= PagesCode_TextChanged;
            PagesDeployTypeCombo.SelectionChanged -= PagesDeployTypeCombo_SelectionChanged;

            PagesApiToken.PasswordChanged -= PagesApiToken_Save;
            PagesApiTokenVisible.TextChanged -= PagesApiTokenVisible_Save;
        }

        private void PagesProjectName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentPagesProject != null)
            {
                _currentPagesProject.Name = PagesProjectName.Text;
                RefreshPagesProjectList();
                DebounceSave();
            }
        }

        private void PagesAccountId_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentPagesProject != null)
            {
                _currentPagesProject.AccountId = PagesAccountId.Text;
                RefreshPagesProjectList();
                DebounceSave();
            }
        }

        private void PagesProjectProjectName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentPagesProject != null)
            {
                _currentPagesProject.ProjectName = PagesProjectProjectName.Text;
                RefreshPagesProjectList();
                DebounceSave();
            }
        }

        private void PagesBranch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentPagesProject != null)
            {
                _currentPagesProject.Branch = PagesBranch.Text;
                DebounceSave();
            }
        }

        private void PagesStaticDir_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentPagesProject != null)
            {
                _currentPagesProject.StaticDir = PagesStaticDir.Text;
                DebounceSave();
            }
        }

        private void PagesCode_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentPagesProject != null)
            {
                _currentPagesProject.Code = PagesCode.Text;
                DebounceSave();
            }
        }

        private void PagesDeployTypeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_currentPagesProject != null)
            {
                _currentPagesProject.DeployType = PagesDeployTypeCombo.SelectedIndex == 1
                    ? PagesDeployType.PagesFunction
                    : PagesDeployType.DirectUpload;
                RefreshPagesProjectList();
                DebounceSave();
            }
        }

        private void PagesApiToken_Save(object sender, RoutedEventArgs e)
        {
            if (_currentPagesProject != null && !_isUpdatingPagesToken)
            {
                _currentPagesProject.ApiToken = PagesApiToken.Password;
                DebounceSave();
            }
        }

        private void PagesApiTokenVisible_Save(object sender, TextChangedEventArgs e)
        {
            if (_currentPagesProject != null && !_isUpdatingPagesToken)
            {
                _currentPagesProject.ApiToken = PagesApiTokenVisible.Text;
                DebounceSave();
            }
        }

        private void PagesApiToken_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingPagesToken) return;
            _isUpdatingPagesToken = true;
            if (PagesApiTokenVisible != null)
            {
                PagesApiTokenVisible.Text = PagesApiToken.Password;
            }
            _isUpdatingPagesToken = false;
        }

        private void PagesApiTokenVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingPagesToken) return;
            _isUpdatingPagesToken = true;
            if (PagesApiToken != null)
            {
                PagesApiToken.Password = PagesApiTokenVisible.Text;
            }
            _isUpdatingPagesToken = false;
        }

        private void TogglePagesTokenVisibility_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PagesApiToken.Visibility == Visibility.Visible)
                {
                    PagesApiToken.Visibility = Visibility.Collapsed;
                    PagesApiTokenVisible.Visibility = Visibility.Visible;
                    PagesApiTokenVisible.Text = PagesApiToken.Password;
                    TogglePagesTokenBtn.Content = "🙈";
                    AddLog("Pages API Token 已显示", "debug");
                }
                else
                {
                    PagesApiToken.Visibility = Visibility.Visible;
                    PagesApiTokenVisible.Visibility = Visibility.Collapsed;
                    PagesApiToken.Password = PagesApiTokenVisible.Text;
                    TogglePagesTokenBtn.Content = "👁️";
                    AddLog("Pages API Token 已隐藏", "debug");
                }
            }
            catch (Exception ex)
            {
                AddLog($"切换Pages Token显示失败: {ex.Message}", "error");
            }
        }

        private void AddPagesEnvVar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentPagesProject == null)
                {
                    MessageBox.Show("请先选择一个Pages项目", "提示",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _currentPagesProject.EnvironmentVariables.Add(new Secret
                {
                    Key = "VAR_NAME",
                    Value = ""
                });
                PagesEnvVarsList.ItemsSource = null;
                PagesEnvVarsList.ItemsSource = _currentPagesProject.EnvironmentVariables;
                SaveData();
                AddLog("添加环境变量到Pages项目", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"添加Pages环境变量失败: {ex.Message}", "error");
            }
        }

        private void BrowsePagesStaticDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择 Pages 静态文件目录",
                    Multiselect = false
                };

                if (!string.IsNullOrEmpty(PagesStaticDir.Text) && Directory.Exists(PagesStaticDir.Text))
                {
                    dialog.FolderName = PagesStaticDir.Text;
                }

                if (dialog.ShowDialog() == true)
                {
                    var selectedPath = dialog.FolderName;
                    PagesStaticDir.Text = selectedPath;
                    if (_currentPagesProject != null)
                    {
                        _currentPagesProject.StaticDir = selectedPath;
                        DebounceSave();
                    }
                    AddLog($"已选择静态文件目录: {selectedPath}", "debug");
                }
            }
            catch (Exception ex)
            {
                AddLog($"选择目录失败: {ex.Message}", "error");
            }
        }

        private void DeletePagesProject_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is Button btn && btn.Tag is PagesProject project)
            {
                if (MessageBox.Show($"确定删除Pages项目 \"{project.Name}\" 吗？", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        PagesProjects.Remove(project);
                        if (_currentPagesProject == project)
                        {
                            _currentPagesProject = null;
                            UnsubscribePagesProjectEvents();
                            PagesProjectName.Text = "";
                            PagesAccountId.Text = "";
                            PagesApiToken.Password = "";
                            PagesApiTokenVisible.Text = "";
                            PagesProjectProjectName.Text = "";
                            PagesBranch.Text = "main";
                            PagesStaticDir.Text = "";
                            PagesCode.Text = "";
                            PagesEnvVarsList.ItemsSource = null;
                        }
                        SaveData();
                        RefreshUI();
                        AddLog($"删除Pages项目成功: {project.Name}", "success");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"删除Pages项目失败: {ex.Message}", "error");
                    }
                }
            }
        }

        private void DeployPagesProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PagesProject project)
            {
                _ = DeployPagesProjectAsync(project);
            }
        }

        private async void DeployPagesCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPagesProject == null) return;
            await DeployPagesProjectAsync(_currentPagesProject);
        }

        private async Task DeployPagesProjectAsync(PagesProject project)
        {
            if (string.IsNullOrWhiteSpace(project.AccountId) ||
                string.IsNullOrWhiteSpace(project.ApiToken) ||
                string.IsNullOrWhiteSpace(project.ProjectName))
            {
                MessageBox.Show("请填写完整的Pages项目信息", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("Pages部署失败: 项目信息不完整", "error");
                return;
            }

            IsDeploying = true;
            StatusText = $"正在部署 Pages: {project.ProjectName}...";
            AddLog($"开始部署Pages项目: {project.ProjectName}...", "info");

            try
            {
                var job = new PagesDeployJob
                {
                    AccountId = project.AccountId.Trim(),
                    ApiToken = project.ApiToken.Trim(),
                    ProjectName = project.ProjectName.Trim(),
                    Branch = string.IsNullOrWhiteSpace(project.Branch) ? "main" : project.Branch.Trim(),
                    StaticDir = project.StaticDir,
                    Script = project.Code,
                    DeployType = project.DeployType,
                    EnvironmentVariables = project.EnvironmentVariables?.ToDictionary(s => s.Key, s => s.Value)
                                           ?? new Dictionary<string, string>()
                };

                var result = await PagesDeployService.DeploySingleAsync(job);
                if (!result.Success)
                {
                    throw new Exception(result.Error);
                }

                AddLog($"✅ Pages项目 {project.ProjectName} 部署成功", "success");
                StatusText = "Pages部署成功";
            }
            catch (Exception ex)
            {
                AddLog($"❌ Pages项目 {project.ProjectName} 部署失败: {ex.Message}", "error");
                StatusText = "Pages部署失败";
            }
            finally
            {
                IsDeploying = false;
                UpdateStatus();
            }
        }

        private void SyncCurrentPagesProjectFromUI()
        {
            if (_currentPagesProject == null) return;

            _currentPagesProject.Name = PagesProjectName.Text;
            _currentPagesProject.AccountId = PagesAccountId.Text;
            _currentPagesProject.ApiToken = PagesApiToken.Password;
            _currentPagesProject.ProjectName = PagesProjectProjectName.Text;
            _currentPagesProject.Branch = PagesBranch.Text;
            _currentPagesProject.DeployType = PagesDeployTypeCombo.SelectedIndex == 1
                ? PagesDeployType.PagesFunction
                : PagesDeployType.DirectUpload;
            _currentPagesProject.StaticDir = PagesStaticDir.Text;
            _currentPagesProject.Code = PagesCode.Text;
        }

        private void RefreshPagesProjectList()
        {
            var temp = PagesProjectsList.ItemsSource;
            PagesProjectsList.ItemsSource = null;
            PagesProjectsList.ItemsSource = temp;
        }

        #endregion

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
            // 检查是否是 Profile 的环境变量
            if (_currentProfile?.EnvironmentVariables.Contains(secret) == true)
            {
                _currentProfile.EnvironmentVariables.Remove(secret);
                ProfileEnvVarsList.ItemsSource = null;
                ProfileEnvVarsList.ItemsSource = _currentProfile.EnvironmentVariables;
                SaveData();
                AddLog("删除环境变量", "debug");
                return;
            }
            
            // 否则是 Secrets
            if (_currentProfile?.Secrets.Contains(secret) == true)
            {
                _currentProfile.Secrets.Remove(secret);
                SecretsList.ItemsSource = null;
                SecretsList.ItemsSource = _currentProfile.Secrets;
                SaveData();
                AddLog("删除 Secret", "debug");
                return;
            }
            
            // 检查是否是模板的环境变量 - 通过 Key 匹配
            if (_currentTemplate?.EnvironmentVariables != null)
            {
                var templateEnvVar = _currentTemplate.EnvironmentVariables
                    .FirstOrDefault(ev => ev.Key == secret.Key);
                
                if (templateEnvVar != null)
                {
                    _currentTemplate.EnvironmentVariables.Remove(templateEnvVar);
                    RefreshEnvVars();
                    SaveData();
                    AddLog("删除模板环境变量", "debug");
                    return;
                }
            }

            // 检查是否是 Pages 项目的环境变量
            if (_currentPagesProject?.EnvironmentVariables.Contains(secret) == true)
            {
                _currentPagesProject.EnvironmentVariables.Remove(secret);
                PagesEnvVarsList.ItemsSource = null;
                PagesEnvVarsList.ItemsSource = _currentPagesProject.EnvironmentVariables;
                SaveData();
                AddLog("删除Pages环境变量", "debug");
                return;
            }
        }
        catch (Exception ex)
        {
            AddLog($"删除变量失败: {ex.Message}", "error");
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
                    Secrets = new List<Secret>(),
                    EnvironmentVariables = new List<Secret>()
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
                            // 清空环境变量列表
                            EnvVarsList.ItemsSource = null;
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
                // 新增：刷新环境变量列表
                RefreshEnvVars();
                
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
        
        // 新增：添加环境变量按钮点击事件
        private void AddEnvVar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentTemplate == null)
                {
                    MessageBox.Show("请先选择一个 Worker 模板", "提示", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _currentTemplate.EnvironmentVariables.Add(new Secret 
                { 
                    Key = "VAR_NAME", 
                    Value = "" 
                });
                RefreshEnvVars();
                SaveData();
                AddLog("添加环境变量到模板", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"添加环境变量失败: {ex.Message}", "error");
            }
        }

        // 新增：刷新环境变量列表显示
        private void RefreshEnvVars()
        {
            if (EnvVarsList == null) return;
            
            EnvVarsList.ItemsSource = null;
            EnvVarsList.ItemsSource = _currentTemplate?.EnvironmentVariables;
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

            try
            {
                var service = new DeployService(ProxyUrl, ProxyKey);

                var jobs = items.Select(item => new DeployJob
                {
                    AccountId = item.AccountId,
                    ApiToken = item.ApiToken,
                    WorkerName = item.WorkerName,
                    Script = ReplaceVars(template?.Code ?? "", item.Variables),
                    Secrets = template?.Secrets?.Where(s => !string.IsNullOrEmpty(s.Key))
                        .ToDictionary(s => s.Key, s => ReplaceVars(s.Value, item.Variables)) ?? new Dictionary<string, string>(),
                    EnvironmentVariables = template?.EnvironmentVariables?.Where(s => !string.IsNullOrEmpty(s.Key))
                        .ToDictionary(s => s.Key, s => ReplaceVars(s.Value, item.Variables)) ?? new Dictionary<string, string>(),
                    Routes = new List<Route>(),
                    Subdomain = false
                }).ToList();

                int completed = 0;
                int success = 0;
                int failed = 0;

                var progress = new Progress<(int index, string status, string? error)>(update =>
                {
                    var item = items[update.index];
                    completed++;
                    if (update.status == "success")
                    {
                        success++;
                        AddLog($"✅ {item.WorkerName} 部署成功", "success");
                    }
                    else if (update.status == "error")
                    {
                        failed++;
                        AddLog($"❌ {item.WorkerName} 失败: {update.error}", "error");
                    }
                    StatusText = $"部署中... {completed}/{items.Count}";
                });

                await service.DeployBatchAsync(jobs, 3, progress);

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

        #region Pages 矩阵部署

        private void PagesDeployConfig_Changed(object sender, SelectionChangedEventArgs e)
        {
            RefreshPagesDeployMatrix();
        }

        private void PagesProjectNamePattern_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshPagesDeployMatrix();
        }

        private void RefreshPagesDeployMatrix()
        {
            try
            {
                var group = PagesDeployGroupCombo.SelectedItem as AccountGroup;
                var pattern = PagesProjectNamePattern?.Text ?? "";

                if (group == null || string.IsNullOrWhiteSpace(pattern))
                {
                    PagesMatrixPreviewPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                var variables = ExtractVariables(pattern);
                PagesMatrixVariablesList.Items.Clear();

                foreach (var variable in variables)
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
                            Text = "生成 N 个 Pages",
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
                    box.TextChanged += (s, ev) => UpdatePagesMatrixPreview();
                    stack.Children.Add(box);

                    PagesMatrixVariablesList.Items.Add(stack);
                }

                UpdatePagesMatrixPreview();
            }
            catch (Exception ex)
            {
                AddLog($"刷新Pages矩阵失败: {ex.Message}", "error");
            }
        }

        private List<string> ExtractVariables(string pattern)
        {
            var variables = new List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(pattern, @"\{\{(\w+)\}\}");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var varName = match.Groups[1].Value;
                if (!variables.Contains(varName))
                    variables.Add(varName);
            }
            return variables;
        }

        private void UpdatePagesMatrixPreview()
        {
            try
            {
                var group = PagesDeployGroupCombo.SelectedItem as AccountGroup;
                var pattern = PagesProjectNamePattern?.Text ?? "";

                if (group == null || string.IsNullOrWhiteSpace(pattern)) return;

                var variables = ExtractVariables(pattern);
                var varValues = new Dictionary<string, List<string>>();
                foreach (StackPanel panel in PagesMatrixVariablesList.Items)
                {
                    var box = panel.Children[1] as TextBox;
                    var varName = box?.Tag?.ToString();
                    if (varName != null && box != null)
                    {
                        var values = box.Text.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        varValues[varName] = values.Any() ? values : new List<string> { "" };
                    }
                }

                PagesDeployMatrix.Clear();
                GeneratePagesCombinations(new Dictionary<string, string>(), 0, variables, varValues, group, pattern);

                PagesMatrixPreviewPanel.Visibility = PagesDeployMatrix.Any() ? Visibility.Visible : Visibility.Collapsed;
                PagesMatrixCountText.Text = $"共 {PagesDeployMatrix.Count} 个部署任务 ({PagesDeployMatrix.Count(i => i.Selected)} 已选择)";

                AddLog($"Pages部署矩阵已更新: {PagesDeployMatrix.Count}个任务", "debug");
                RefreshPagesMatrixGrid();
            }
            catch (Exception ex)
            {
                AddLog($"更新Pages矩阵预览失败: {ex.Message}", "error");
            }
        }

        private void GeneratePagesCombinations(Dictionary<string, string> current, int index,
            List<string> variables, Dictionary<string, List<string>> varValues,
            AccountGroup group, string pattern)
        {
            if (index >= variables.Count)
            {
                foreach (var account in group.Accounts)
                {
                    if (account == null) continue;

                    var projectName = ReplaceVars(pattern, current);
                    PagesDeployMatrix.Add(new DeployMatrixItem
                    {
                        AccountId = account.AccountId!,
                        AccountName = account.Name ?? (account.AccountId?.Length > 0
                            ? account.AccountId[..System.Math.Min(8, account.AccountId.Length)]
                            : "无账户"),
                        ApiToken = account.ApiToken!,
                        PagesProjectName = projectName,
                        Variables = new Dictionary<string, string>(current),
                        DeployTarget = DeployTarget.Pages,
                        PagesBranch = PagesBranch?.Text ?? "main",
                        PagesDeployType = PagesDeployTypeCombo?.SelectedIndex == 1
                            ? PagesDeployType.PagesFunction
                            : PagesDeployType.DirectUpload,
                        PagesStaticDir = PagesStaticDir?.Text,
                        Code = PagesCode?.Text,
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
                GeneratePagesCombinations(current, index + 1, variables, varValues, group, pattern);
            }
        }

        private void RefreshPagesMatrixGrid()
        {
            try
            {
                PagesMatrixItemsPanel.Children.Clear();

                for (int i = 0; i < PagesDeployMatrix.Count; i++)
                {
                    int idx = i;
                    var item = PagesDeployMatrix[i];

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
                        PagesDeployMatrix[idx].Selected = !PagesDeployMatrix[idx].Selected;
                        RefreshPagesMatrixGrid();
                        PagesMatrixCountText.Text = $"共 {PagesDeployMatrix.Count} 个部署任务 ({PagesDeployMatrix.Count(i => i.Selected)} 已选择)";
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
                        Text = item.PagesProjectName,
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
                    PagesMatrixItemsPanel.Children.Add(border);
                }
            }
            catch (Exception ex)
            {
                AddLog($"刷新Pages矩阵网格失败: {ex.Message}", "error");
            }
        }

        private void SelectAllPagesMatrix_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var item in PagesDeployMatrix) item.Selected = true;
                RefreshPagesMatrixGrid();
                AddLog("已全选所有Pages部署任务", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"全选Pages任务失败: {ex.Message}", "error");
            }
        }

        private void SelectNonePagesMatrix_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var item in PagesDeployMatrix) item.Selected = false;
                RefreshPagesMatrixGrid();
                AddLog("已取消全选Pages任务", "debug");
            }
            catch (Exception ex)
            {
                AddLog($"取消全选Pages任务失败: {ex.Message}", "error");
            }
        }

        private async void StartPagesMatrixDeploy_Click(object sender, RoutedEventArgs e)
        {
            var items = PagesDeployMatrix.Where(i => i.Selected).ToList();
            if (!items.Any()) return;

            var startButton = sender as Button;
            if (startButton != null) startButton.IsEnabled = false;

            IsDeploying = true;
            StatusText = "Pages部署中...";
            AddLog($"开始Pages矩阵部署: {items.Count} 个任务", "info");

            try
            {
                var jobs = items.Select(item => new PagesDeployJob
                {
                    AccountId = item.AccountId,
                    ApiToken = item.ApiToken,
                    ProjectName = item.PagesProjectName,
                    Branch = string.IsNullOrWhiteSpace(item.PagesBranch) ? "main" : item.PagesBranch.Trim(),
                    StaticDir = item.PagesStaticDir,
                    Script = item.Code,
                    DeployType = item.PagesDeployType,
                    EnvironmentVariables = new Dictionary<string, string>()
                }).ToList();

                int completed = 0;
                int success = 0;
                int failed = 0;

                var progress = new Progress<(int index, string status, string? message)>(update =>
                {
                    var item = items[update.index];
                    if (update.status == "success")
                    {
                        success++;
                        AddLog($"✅ {item.PagesProjectName} 部署成功", "success");
                    }
                    else if (update.status == "error")
                    {
                        failed++;
                        AddLog($"❌ {item.PagesProjectName} 失败: {update.message}", "error");
                    }
                    completed++;
                    StatusText = $"Pages部署中... {completed}/{items.Count}";
                });

                await PagesDeployService.DeployBatchAsync(jobs, concurrency: 1, delayMs: 1000, maxRetries: 3, progress);

                AddLog($"Pages矩阵部署完成: 成功 {success} 个, 失败 {failed} 个", success > 0 ? "success" : "warning");
                StatusText = $"Pages部署完成: 成功 {success}, 失败 {failed}";
            }
            catch (Exception ex)
            {
                AddLog($"Pages部署过程异常: {ex.Message}", "error");
                MessageBox.Show($"Pages部署失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsDeploying = false;
                if (startButton != null) startButton.IsEnabled = true;
                UpdateStatus();
            }
        }

        #endregion

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
                var service = new DeployService(ProxyUrl, ProxyKey);
                var job = new DeployJob
                {
                    AccountId = profile.AccountId.Trim(),
                    ApiToken = profile.ApiToken.Trim(),
                    WorkerName = profile.WorkerName.Trim(),
                    Script = profile.Code ?? GetDefaultWorkerCode(),
                    Secrets = profile.Secrets?.ToDictionary(s => s.Key, s => s.Value) ?? new Dictionary<string, string>(),
                    EnvironmentVariables = profile.EnvironmentVariables?.ToDictionary(s => s.Key, s => s.Value) ?? new Dictionary<string, string>(),
                    Routes = new List<Route>(),
                    Subdomain = !string.IsNullOrEmpty(profile.Subdomain)
                };

                var result = await service.DeploySingleAsync(job);
                if (!result.Success)
                {
                    throw new Exception(result.Error);
                }

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
                    Templates = Templates.ToList(),
                    PagesProjects = PagesProjects.ToList()
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

                        PagesProjects.Clear();
                        foreach (var p in data.PagesProjects ?? new List<PagesProject>())
                            PagesProjects.Add(p);
                        
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
                    PagesProjects.Clear();
                    _currentPagesProject = null;
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

    // Worker代码分析器
    public class WorkerCodeAnalyzer
    {
        public class AnalysisResult
        {
            public int TotalLines { get; set; }
            public int CodeLines { get; set; }
            public int CommentLines { get; set; }
            public int EmptyLines { get; set; }
            public List<string> DetectedPatterns { get; set; } = new();
            public List<string> Suggestions { get; set; } = new();
            public string Complexity { get; set; } = "简单";
        }

        public static AnalysisResult Analyze(string code)
        {
            var result = new AnalysisResult();
            if (string.IsNullOrWhiteSpace(code))
                return result;

            var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            result.TotalLines = lines.Length;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    result.EmptyLines++;
                else if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    result.CommentLines++;
                else
                    result.CodeLines++;
            }

            // 检测常用模式
            if (code.Contains("export default"))
                result.DetectedPatterns.Add("ES Module 导出");
            if (code.Contains("async fetch"))
                result.DetectedPatterns.Add("Fetch 事件处理器");
            if (code.Contains("addEventListener"))
                result.DetectedPatterns.Add("事件监听器模式");
            if (code.Contains("new Response"))
                result.DetectedPatterns.Add("HTTP 响应构造");
            if (code.Contains("env."))
                result.DetectedPatterns.Add("环境变量访问");
            if (code.Contains("cors") || code.Contains("CORS"))
                result.DetectedPatterns.Add("CORS 处理");

            // 复杂度评估
            var bracketCount = code.Count(c => c == '{' || c == '}');
            if (bracketCount > 20) result.Complexity = "复杂";
            else if (bracketCount > 10) result.Complexity = "中等";
            else result.Complexity = "简单";

            // 建议
            if (!code.Contains("try") && code.Contains("await"))
                result.Suggestions.Add("建议：异步操作添加 try-catch 错误处理");
            if (!code.Contains("console.log") && result.CodeLines > 20)
                result.Suggestions.Add("提示：可添加日志输出便于调试");
            if (!result.DetectedPatterns.Any(p => p.Contains("CORS")) && code.Contains("fetch"))
                result.Suggestions.Add("建议：跨域请求可能需要 CORS 处理");

            return result;
        }
    }
}