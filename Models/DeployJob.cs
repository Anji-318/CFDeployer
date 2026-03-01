using System.Collections.Generic;
using System.ComponentModel;

namespace CFDeployer.Models
{
    public class DeployJob
    {
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string WorkerName { get; set; } = "";
        public string Script { get; set; } = "";
        public Dictionary<string, string> Secrets { get; set; } = new();
        public List<Route> Routes { get; set; } = new();
        public bool Subdomain { get; set; }
        
        // 环境变量（明文存储）
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    }
    
    public class DeployMatrixItem : INotifyPropertyChanged
    {
        private bool _selected = true;
        private string _status = "pending";
        private string _message = "";

        public string AccountId { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string WorkerName { get; set; } = "";
        public Dictionary<string, string> Variables { get; set; } = new();
        
        // 环境变量（用于矩阵部署时传递自定义环境变量）
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        public bool Selected
        {
            get => _selected;
            set { _selected = value; OnPropertyChanged(nameof(Selected)); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(nameof(Message)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
