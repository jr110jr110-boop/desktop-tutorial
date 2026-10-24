using System.Collections.Generic;
using System.Linq;

namespace Entities.Identity.Constants
{
    public static class RoleConstants
    {
        public const string PMO = nameof(PMO);
        public static readonly ResponseRole PMOResponseRole = new ResponseRole()
        {
            Id = 1,
            Name = PMO.ToUpper(),
            Description = "مدیریت"
        };

        public const string Commercial = nameof(Commercial);
        public static readonly ResponseRole CommercialResponseRole = new ResponseRole()
        {
            Id = 2,
            Name = Commercial.ToUpper(),
            Description = "واحد بازرگانی"
        };

        public const string Financial = nameof(Financial);
        public static readonly ResponseRole FinancialResponseRole = new ResponseRole()
        {
            Id = 3,
            Name = Financial.ToUpper(),
            Description = "واحد مالی"
        };

        public const string Engineering = nameof(Engineering);
        public static readonly ResponseRole EngineeringResponseRole = new ResponseRole()
        {
            Id = 4,
            Name = Engineering.ToUpper(),
            Description = "واحد مهندسی"
        };

        public const string QC = nameof(QC);
        public static readonly ResponseRole QCResponseRole = new ResponseRole()
        {
            Id = 5,
            Name = QC.ToUpper(),
            Description = "کنترل کیفیت"
        };

        public const string WareHouse = nameof(WareHouse);
        public static readonly ResponseRole WareHouseResponseRole = new ResponseRole()
        {
            Id = 6,
            Name = WareHouse.ToUpper(),
            Description = "انبار"
        };

        public const string Assembler = nameof(Assembler);
        public static readonly ResponseRole AssemblerResponseRole = new ResponseRole()
        {
            Id = 7,
            Name = Assembler.ToUpper(),
            Description = "مونتاژ"
        };

        public const string Engineer = nameof(Engineer);
        public static readonly ResponseRole EngineerResponseRole = new ResponseRole()
        {
            Id = 8,
            Name = Engineer.ToUpper(),
            Description = "مهندس"
        };

        public const string Construction = nameof(Construction);
        public static readonly ResponseRole ConstructionResponseRole = new ResponseRole()
        {
            Id = 9,
            Name = Construction.ToUpper(),
            Description = "تولید"
        };

        public const string Security = nameof(Security);
        public static readonly ResponseRole SecurityResponseRole = new ResponseRole()
        {
            Id = 10,
            Name = Security.ToUpper(),
            Description = "انتظامات (کنترل خروج کالا)"
        };

        // Decision 1404-07-24: approvals of adjustments and stock counts are by the warehouse supervisor and the
        // planning and project control manager (segregation of duties from the storekeeper); planning gives item codes.
        public const string Planning = nameof(Planning);
        public static readonly ResponseRole PlanningResponseRole = new ResponseRole()
        {
            Id = 11,
            Name = Planning.ToUpper(),
            Description = "برنامه‌ریزی و کنترل پروژه"
        };

        public const string WarehouseSupervisor = "WHSupervisor";
        public static readonly ResponseRole WarehouseSupervisorResponseRole = new ResponseRole()
        {
            Id = 12,
            Name = WarehouseSupervisor.ToUpper(),
            Description = "سرپرست انبار"
        };

        public const string Ceo = "CEO";
        public static readonly ResponseRole CeoResponseRole = new ResponseRole()
        {
            Id = 13,
            Name = Ceo,
            Description = "مدیرعامل"
        };

        // Quality module: document control, audits, CAPA administration and management review (★ provisional decision)
        public const string QA = "QA";
        public static readonly ResponseRole QAResponseRole = new ResponseRole()
        {
            Id = 14,
            Name = QA,
            Description = "تضمین کیفیت"
        };

        // Project control module: project manager (appointed by the CEO charter) and project documents control (DCC) (★ provisional decision)
        public const string ProjectManager = "PROJECTMANAGER";
        public static readonly ResponseRole ProjectManagerResponseRole = new ResponseRole()
        {
            Id = 15,
            Name = ProjectManager,
            Description = "مدیر پروژه"
        };

        public const string Dcc = "DCC";
        public static readonly ResponseRole DccResponseRole = new ResponseRole()
        {
            Id = 16,
            Name = Dcc,
            Description = "کنترل مدارک پروژه"
        };

        // Finance module (full accounting inside PMIS) (★ provisional decision)
        public const string FinanceManager = "FINANCEMANAGER";
        public static readonly ResponseRole FinanceManagerResponseRole = new ResponseRole()
        {
            Id = 17,
            Name = FinanceManager,
            Description = "مدیر مالی"
        };

        public const string Payroll = "PAYROLL";
        public static readonly ResponseRole PayrollResponseRole = new ResponseRole()
        {
            Id = 18,
            Name = Payroll,
            Description = "حقوق و دستمزد"
        };

        public const string Treasury = "TREASURY";
        public static readonly ResponseRole TreasuryResponseRole = new ResponseRole()
        {
            Id = 19,
            Name = Treasury,
            Description = "خزانه‌داری"
        };

        // Finance release F3: the auditor (حسابرس) reads all finance data, including the audit trail, and writes nothing.
        public const string Auditor = "AUDITOR";
        public static readonly ResponseRole AuditorResponseRole = new ResponseRole()
        {
            Id = 28,
            Name = Auditor,
            Description = "حسابرس"
        };

        // Office automation release O1: the secretariat registers incoming letters and dispatches outgoing ones; HR keeps the organisation chart, attendance and leave.
        public const string Secretariat = "SECRETARIAT";
        public static readonly ResponseRole SecretariatResponseRole = new ResponseRole()
        {
            Id = 26,
            Name = Secretariat,
            Description = "دبیرخانه"
        };

        public const string Hr = "HR";
        public static readonly ResponseRole HrResponseRole = new ResponseRole()
        {
            Id = 27,
            Name = Hr,
            Description = "اداری و منابع انسانی"
        };

        public static IEnumerable<ResponseRole> GetResponseRoles()
        {
            yield return PMOResponseRole;
            yield return CommercialResponseRole;
            yield return FinancialResponseRole;
            yield return EngineeringResponseRole;
            yield return QCResponseRole;
            yield return WareHouseResponseRole;
            yield return AssemblerResponseRole;
            yield return EngineerResponseRole;
            yield return ConstructionResponseRole;
            yield return SecurityResponseRole;
            yield return PlanningResponseRole;
            yield return WarehouseSupervisorResponseRole;
            yield return CeoResponseRole;
            yield return QAResponseRole;
            yield return ProjectManagerResponseRole;
            yield return DccResponseRole;
            yield return FinanceManagerResponseRole;
            yield return PayrollResponseRole;
            yield return TreasuryResponseRole;
            yield return AuditorResponseRole;
            yield return SecretariatResponseRole;
            yield return HrResponseRole;
        }

        // IdentityRole creates a new random ConcurrencyStamp for every instance, so the seed data (HasData)
        // changed on every model build. EF Core 9+ treats that as a pending model change. These are the
        // values stored by the latest migration snapshot, so the seed data stays identical to the database.
        private static readonly Dictionary<int, string> SeedConcurrencyStamps = new Dictionary<int, string>
        {
            { 1, "4d014b68-b953-4f5f-a271-77b52363f81b" },
            { 2, "1f74cf68-1e1b-4be1-800b-226a2e410cb7" },
            { 3, "3812206d-0b96-4844-9036-e7000eeeb54c" },
            { 4, "192ae977-ee8b-4a55-8e12-cf8412b65b05" },
            { 5, "6be12738-de53-4382-9bda-a213bb5bba4c" },
            { 6, "67529016-046a-4381-9924-905ce64fa99e" },
            { 7, "a6f4553a-4bd7-4d6e-96ba-65822f64c071" },
            { 8, "cb798620-c4a2-4db2-b2c2-0d6034094e1c" },
        };

        public static IEnumerable<Role> GetRoles()
        {
            return GetResponseRoles()
                .Select(rr => new Role()
                {
                    Id = rr.Id,
                    Name = rr.Name,
                    NormalizedName = rr.Name.ToUpper(),
                    Description = rr.Description,
                    ConcurrencyStamp = SeedConcurrencyStamps.TryGetValue(rr.Id, out var stamp)
                        ? stamp
                        : rr.Id.ToString("D8") + "-0000-0000-0000-000000000000",
                });
        }

        public static (IEnumerable<string> ValidRoles, IEnumerable<string> InvalidRoles)
            ValidateRoles(IEnumerable<string> roles)
        {
            roles = roles.Select(role => role.ToUpper());

            IEnumerable<string> ValidRoles = roles.Intersect(GetRoles().Select(role => role.NormalizedName));
            IEnumerable<string> InvalidRoles = roles.Except(GetRoles().Select(role => role.NormalizedName));

            return (ValidRoles, InvalidRoles);
        }
    }
}
