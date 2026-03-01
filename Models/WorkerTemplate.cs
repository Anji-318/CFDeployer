using System.Collections.Generic;

namespace CFDeployer.Models
{
    public class WorkerTemplate
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string WorkerNamePattern { get; set; } = "";
        public List<string> Variables { get; set; } = new();
        public string Code { get; set; } = "";
        public List<SecretTemplate> Secrets { get; set; } = new();
        
        // 环境变量（复用 SecretTemplate 结构）
        public List<SecretTemplate> EnvironmentVariables { get; set; } = new();
        
        public string VariablesDisplay => string.Join(", ", Variables);
    }
    
    public class SecretTemplate
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
