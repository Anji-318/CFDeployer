using System.Collections.Generic;
using System.Linq;

namespace CFDeployer.Models
{
    public class WorkerTemplate
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        // 部署目标
        public DeployTarget DeployTarget { get; set; } = DeployTarget.Worker;

        // Worker 专用
        public string WorkerNamePattern { get; set; } = "";

        // Pages 专用
        public string PagesProjectNamePattern { get; set; } = "";
        public string PagesBranch { get; set; } = "main";
        public PagesDeployType PagesDeployType { get; set; } = PagesDeployType.DirectUpload;

        // 通用
        public List<string> Variables { get; set; } = new();
        public List<Secret> Secrets { get; set; } = new();
        public List<Secret> EnvironmentVariables { get; set; } = new();
        public string? Code { get; set; }

        public string VariablesDisplay => Variables.Any()
            ? string.Join(", ", Variables)
            : "无变量";
    }
}
