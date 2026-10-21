<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali, rial } from "@/composables/inventory";
import { changeDecisions, changeOrigins, changeTypes, contractLabel, distributionPlanning, pcFormCodes, unitTitle, votes } from "@/composables/projectControl";

// Change request (FO-PO-011) and its impact analysis with the board votes and the decision (FO-PO-015). The printed pair is the record of the change.
defineProps<{ c: any; activities: any[] }>();
</script>

<template>
    <PrintForm
      class="q-form"
      title="فرم درخواست تغییر"
      :form-code="pcFormCodes.changeRequest"
      :fields="[
        { label: 'شماره', value: c.number, ltr: true },
        { label: 'پروژه', value: contractLabel(c.contractId) },
        { label: 'منشأ', value: changeOrigins[c.origin] },
        { label: 'نوع تغییر', value: changeTypes[c.type] },
        { label: 'تاریخ ثبت', value: jalali(c.registeredAt) },
        { label: 'مهلت تحلیل', value: jalali(c.dueDate) },
        { label: 'تغییر مبلغ درخواستی (ریال)', value: rial(c.requestedPriceDelta) },
        { label: 'تغییر زمان درخواستی (روز)', value: c.requestedDaysDelta },
      ]"
      :notes="[
        { label: 'شرح تغییر', value: c.description },
        { label: 'دلیل تغییر', value: c.reason },
        ...(c.managerAgreement ? [{ label: 'موافقت مدیر درخواست‌کننده', value: c.managerAgreement }] : []),
      ]"
      :signatures="[{ title: 'درخواست‌کننده' }, { title: c.origin === 2 ? 'مدیر درخواست‌کننده' : 'مدیر بازرگانی' }, { title: 'مدیر برنامه‌ریزی و کنترل پروژه (ثبت)', date: jalali(c.registeredAt) }]"
      :distribution="distributionPlanning">
      <table>
        <thead><tr><th>ردیف</th><th>فعالیت متاثر</th><th>تغییر مدت (روز)</th><th>تغییر بودجه (ریال)</th></tr></thead>
        <tbody>
          <tr v-if="!c.scenario.length"><td colspan="4">سناریویی ثبت نشده.</td></tr>
          <tr v-for="(s, i) in c.scenario" :key="s.activityId"><td>{{ i + 1 }}</td><td>{{ activities.find((a) => a.id === s.activityId)?.title || "#" + s.activityId }}</td><td>{{ s.durationDelta }}</td><td>{{ rial(s.budgetDelta) }}</td></tr>
        </tbody>
      </table>
    </PrintForm>

    <PrintForm
      class="q-form"
      title="فرم تحلیل اثر تغییر و تصمیم هیئت"
      :form-code="pcFormCodes.changeAnalysis"
      :fields="[
        { label: 'شماره درخواست', value: c.number, ltr: true },
        { label: 'تاریخ تحلیل', value: jalali(c.analysedAt) },
        { label: 'تغییر پایان پروژه (روز)', value: c.impactDeltaDays },
        { label: 'تغییر هزینه (ریال)', value: c.impactDeltaCost != null ? rial(c.impactDeltaCost) : '' },
        { label: 'اثر بر مسیر بحرانی', value: c.impactOnCriticalPath == null ? '' : c.impactOnCriticalPath ? 'دارد' : 'ندارد' },
        { label: 'سطرهای سفارش متاثر', value: c.affectedPoLines },
        { label: 'نقاط بازرسی ITP متاثر', value: c.affectedItpPoints },
        { label: 'تصمیم', value: changeDecisions[c.decision] },
      ]"
      :notes="[
        { label: 'اثر بر محدوده', value: c.impactScope },
        { label: 'اثر بر منابع', value: c.impactResources },
        { label: 'اثر بر کیفیت', value: c.impactQuality },
        { label: 'اثر بر ریسک', value: c.impactRisk },
        { label: 'اثر بر تامین', value: c.impactProcurement },
        { label: 'اثر بر قرارداد با کارفرما', value: c.impactClientContract },
        { label: 'پیشنهاد مدیر پروژه', value: c.recommendation },
        { label: 'دلیل تصمیم', value: c.decisionNote },
      ]"
      :signatures="[
        { title: 'مدیر برنامه‌ریزی و کنترل پروژه (تحلیل)', date: jalali(c.analysedAt) },
        { title: 'مدیر پروژه (پیشنهاد)' },
        ...c.board.map((m: any) => ({ title: unitTitle(m.role), hint: votes[m.vote], name: m.userName, date: jalali(m.votedAt) })),
        { title: c.decidedByRole ? unitTitle(c.decidedByRole) + ' (تصمیم‌گیرنده)' : 'تصمیم‌گیرنده', date: jalali(c.decidedAt) },
      ]"
      :distribution="distributionPlanning">
      <table>
        <thead><tr><th>فعالیت</th><th>پایان قبلی</th><th>پایان جدید</th><th>بحرانی</th></tr></thead>
        <tbody>
          <tr v-if="!c.impactActivities.length"><td colspan="4">فعالیتی جابجا نشده یا تحلیل انجام نشده است.</td></tr>
          <tr v-for="a in c.impactActivities" :key="a.activityId"><td class="inv-ltr">{{ a.code }}</td><td>{{ jalali(a.oldFinish) }}</td><td>{{ jalali(a.newFinish) }}</td><td>{{ a.critical ? "بله" : "" }}</td></tr>
        </tbody>
      </table>
    </PrintForm>
</template>
