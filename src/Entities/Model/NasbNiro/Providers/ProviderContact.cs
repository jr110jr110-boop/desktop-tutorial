namespace Entities.Model.NasbNiro.Providers
{
    public class ProviderContact : BaseEntity
    {
        public string Name { get; set; }
        public string Data { get; set; }
        public ProviderContactType Type { get; set; }
        public string Description { get; set; }

        public virtual Provider Provider { get; set; }
        public int ProviderId { get; set; }
    }
}