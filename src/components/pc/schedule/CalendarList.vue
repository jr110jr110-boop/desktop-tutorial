<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getCalendars, saveCalendar } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import JDate from "@/components/pc/JDate.vue";
import { fail, jalali, ok } from "@/composables/inventory";
import { dateOnly, weekdays } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Working calendars: weekly working days (bit 1 << day, Sunday = bit 0), hours per day and exceptions (a holiday or an extra working day).
const rows = ref<any[]>([]);
const c = ref<any>(null);
const busy = ref(false);
const exDate = ref<string | null>(null);
const exWorking = ref(false);
const exNote = ref("");
const canEdit = hasRole(ProjectControlRoles.planning);
const order = [6, 0, 1, 2, 3, 4, 5]; // Saturday first

async function load() {
  const [data, error] = await getCalendars();
  if (error) return fail(error);
  rows.value = rowsOf(data);
}
const isWork = (mask: number, day: number) => (mask & (1 << day)) !== 0;
const daysText = (mask: number) => order.filter((d) => isWork(mask, d)).map((d) => weekdays[d]).join("، ");
function toggle(day: number) {
  c.value.workingWeekdays ^= 1 << day;
}
function open(x: any) {
  c.value = JSON.parse(JSON.stringify(x));
  c.value.exceptions = (c.value.exceptions || []).map((e: any) => ({ ...e, date: dateOnly(e.date) }));
}
const blank = () => ({ id: null, name: "", workingWeekdays: 95, hoursPerDay: 8, useCompanyHolidays: true, exceptions: [] });
function addException() {
  if (!exDate.value) return;
  if (c.value.exceptions.some((e: any) => e.date === exDate.value)) return fail(new Error("این تاریخ قبلاً اضافه شده است."));
  c.value.exceptions.push({ date: exDate.value, isWorking: exWorking.value, note: exNote.value });
  exDate.value = null;
  exNote.value = "";
}
async function save() {
  busy.value = true;
  const [, error] = await saveCalendar(c.value);
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  c.value = null;
  load();
}
onMounted(load);
</script>

<template>
  <div>
    <div class="d-flex justify-content-between mb-2"><b>تقویم‌های کاری</b><button v-if="canEdit" class="btn btn-sm btn-primary d-print-none" @click="c = blank()">تقویم جدید</button></div>
    <table class="table table-sm table-hover">
      <thead><tr><th>نام</th><th>روزهای کاری</th><th>ساعت در روز</th><th>تعطیلات شرکت</th><th>استثناها</th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td colspan="5" class="text-center inv-muted">تقویم اختصاصی نیست؛ تقویم شرکت (روزهای کاری و تعطیلات) به کار می‌رود.</td></tr>
        <tr v-for="x in rows" :key="x.id" role="button" @click="open(x)">
          <td>{{ x.name }}</td><td>{{ daysText(x.workingWeekdays) }}</td><td>{{ x.hoursPerDay }}</td><td>{{ x.useCompanyHolidays ? "بله" : "خیر" }}</td><td>{{ (x.exceptions || []).length }}</td>
        </tr>
      </tbody>
    </table>
    <div v-if="c" class="border rounded p-3 d-print-none">
      <div class="row g-2">
        <div class="col-md-4"><label class="form-label small mb-0">نام *</label><input v-model="c.name" :disabled="!canEdit" class="form-control form-control-sm" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">ساعت کار در روز</label><input v-model.number="c.hoursPerDay" :disabled="!canEdit" class="form-control form-control-sm" type="number" min="1" max="24" step="0.5" /></div>
        <div class="col-md-3 pt-4"><label class="form-check"><input v-model="c.useCompanyHolidays" :disabled="!canEdit" class="form-check-input" type="checkbox" /> تعطیلات رسمی شرکت لحاظ شود</label></div>
        <div class="col-12 d-flex flex-wrap gap-3">
          <label v-for="d in order" :key="d" class="form-check"><input :checked="isWork(c.workingWeekdays, d)" :disabled="!canEdit" class="form-check-input" type="checkbox" @change="toggle(d)" /> {{ weekdays[d] }}</label>
        </div>
      </div>
      <h3 class="inv-title mt-3 mb-2" style="font-size: 0.95rem">استثناها (تعطیلی یا روز کاری اضافه)</h3>
      <table class="table table-sm">
        <tbody>
          <tr v-for="(e, i) in c.exceptions" :key="e.date"><td>{{ jalali(e.date) }}</td><td>{{ e.isWorking ? "روز کاری" : "تعطیل" }}</td><td>{{ e.note }}</td><td><button v-if="canEdit" class="btn btn-sm btn-outline-danger" @click="c.exceptions.splice(i, 1)">حذف</button></td></tr>
        </tbody>
      </table>
      <div v-if="canEdit" class="row g-2 align-items-end">
        <div class="col-md-3"><JDate v-model="exDate" /></div>
        <div class="col-md-2"><select v-model="exWorking" class="form-select form-select-sm"><option :value="false">تعطیل</option><option :value="true">روز کاری</option></select></div>
        <div class="col-md-4"><input v-model="exNote" class="form-control form-control-sm" placeholder="توضیح" /></div>
        <div class="col-md-3"><button :disabled="!exDate" class="btn btn-sm btn-outline-secondary" @click="addException">افزودن استثنا</button></div>
      </div>
      <div class="mt-3 d-flex gap-2"><button v-if="canEdit" :disabled="busy || !c.name || !c.workingWeekdays" class="btn btn-sm btn-primary" @click="save">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="c = null">بستن</button></div>
    </div>
  </div>
</template>
