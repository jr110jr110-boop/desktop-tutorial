using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Entities.Model.NasbNiro.Contracts;
using Newtonsoft.Json;

namespace ViewModels.NasbNiro.ContractDtos
{
    public class GetContractDto
    {
        public GetContractDto()
        {
        }

        public GetContractDto(int id, bool forProvider) : this()
        {
            Id = id;
            ForProvider = forProvider;
        }

        [Required]
        public int Id { get; set; }

        [JsonIgnore]
        public bool ForProvider { get; set; }

        public void IsForProvider()
        {
            ForProvider = true;
        }

        public void IsNotForProvider()
        {
            ForProvider = false;
        }

        public static implicit operator GetContractDto(ContractEntity entity)
        {
            return new GetContractDto(entity.Id , entity.ForProvider);
        }
    }
}
