using System.Collections.Generic;
using System.ComponentModel;

namespace CFDeployer.Models
{
    /// <summary>
    /// 单个部署任务
    /// </summary>
    public class DeployJob
    {
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string WorkerName { get; set; } = "";
        public string Script { get; set; } = "";
        public Dictionary<string, string> Secrets { get; set; } = new();
        public List<Route> Routes { get; set; } = new();
        public bool Subdomain { get; set; }
    }
    
    /// <summary>
    /// 部署矩阵项（支持数据绑定）
    /// </summary>
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