using System.Collections.Generic;

namespace CFDeployer.Models
{
    public class AppData
    {
        public string ProxyUrl { get; set; } = "";
        public string ProxyKey { get; set; } = "";
        public List<Profile> Profiles { get; set; } = new();
        public List<AccountGroup> AccountGroups { get; set; } = new();
        public List<WorkerTemplate> Templates { get; set; } = new();
        public List<PagesProject> PagesProjects { get; set; } = new();
    }
}
