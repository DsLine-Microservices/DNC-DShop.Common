namespace DShop.CrossCutting.MultiTenant
{
    public class Tenant : ITenant
    {
        public string TenantId { get; set; }
    }
}
