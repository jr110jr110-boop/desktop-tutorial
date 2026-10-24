<script setup lang="ts">
// Assignment of employees to shifts for a Persian date range (the open end means "until changed").
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import { getShifts, getShiftAssignments, saveShiftAssignment } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import EmployeePick from "./EmployeePick.vue";
import { rowsOf } from "./attCommon";

const shifts = ref<any[]>([]);
const employee = ref<any>(null);
const shiftId = ref<number | "">("");
const filters = ref<any>({});
const list = usePagedList((p: any) => getShiftAssignments(p), filters);
const form = ref<any>(null);
const busy = ref(false);

function search() {
  filters.value = { EmployeeId: employee.value?.id || null, ShiftId: shiftId.value === "" ? null : shiftId.value };
  return list.search();
}
const openForm = () => (form.value = { employee: null, shiftId: "", from: null, to: null });
async function save() {
  const f = form.value;
  if (!f.employee) return fail("کارمند را انتخاب کنید.");
  if (!f.shiftId) return fail("شیفت را انتخاب کنید.");
  if (!f.from) return fail("تاریخ شروع را وارد کنید.");
  if (f.to && f.to < f.from) return fail("تاریخ پایان از شروع زودتر است.");
  busy.value = true;
  const [, error] = await saveShiftAssignment({ employeeId: f.employee.id, shiftId: Number(f.shiftId), fromDate: f.from, toDate: f.to || null });
  busy.value = false;
  if (error) return fail(error);
  ok("تخصیص شیفت ذخیره شد.");
  form.value = null;
  list.load();
}
onMounted(async () => {
  const [d] = await getShifts();
  shifts.value = rowsOf(d);
  search();
});
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <div style="min-width: 220px"><label class="form-label small mb-0">کارمند</label><EmployeePick v-model="employee" placeholder="همه" /></div>
      <div><label class="form-label small mb-0">شیفت</label><select v-model="shiftId" class="form-select form-select-sm"><option value="">همه</option><option v-for="s in shifts" :key="s.id" :value="s.id">{{ s.title }}</option></select></div>
      <button class="btn btn-sm btn-secondary align-self-end" @click="search">نمایش</button>
      <button class="btn btn-sm btn-primary align-self-end" @click="openForm">تخصیص شیفت</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>کارمند</th><th>کد پرسنلی</th><th>شیفت</th><th>از تاریخ</th><th>تا تاریخ</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="5" class="text-center inv-muted">تخصیصی ثبت نشده است.</td></tr>
          <tr v-for="a in list.rows.value" :key="a.id"><td>{{ a.employeeName }}</td><td class="inv-ltr">{{ a.personnelCode }}</td><td>{{ a.shiftTitle }}</td><td class="inv-ltr">{{ a.fromDateText }}</td><td class="inv-ltr">{{ a.toDateText || "تا اطلاع ثانوی" }}</td></tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">تخصیص شیفت</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">کارمند *</label><EmployeePick v-model="form.employee" /></div>
          <div class="col-12"><label class="form-label small mb-0">شیفت *</label><select v-model="form.shiftId" class="form-select form-select-sm"><option value="">انتخاب کنید</option><option v-for="s in shifts.filter((x) => x.isActive)" :key="s.id" :value="s.id">{{ s.title }} ({{ s.start }} - {{ s.end }})</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">از تاریخ *</label><FnDate v-model="form.from" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">تا تاریخ (اختیاری)</label><FnDate v-model="form.to" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
