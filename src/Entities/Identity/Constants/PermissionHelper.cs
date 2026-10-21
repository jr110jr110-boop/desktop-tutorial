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

        // Warehouse and part traceability module
        public const string Warehouse = "Warehouse";
        public const string Traceability = "Traceability";
        public const string SepidarLink = "SepidarLink";


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

            #region Warehouse_and_traceability_RoleClaims (PMO 112-114, Commercial 202, Engineering 308-309, Engineer 401, Financial 500+, QC 600+, WareHouse 700+, Assembler 800+)
            yield return new RoleClaim()
            {
                Id = 112,
                ClaimType = Type,
                ClaimValue = Warehouse,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 113,
                ClaimType = Type,
                ClaimValue = Traceability,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 114,
                ClaimType = Type,
                ClaimValue = SepidarLink,
                RoleId = RoleConstants.PMOResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 202,
                ClaimType = Type,
                ClaimValue = Warehouse,
                RoleId = RoleConstants.CommercialResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 308,
                ClaimType = Type,
                ClaimValue = Warehouse,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 309,
                ClaimType = Type,
                ClaimValue = Traceability,
                RoleId = RoleConstants.EngineeringResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 401,
                ClaimType = Type,
                ClaimValue = Traceability,
                RoleId = RoleConstants.EngineerResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 500,
                ClaimType = Type,
                ClaimValue = Warehouse,
                RoleId = RoleConstants.FinancialResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 501,
                ClaimType = Type,
                ClaimValue = SepidarLink,
                RoleId = RoleConstants.FinancialResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 600,
                ClaimType = Type,
                ClaimValue = Traceability,
                RoleId = RoleConstants.QCResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 601,
                ClaimType = Type,
                ClaimValue = Warehouse,
                RoleId = RoleConstants.QCResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 700,
                ClaimType = Type,
                ClaimValue = Warehouse,
                RoleId = RoleConstants.WareHouseResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 701,
                ClaimType = Type,
                ClaimValue = Traceability,
                RoleId = RoleConstants.WareHouseResponseRole.Id,
            };
            yield return new RoleClaim()
            {
                Id = 800,
                ClaimType = Type,
                ClaimValue = Traceability,
                RoleId = RoleConstants.AssemblerResponseRole.Id,
            };
            yield return new RoleClaim() { Id = 801, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.AssemblerResponseRole.Id };
            yield return new RoleClaim() { Id = 402, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.EngineerResponseRole.Id };
            yield return new RoleClaim() { Id = 900, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.ConstructionResponseRole.Id };
            yield return new RoleClaim() { Id = 901, ClaimType = Type, ClaimValue = Traceability, RoleId = RoleConstants.ConstructionResponseRole.Id };
            yield return new RoleClaim() { Id = 1000, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.SecurityResponseRole.Id };
            yield return new RoleClaim() { Id = 1100, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.PlanningResponseRole.Id };
            yield return new RoleClaim() { Id = 1101, ClaimType = Type, ClaimValue = Traceability, RoleId = RoleConstants.PlanningResponseRole.Id };
            yield return new RoleClaim() { Id = 1200, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.WarehouseSupervisorResponseRole.Id };
            yield return new RoleClaim() { Id = 1201, ClaimType = Type, ClaimValue = Traceability, RoleId = RoleConstants.WarehouseSupervisorResponseRole.Id };
            yield return new RoleClaim() { Id = 1300, ClaimType = Type, ClaimValue = Contract, RoleId = RoleConstants.CeoResponseRole.Id };
            yield return new RoleClaim() { Id = 1301, ClaimType = Type, ClaimValue = WorkOrder, RoleId = RoleConstants.CeoResponseRole.Id };
            yield return new RoleClaim() { Id = 1302, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.CeoResponseRole.Id };
            yield return new RoleClaim() { Id = 1303, ClaimType = Type, ClaimValue = Traceability, RoleId = RoleConstants.CeoResponseRole.Id };
            yield return new RoleClaim() { Id = 1304, ClaimType = Type, ClaimValue = SepidarLink, RoleId = RoleConstants.CeoResponseRole.Id };
            yield return new RoleClaim() { Id = 1400, ClaimType = Type, ClaimValue = Traceability, RoleId = RoleConstants.QAResponseRole.Id };
            yield return new RoleClaim() { Id = 1401, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.QAResponseRole.Id };
            yield return new RoleClaim() { Id = 1500, ClaimType = Type, ClaimValue = Contract, RoleId = RoleConstants.ProjectManagerResponseRole.Id };
            yield return new RoleClaim() { Id = 1501, ClaimType = Type, ClaimValue = WorkOrder, RoleId = RoleConstants.ProjectManagerResponseRole.Id };
            yield return new RoleClaim() { Id = 1502, ClaimType = Type, ClaimValue = Traceability, RoleId = RoleConstants.ProjectManagerResponseRole.Id };
            yield return new RoleClaim() { Id = 1600, ClaimType = Type, ClaimValue = Contract, RoleId = RoleConstants.DccResponseRole.Id };
            yield return new RoleClaim() { Id = 1601, ClaimType = Type, ClaimValue = WorkOrder, RoleId = RoleConstants.DccResponseRole.Id };
            yield return new RoleClaim() { Id = 1602, ClaimType = Type, ClaimValue = Traceability, RoleId = RoleConstants.DccResponseRole.Id };
            yield return new RoleClaim() { Id = 1700, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.FinanceManagerResponseRole.Id };
            yield return new RoleClaim() { Id = 1701, ClaimType = Type, ClaimValue = SepidarLink, RoleId = RoleConstants.FinanceManagerResponseRole.Id };
            yield return new RoleClaim() { Id = 1800, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.PayrollResponseRole.Id };
            yield return new RoleClaim() { Id = 1900, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.TreasuryResponseRole.Id };
            yield return new RoleClaim() { Id = 2800, ClaimType = Type, ClaimValue = Warehouse, RoleId = RoleConstants.AuditorResponseRole.Id };
            #endregion
        }
    }
}
