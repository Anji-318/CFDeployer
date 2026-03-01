using System.Collections.Generic;

namespace CFDeployer.Models
{
    public class Profile
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string WorkerName { get; set; } = "";
        public string? Subdomain { get; set; }
        public List<Secret> Secrets { get; set; } = new();
        public List<Route> Routes { get; set; } = new();
        public string? Code { get; set; }
        
        // 环境变量（明文存储，适用于非敏感配置）
        public List<Secret> EnvironmentVariables { get; set; } = new();
        
        public string DisplayInfo => $"{WorkerName ?? "未设置"} | {(AccountId?.Length > 8 ? AccountId[..8] + "..." : AccountId ?? "无账户")}";
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
