using System.Collections.Generic;
using System.ComponentModel;

namespace CFDeployer.Models
{
    public class DeployJob
    {
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";

        // 部署目标
        public DeployTarget DeployTarget { get; set; } = DeployTarget.Worker;

        // Worker 专用
        public string WorkerName { get; set; } = "";
        public string Script { get; set; } = "";
        public Dictionary<string, string> Secrets { get; set; } = new();
        public List<Route> Routes { get; set; } = new();
        public bool Subdomain { get; set; }

        // Pages 专用
        public string PagesProjectName { get; set; } = "";
        public string? PagesStaticDir { get; set; }
        public string? PagesBranch { get; set; }
        public PagesDeployType PagesDeployType { get; set; } = PagesDeployType.DirectUpload;

        // 通用
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
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        // 部署目标（Worker / Pages）
        public DeployTarget DeployTarget { get; set; } = DeployTarget.Worker;

        // Pages 专用
        public string PagesProjectName { get; set; } = "";
        public string? PagesBranch { get; set; }
        public PagesDeployType PagesDeployType { get; set; } = PagesDeployType.DirectUpload;
        public string? PagesStaticDir { get; set; }
        public string? Code { get; set; }

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
