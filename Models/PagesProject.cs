using System.Collections.Generic;
using System.Linq;

namespace CFDeployer.Models
{
    /// <summary>
    /// Pages 部署类型
    /// </summary>
    public enum PagesDeployType
    {
        /// <summary>直接上传静态资源</summary>
        DirectUpload,

        /// <summary>Pages Function（含 _worker.js）</summary>
        PagesFunction
    }

    /// <summary>
    /// Pages 项目配置
    /// </summary>
    public class PagesProject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Branch { get; set; } = "main";
        public string? StaticDir { get; set; }
        public string? Code { get; set; }
        public PagesDeployType DeployType { get; set; } = PagesDeployType.DirectUpload;
        public List<Secret> EnvironmentVariables { get; set; } = new();

        public string DisplayInfo =>
            $"{ProjectName} | {(AccountId?.Length > 0 ? AccountId[..System.Math.Min(8, AccountId.Length)] + "..." : "无账户")} | {GetDeployTypeText()}";

        private string GetDeployTypeText() => DeployType switch
        {
            PagesDeployType.PagesFunction => "Pages Function",
            _ => "Direct Upload"
        };
    }
}
