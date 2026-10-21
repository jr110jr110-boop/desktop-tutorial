# Fiscal calendar seam

Only the Persian (Shamsi) calendar exists. All fiscal-year and period dates come from `IFiscalCalendar`
(`src/Services/NasbNiro/Finance/FiscalCalendar.cs`), used by `FinanceSeed.YearNumberOf` and `FinanceSeed.CreateYearAsync`.
Dates are stored as `DateTime`; reports read the stored period dates and never compute months themselves.

To add another calendar later:
1. Write one class implementing `IFiscalCalendar`.
2. Add a calendar setting to `FnLegalEntity` and return the right implementation in `FiscalCalendars` (per entity).
3. Nothing else changes (vouchers, reports and services use stored years and periods).
