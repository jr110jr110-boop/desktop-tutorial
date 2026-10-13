using ViewModels.NasbNiro.ContractEquipmentDtos;

namespace Services.NasbNiro.Extensions
{
    public static class ContractEquipmentExtensions
    {
        public static GetContractEquipmentDto AsGetContractEquipmentDto(this ContractEquipmentDtoKey contractEquipmentDtoKey)
        {
            return new GetContractEquipmentDto()
            {
                ContractId = contractEquipmentDtoKey.ContractId,
                EquipmentId = contractEquipmentDtoKey.EquipmentId
            };
        }
    }
}
