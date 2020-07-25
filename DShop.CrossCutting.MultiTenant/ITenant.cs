namespace DShop.CrossCutting.MultiTenant
{
    public interface ITenant
    {
        string TenantId { get; set; }
    }
}
