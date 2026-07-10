using System.Collections.Generic;

namespace CFDeployer.Models
{
    public class Profile
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";

        // 部署目标
        public DeployTarget DeployTarget { get; set; } = DeployTarget.Worker;

        // Worker 专用
        public string WorkerName { get; set; } = "";
        public string? Subdomain { get; set; }
        public List<Route> Routes { get; set; } = new();

        // Pages 专用
        public string PagesProjectName { get; set; } = "";
        public string PagesBranch { get; set; } = "main";
        public string? PagesStaticDir { get; set; }
        public PagesDeployType PagesDeployType { get; set; } = PagesDeployType.DirectUpload;

        // 通用
        public List<Secret> Secrets { get; set; } = new();
        public string? Code { get; set; }
        public List<Secret> EnvironmentVariables { get; set; } = new();

        public string DisplayInfo
        {
            get
            {
                var target = DeployTarget == DeployTarget.Pages ? "Pages" : "Worker";
                var mainName = DeployTarget == DeployTarget.Pages
                    ? (string.IsNullOrEmpty(PagesProjectName) ? "未设置" : PagesProjectName)
                    : (string.IsNullOrEmpty(WorkerName) ? "未设置" : WorkerName);
                var account = AccountId?.Length > 8 ? AccountId[..8] + "..." : AccountId ?? "无账户";
                return $"[{target}] {mainName} | {account}";
            }
        }
    }

    public class Secret
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class Route
    {
        public string Pattern { get; set; } = "";
        public string ZoneId { get; set; } = "";
    }
}
