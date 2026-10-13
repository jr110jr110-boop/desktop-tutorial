using System.Collections.Generic;

namespace Entities.Identity.Constants
{
    public static class PermissionHelper
    {
        public const string Type = "permission";

        public const string Account = "Account";

        public const string Tender = "Tender";

        public const string Contract = "Contract";

        public const string WorkOrder = "WorkOrder";
        public const string Assembly = "Assembly";
        public const string Bom = "Bom";
        public const string Process = "Process";

        public const string ContractEquipment = "ContractEquipment";

        public const string Equipment = "Equipment";
        public const string EquipmentAssembly = "EquipmentAssembly";
        public const string EquipmentBom = "EquipmentBom";
        public const string EquipmentProcess = "EquipmentProcess";


        public static IEnumerable<RoleClaim> GetRoleClaims()
        {
            #region PMO_RoleClaims id starts from 100
            yield return new RoleClaim()
            {
                Id = 100,
                ClaimType = Type,
                ClaimValue = Account,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 101,
                ClaimType = Type,
                ClaimValue = Tender,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 102,
                ClaimType = Type,
                ClaimValue = Contract,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 103,
                ClaimType = Type,
                ClaimValue = Equipment,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 104,
                ClaimType = Type,
                ClaimValue = ContractEquipment,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 105,
                ClaimType = Type,
                ClaimValue = EquipmentAssembly,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 106,
                ClaimType = Type,
                ClaimValue = EquipmentBom,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 107,
                ClaimType = Type,
                ClaimValue = EquipmentProcess,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 108,
                ClaimType = Type,
                ClaimValue = Assembly,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 109,
                ClaimType = Type,
                ClaimValue = Bom,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 110,
                ClaimType = Type,
                ClaimValue = WorkOrder,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 111,
                ClaimType = Type,
                ClaimValue = Process,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            #endregion

            #region Commercial_RoleClaims id starts from 200
            yield return new RoleClaim()
            {
                Id = 200,
                ClaimType = Type,
                ClaimValue = Tender,
                RoleId = RoleConstants.CommercialResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 201,
                ClaimType = Type,
                ClaimValue = Contract,
                RoleId = RoleConstants.CommercialResponseRole.Id,
            };
            #endregion

            #region Engineering_RoleClaims id starts from 300
            yield return new RoleClaim()
            {
                Id = 300,
                ClaimType = Type,
                ClaimValue = Equipment,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 301,
                ClaimType = Type,
                ClaimValue = EquipmentAssembly,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 302,
                ClaimType = Type,
                ClaimValue = EquipmentBom,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 303,
                ClaimType = Type,
                ClaimValue = EquipmentProcess,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 304,
                ClaimType = Type,
                ClaimValue = WorkOrder,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 305,
                ClaimType = Type,
                ClaimValue = Assembly,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 306,
                ClaimType = Type,
                ClaimValue = Bom,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 307,
                ClaimType = Type,
                ClaimValue = Process,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            #endregion

            #region Engineer_RoleClaims id starts from 400
            yield return new RoleClaim()
            {
                Id = 400,
                ClaimType = Type,
                ClaimValue = Process,
                RoleId = RoleConstants.EngineerResponseRole.Id,
            };
            #endregion
        }
    }
}
