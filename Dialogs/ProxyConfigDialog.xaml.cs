using System.Windows;

namespace CFDeployer.Dialogs
{
    public partial class ProxyConfigDialog : Window
    {
        public string ProxyUrl { get; set; } = "";
        public string ProxyKey { get; set; } = "";

        public ProxyConfigDialog(string currentUrl, string currentKey)
        {
            InitializeComponent();
            ProxyUrlTextBox.Text = currentUrl;
            ProxyKeyBox.Password = currentKey;
            ProxyKeyBoxVisible.Text = currentKey;
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