using System;
using System.Collections.Generic;
using System.Text;
using Entities.Model.NasbNiro.Providers;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class GetProviderDto
    {
        public int Id { get; set; }
        public int? CategoryId { get; set; }

        public bool HasCategory() => CategoryId.HasValue;

        public static implicit operator GetProviderDto(Provider provider)
        {
            return new GetProviderDto()
            {
                Id = provider.Id,
            };
        }

        public static implicit operator GetProviderDto(ProviderContact providerContact)
        {
            return new GetProviderDto()
            {
                Id = providerContact.ProviderId,
            };
        }

        public static implicit operator GetProviderDto(ProviderAccount providerAccount)
        {
            return new GetProviderDto()
            {
                Id = providerAccount.ProviderId,
            };
        }
    }
}
