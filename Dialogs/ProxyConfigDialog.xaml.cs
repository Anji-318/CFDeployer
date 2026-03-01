using System.Windows;
using System.Windows.Media;

namespace CFDeployer.Dialogs
{
    public partial class ProxyConfigDialog : Window
    {
        public string ProxyUrl { get; set; } = "";
        public string ProxyKey { get; set; } = "";

        public ProxyConfigDialog(string currentUrl, string currentKey)
        {
            InitializeComponent();
            
            // 关键：同步主窗口的主题资源
            SyncThemeFromMainWindow();
            
            ProxyUrlTextBox.Text = currentUrl;
            ProxyKeyBox.Password = currentKey;
            ProxyKeyBoxVisible.Text = currentKey;
        }

        // 新增：从主窗口同步主题
        private void SyncThemeFromMainWindow()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                // 复制主窗口的关键资源到对话框
                var keysToSync = new[] { "BgDark", "BgCard", "BgInput", "TextPrimary", "TextSecondary", "Border" };
                
                foreach (var key in keysToSync)
                {
                    if (mainWindow.Resources.Contains(key))
                    {
                        this.Resources[key] = mainWindow.Resources[key];
                    }
                }
                
                // 同步窗口背景（可选，让对话框更统一）
                this.Background = mainWindow.Resources["BgDark"] as Brush;
            }
        }

        private void ToggleProxyKeyVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (ProxyKeyBox.Visibility == Visibility.Visible)
            {
                ProxyKeyBox.Visibility = Visibility.Collapsed;
                ProxyKeyBoxVisible.Visibility = Visibility.Visible;
                ProxyKeyBoxVisible.Text = ProxyKeyBox.Password;
                ToggleProxyKeyBtn.Content = "🙈";
            }
            else
            {
                ProxyKeyBox.Visibility = Visibility.Visible;
                ProxyKeyBoxVisible.Visibility = Visibility.Collapsed;
                ProxyKeyBox.Password = ProxyKeyBoxVisible.Text;
                ToggleProxyKeyBtn.Content = "👁️";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ProxyUrl = ProxyUrlTextBox.Text.Trim();
            ProxyKey = ProxyKeyBox.Visibility == Visibility.Visible 
                ? ProxyKeyBox.Password 
                : ProxyKeyBoxVisible.Text;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}