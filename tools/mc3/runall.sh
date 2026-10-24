#!/bin/bash
cd "$(dirname "$0")"
NEW=FnBankGuarantees,FnContractors,FnSupplierControls,FnConfirmationLetters,FnSettlements,FnFinancialStatements,FnCosting,FnPayments,FnPayrollMasters,FnReportBalance
for role in FINANCEMANAGER FINANCIAL TREASURY PAYROLL CEO AUDITOR PMO; do
  node run.mjs "$NEW" 1 $role deep > out_$role.txt 2>&1 &
done
wait
echo done > all_done.txt
