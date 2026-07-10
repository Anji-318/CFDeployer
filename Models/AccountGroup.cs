using System.Collections.Generic;
using System.Linq;

namespace CFDeployer.Models
{
    public class AccountGroup
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<Account> Accounts { get; set; } = new();
        
        public string AccountsDisplay => 
            Accounts.Any() 
                ? string.Join(", ", Accounts.Select(a => 
                    a.Name ?? (a.AccountId?.Length > 0 
                        ? a.AccountId[..System.Math.Min(8, a.AccountId.Length)] 
                        : "无ID")))
                : "无账户";
    }
    
    public class Account
    {
        public string Name { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
    }
}
