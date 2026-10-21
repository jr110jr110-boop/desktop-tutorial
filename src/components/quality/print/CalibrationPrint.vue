<script setup lang="ts">
import PrintForm from "@/components/print/PrintForm.vue";
import { jalali } from "@/composables/inventory";
import { distributionQc, equipmentStatuses, equipmentTypes, qualityFormCodes } from "@/composables/quality";

// FO-QC-007 control register, FO-QC-008 units' equipment list, FO-QC-009 equipment needing calibration,
// FO-QC-010 handover of calibrated equipment, FO-QC-011 equipment identity file.
const props = defineProps<{ kind: "control" | "units" | "needs" | "handover" | "identity"; rows?: any[]; eq?: any }>();

const titles = {
  control: "فرم کنترل کالیبراسیون دستگاه‌ها و تجهیزات",
  units: "فرم لیست تجهیزات واحدها",
  needs: "فرم تجهیزات نیازمند کالیبراسیون",
  handover: "فرم تحویل تجهیز کالیبره",
  identity: "شناسنامه تجهیز کالیبراسیون",
};
const codes = { control: qualityFormCodes.calibrationControl, units: qualityFormCodes.unitEquipmentList, needs: qualityFormCodes.calibrationNeeds, handover: qualityFormCodes.calibrationHandover, identity: qualityFormCodes.equipmentIdentity };

function sorted(): any[] {
  const list = [...(props.rows || [])];
  if (props.kind === "units") return list.sort((a, b) => (a.location || "").localeCompare(b.location || "") || a.code.localeCompare(b.code));
  if (props.kind === "needs") return list.filter((r) => r.requiresCalibration && (r.status === 1 || r.status === 2 || r.nextDue == null));
  return list.sort((a, b) => a.code.localeCompare(b.code));
}
const signatures = () =>
  props.kind === "handover"
    ? [{ title: 'مسئول کالیبراسیون' }, { title: 'مدیر کنترل کیفیت' }, { title: 'واحد تحویل‌گیرنده' }]
    : props.kind === "identity"
    ? [{ title: 'مسئول کالیبراسیون' }, { title: 'مدیر کنترل کیفیت' }]
    : props.kind === "units"
    ? [{ title: 'مسئول واحد' }, { title: 'مسئول کالیبراسیون' }]
    : [{ title: 'مسئول کالیبراسیون' }, { title: 'مدیر کنترل کیفیت' }];
</script>

<template>
  <PrintForm
    class="q-form"
    :title="titles[kind]"
    :form-code="codes[kind]"
    :fields="
      eq
        ? [
            { label: 'کد تجهیز', value: eq.code, ltr: true },
            { label: 'نام', value: eq.name },
            { label: 'نوع', value: equipmentTypes[eq.type] },
            { label: 'سازنده / مدل', value: (eq.maker || '') + ' ' + (eq.model || '') },
            { label: 'شماره سریال', value: eq.serialNumber, ltr: true },
            { label: 'واحد مالک', value: eq.ownerUnit },
            { label: 'محل نگهداری', value: eq.location },
            { label: 'محدوده / دقت', value: (eq.range || '-') + ' / ' + (eq.accuracy || '-') },
            { label: 'وضعیت', value: equipmentStatuses[eq.status] },
            { label: 'آخرین کالیبراسیون', value: jalali(eq.lastCalibrated) },
            { label: 'سررسید بعدی', value: jalali(eq.nextDue) },
            { label: 'دوره (ماه)', value: eq.intervalMonths },
          ]
        : []
    "
    :signatures="signatures()"
    :distribution="distributionQc">
    <template v-if="!eq">
      <table>
        <thead>
          <tr>
            <th>ردیف</th><th>کد</th><th>نام</th><th>نوع</th><th>سریال</th><th>محل / واحد</th>
            <template v-if="kind === 'control'"><th>آخرین کالیبراسیون</th><th>سررسید</th><th>دوره (ماه)</th><th>وضعیت</th></template>
            <template v-else-if="kind === 'needs'"><th>دوره پیشنهادی (ماه)</th><th>سررسید</th><th>وضعیت</th></template>
          </tr>
        </thead>
        <tbody>
          <tr v-if="!sorted().length"><td colspan="10">موردی نیست.</td></tr>
          <tr v-for="(r, i) in sorted()" :key="r.id">
            <td>{{ i + 1 }}</td><td class="inv-ltr">{{ r.code }}</td><td>{{ r.name }}</td><td>{{ equipmentTypes[r.type] }}</td><td class="inv-ltr">{{ r.serialNumber }}</td><td>{{ r.location }}</td>
            <template v-if="kind === 'control'"><td>{{ jalali(r.lastCalibrated) }}</td><td>{{ jalali(r.nextDue) }}</td><td>{{ r.intervalMonths }}</td><td>{{ equipmentStatuses[r.status] }}</td></template>
            <template v-else-if="kind === 'needs'"><td>{{ r.intervalMonths }}</td><td>{{ jalali(r.nextDue) }}</td><td>{{ equipmentStatuses[r.status] }}</td></template>
          </tr>
        </tbody>
      </table>
    </template>
    <template v-else>
      <table>
        <thead>
          <tr><th>تاریخ</th><th>آزمایشگاه</th><th>گواهی</th><th>As-found</th><th>As-left</th><th>تنظیم</th><th>سررسید بعدی</th><th>یادداشت</th></tr>
        </thead>
        <tbody>
          <tr v-if="!(eq.events || []).length"><td colspan="8">کالیبراسیونی ثبت نشده.</td></tr>
          <tr v-for="e in kind === 'handover' ? (eq.events || []).slice(0, 1) : eq.events || []" :key="e.id">
            <td>{{ jalali(e.eventDate) }}</td><td>{{ e.labName }}<span v-if="e.labAccredited"> (دارای اعتبار)</span></td><td class="inv-ltr">{{ e.certificateNumber }}</td>
            <td>{{ e.asFoundPass ? "قبول" : "خارج از تلرانس" }}</td><td>{{ e.asLeftPass ? "قبول" : "مردود" }}</td><td>{{ e.adjusted ? "بله" : "خیر" }}</td><td>{{ jalali(e.nextDue) }}</td><td>{{ e.note }}</td>
          </tr>
        </tbody>
      </table>
      <table v-if="kind === 'handover'" class="mt-1">
        <tbody>
          <tr><td style="width: 25%"><b>واحد تحویل‌گیرنده</b></td><td>&nbsp;</td><td style="width: 20%"><b>تاریخ تحویل</b></td><td>&nbsp;</td></tr>
        </tbody>
      </table>
    </template>
  </PrintForm>
</template>
