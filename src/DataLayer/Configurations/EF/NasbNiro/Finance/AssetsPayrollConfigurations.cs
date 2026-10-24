using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Configurations.EF.NasbNiro.Finance
{
    // Release F2: Sepidar outbox, fixed assets, payroll and insurance. All tables start with "Fn".
    // Amounts in rial are decimal(18,0), days and hours decimal(9,2), rates and percentages decimal(18,6).
    public static class AssetsPayrollModelBuilderExtensions
    {
        public static void AddAssetsPayrollConfigurations(this ModelBuilder builder)
        {
            // ------------------------------------------------------------------ outbox
            builder.Entity<FnOutboxPackage>(b =>
            {
                b.ToTable("FnOutboxPackages");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.ContentHash).HasMaxLength(64);
                b.Property(x => x.PayloadJson);
                b.Property(x => x.ExternalReference).HasMaxLength(200);
                b.Property(x => x.Error).HasMaxLength(2000);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.Kind, x.Year, x.Month, x.Season, x.WorkshopId });
                b.HasIndex(x => x.Status);
            });

            // ------------------------------------------------------------------ fixed assets
            builder.Entity<FnAssetClass>(b =>
            {
                b.ToTable("FnAssetClasses");
                b.Property(x => x.Code).HasMaxLength(20).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.AssetAccountCode).HasMaxLength(10).IsRequired();
                b.Property(x => x.AccumulatedAccountCode).HasMaxLength(10);
                b.Property(x => x.RatePercent).HasPrecision(18, 6);
                b.Property(x => x.UsefulLifeYears).HasPrecision(18, 6);
                b.Property(x => x.IdleRatePercent).HasPrecision(18, 6);
                b.Property(x => x.SalvagePercent).HasPrecision(18, 6);
                b.Property(x => x.Source).HasMaxLength(500);
                b.Property(x => x.Jurisdiction).HasMaxLength(10);
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<FnAsset>(b =>
            {
                b.ToTable("FnAssets");
                b.Property(x => x.Code).HasMaxLength(40).IsRequired();
                b.Property(x => x.Title).HasMaxLength(300).IsRequired();
                b.Property(x => x.Serial).HasMaxLength(100);
                b.Property(x => x.Maker).HasMaxLength(200);
                b.Property(x => x.Location).HasMaxLength(200);
                b.Property(x => x.CustodianTitle).HasMaxLength(200);
                b.Property(x => x.SourceNumber).HasMaxLength(50);
                b.Property(x => x.OffsetAccountCode).HasMaxLength(10);
                b.Property(x => x.Cost).HasPrecision(18, 0);
                b.Property(x => x.SalvageValue).HasPrecision(18, 0);
                b.Property(x => x.OpeningAccumulated).HasPrecision(18, 0);
                b.Property(x => x.AccumulatedDepreciation).HasPrecision(18, 0);
                b.Property(x => x.YearStartAccumulated).HasPrecision(18, 0);
                b.HasIndex(x => x.Code).IsUnique();
                b.HasIndex(x => x.Status);
                b.HasIndex(x => new { x.Source, x.SourceId });
                b.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnAssetHistory>(b =>
            {
                b.ToTable("FnAssetHistory");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.AssetId);
                b.HasOne(x => x.Asset).WithMany(x => x.History).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnDepreciationRun>(b =>
            {
                b.ToTable("FnDepreciationRuns");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.TotalAmount).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.PeriodId).IsUnique();
                b.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnDepreciationLine>(b =>
            {
                b.ToTable("FnDepreciationLines");
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.Property(x => x.BookValueBefore).HasPrecision(18, 0);
                b.Property(x => x.AccumulatedAfter).HasPrecision(18, 0);
                b.Property(x => x.YearStartAccumulatedAfter).HasPrecision(18, 0);
                b.HasIndex(x => new { x.RunId, x.AssetId }).IsUnique();
                b.HasIndex(x => x.AssetId);
                b.HasOne(x => x.Run).WithMany(x => x.Lines).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnAssetDisposal>(b =>
            {
                b.ToTable("FnAssetDisposals");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.ProceedsAmount).HasPrecision(18, 0);
                b.Property(x => x.VatPercent).HasPrecision(9, 4);
                b.Property(x => x.VatAmount).HasPrecision(18, 0);
                b.Property(x => x.CatchUpDepreciation).HasPrecision(18, 0);
                b.Property(x => x.AccumulatedAtDisposal).HasPrecision(18, 0);
                b.Property(x => x.BookValue).HasPrecision(18, 0);
                b.Property(x => x.GainLoss).HasPrecision(18, 0);
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => x.AssetId);
                b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------------------------------------------------------ payroll masters
            builder.Entity<FnInsuranceWorkshop>(b =>
            {
                b.ToTable("FnInsuranceWorkshops");
                b.Property(x => x.Code).HasMaxLength(20).IsRequired();
                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.EmployerName).HasMaxLength(200);
                b.Property(x => x.Address).HasMaxLength(500);
                b.Property(x => x.Branch).HasMaxLength(100);
                b.Property(x => x.ContractRow).HasMaxLength(50);
                b.Property(x => x.RateSet).HasMaxLength(100);
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<FnEmployee>(b =>
            {
                b.ToTable("FnEmployees");
                b.Property(x => x.PersonnelCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
                b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
                b.Property(x => x.FatherName).HasMaxLength(100);
                b.Property(x => x.NationalCode).HasMaxLength(10).IsRequired();
                b.Property(x => x.BirthPlace).HasMaxLength(100);
                b.Property(x => x.InsuranceNumber).HasMaxLength(20);
                b.Property(x => x.InsuranceJobCode).HasMaxLength(20);
                b.Property(x => x.JobTitle).HasMaxLength(200);
                b.Property(x => x.Unit).HasMaxLength(200);
                b.Property(x => x.BankIban).HasMaxLength(34);
                b.Property(x => x.DailyWage).HasPrecision(18, 0);
                b.Property(x => x.ExemptionFlags).HasMaxLength(200);
                b.HasIndex(x => x.PersonnelCode).IsUnique();
                b.HasIndex(x => x.NationalCode).IsUnique();
                b.HasOne(x => x.Workshop).WithMany().HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnEmployeeLoan>(b =>
            {
                b.ToTable("FnEmployeeLoans");
                b.Property(x => x.Title).HasMaxLength(200);
                b.Property(x => x.Principal).HasPrecision(18, 0);
                b.Property(x => x.InstallmentAmount).HasPrecision(18, 0);
                b.Property(x => x.Balance).HasPrecision(18, 0);
                b.HasIndex(x => x.EmployeeId);
                b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnPayrollItem>(b =>
            {
                b.ToTable("FnPayrollItems");
                b.Property(x => x.Code).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.RateKey).HasMaxLength(60);
                b.Property(x => x.Variant).HasMaxLength(30);
                b.Property(x => x.ItemCodes).HasMaxLength(300);
                b.Property(x => x.Formula).HasMaxLength(500);
                b.Property(x => x.Factor).HasPrecision(18, 6);
                b.Property(x => x.Factor2).HasPrecision(18, 6);
                b.Property(x => x.Condition).HasMaxLength(30);
                b.Property(x => x.AccountRole).HasMaxLength(50);
                b.Property(x => x.Jurisdiction).HasMaxLength(10);
                b.Property(x => x.Source).HasMaxLength(500);
                b.HasIndex(x => x.Code).IsUnique();
            });
            builder.Entity<FnPayrollRateTable>(b =>
            {
                b.ToTable("FnPayrollRateTables");
                b.Property(x => x.Key).HasMaxLength(60).IsRequired();
                b.Property(x => x.Value).HasPrecision(18, 6);
                b.Property(x => x.Source).HasMaxLength(500);
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => new { x.Year, x.Key }).IsUnique();
            });
            builder.Entity<FnTaxBracket>(b =>
            {
                b.ToTable("FnTaxBrackets");
                b.Property(x => x.FromAmount).HasPrecision(18, 0);
                b.Property(x => x.ToAmount).HasPrecision(18, 0);
                b.Property(x => x.RatePercent).HasPrecision(18, 6);
                b.HasIndex(x => new { x.Year, x.FromAmount }).IsUnique();
            });

            // ------------------------------------------------------------------ attendance and leave
            builder.Entity<FnAttendanceSheet>(b =>
            {
                b.ToTable("FnAttendanceSheets");
                b.Property(x => x.DaysWorked).HasPrecision(9, 2);
                b.Property(x => x.OvertimeHours).HasPrecision(9, 2);
                b.Property(x => x.NightHours).HasPrecision(9, 2);
                b.Property(x => x.HolidayHours).HasPrecision(9, 2);
                b.Property(x => x.PaidLeaveDays).HasPrecision(9, 2);
                b.Property(x => x.UnpaidLeaveDays).HasPrecision(9, 2);
                b.Property(x => x.AbsenceDays).HasPrecision(9, 2);
                b.Property(x => x.MissionDays).HasPrecision(9, 2);
                b.Property(x => x.InputsJson).HasMaxLength(2000);
                b.HasIndex(x => new { x.EmployeeId, x.Year, x.Month }).IsUnique();
                b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnAttendanceWorkOrder>(b =>
            {
                b.ToTable("FnAttendanceWorkOrders");
                b.Property(x => x.Hours).HasPrecision(9, 2);
                b.HasIndex(x => new { x.SheetId, x.WorkOrderId }).IsUnique();
                b.HasOne(x => x.Sheet).WithMany(x => x.WorkOrders).HasForeignKey(x => x.SheetId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnLeaveBalance>(b =>
            {
                b.ToTable("FnLeaveBalances");
                b.Property(x => x.CarriedForward).HasPrecision(9, 2);
                b.Property(x => x.Entitled).HasPrecision(9, 2);
                b.Property(x => x.Used).HasPrecision(9, 2);
                b.HasIndex(x => new { x.EmployeeId, x.Year }).IsUnique();
            });

            // ------------------------------------------------------------------ payroll runs
            builder.Entity<FnPayrollRun>(b =>
            {
                b.ToTable("FnPayrollRuns");
                b.Property(x => x.Number).HasMaxLength(50).IsRequired();
                b.Property(x => x.Note).HasMaxLength(1000);
                b.Property(x => x.TotalGross).HasPrecision(18, 0);
                b.Property(x => x.TotalInsurable).HasPrecision(18, 0);
                b.Property(x => x.TotalTaxable).HasPrecision(18, 0);
                b.Property(x => x.TotalEmployeeInsurance).HasPrecision(18, 0);
                b.Property(x => x.TotalEmployerInsurance).HasPrecision(18, 0);
                b.Property(x => x.TotalTax).HasPrecision(18, 0);
                b.Property(x => x.TotalOtherDeductions).HasPrecision(18, 0);
                b.Property(x => x.TotalNet).HasPrecision(18, 0);
                b.Property(x => x.TotalProvisions).HasPrecision(18, 0);
                b.HasIndex(x => x.Number).IsUnique();
                b.HasIndex(x => new { x.Year, x.Month, x.WorkshopId }).IsUnique();
            });
            builder.Entity<FnPayslip>(b =>
            {
                b.ToTable("FnPayslips");
                b.Property(x => x.DaysPaid).HasPrecision(9, 2);
                b.Property(x => x.GrossEarnings).HasPrecision(18, 0);
                b.Property(x => x.InsurableTotal).HasPrecision(18, 0);
                b.Property(x => x.TaxableTotal).HasPrecision(18, 0);
                b.Property(x => x.EmployeeInsurance).HasPrecision(18, 0);
                b.Property(x => x.EmployerInsurance).HasPrecision(18, 0);
                b.Property(x => x.Tax).HasPrecision(18, 0);
                b.Property(x => x.OtherDeductions).HasPrecision(18, 0);
                b.Property(x => x.Provisions).HasPrecision(18, 0);
                b.Property(x => x.Net).HasPrecision(18, 0);
                b.HasIndex(x => new { x.RunId, x.EmployeeId }).IsUnique();
                b.HasIndex(x => x.EmployeeId);
                b.HasOne(x => x.Run).WithMany(x => x.Payslips).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<FnPayslipLine>(b =>
            {
                b.ToTable("FnPayslipLines");
                b.Property(x => x.ItemCode).HasMaxLength(30).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200);
                b.Property(x => x.AccountRole).HasMaxLength(50);
                b.Property(x => x.Base).HasPrecision(18, 4);
                b.Property(x => x.Amount).HasPrecision(18, 0);
                b.HasOne(x => x.Payslip).WithMany(x => x.Lines).HasForeignKey(x => x.PayslipId).OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<FnPayslipAllocation>(b =>
            {
                b.ToTable("FnPayslipAllocations");
                b.Property(x => x.Share).HasPrecision(18, 6);
                b.HasOne(x => x.Payslip).WithMany(x => x.Allocations).HasForeignKey(x => x.PayslipId).OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------------------------------------------------------ changes to existing tables
            builder.Entity<FnPayment>(b =>
            {
                b.HasIndex(x => x.PayrollRunId).HasFilter("[PayrollRunId] IS NOT NULL");
                b.HasIndex(x => x.BonusRunId).HasFilter("[BonusRunId] IS NOT NULL");
                b.HasIndex(x => x.TerminationId).HasFilter("[TerminationId] IS NOT NULL");
            });
        }
    }
}
