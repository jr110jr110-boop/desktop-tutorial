<script setup lang="ts">
// Shift definitions: title, start / end (a shift may cross midnight), break, night window and work days (week starts on Saturday).
import { computed, onMounted, ref } from "vue";
import { getShifts, saveShift } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import { minutesText, weekDays, weekOrder } from "@/composables/office";
import TimeField from "./TimeField.vue";
import { rowsOf } from "./attCommon";

const shifts = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);

async function load() {
  const [d, error] = await getShifts();
  if (error) return fail(error);
  shifts.value = rowsOf(d);
}
function edit(s: any | null) {
  form.value = s
    ? { id: s.id, title: s.title, start: s.start, end: s.end, breakMinutes: String(s.breakMinutes ?? 0), nightStart: s.nightStart || "22:00", nightEnd: s.nightEnd || "06:00", workDays: [...(s.workDays || [])], isActive: !!s.isActive }
    : { id: null, title: "", start: "08:00", end: "16:00", breakMinutes: "30", nightStart: "22:00", nightEnd: "06:00", workDays: [6, 0, 1, 2, 3], isActive: true };
}
const toMin = (t: string) => { const [h, m] = t.split(":").map(Number); return h * 60 + m; };
/** Planned minutes preview: end - start (+24 h when the shift crosses midnight) - break. */
const preview = computed(() => {
  const f = form.value;
  if (!f || !f.start || !f.end) return null;
  let span = toMin(f.end) - toMin(f.start);
  const crosses = span <= 0;
  if (crosses) span += 1440;
  return { minutes: span - (Number(f.breakMinutes) || 0), crosses };
});
async function save() {
  const f = form.value;
  if (!f.title.trim()) return fail("عنوان شیفت را وارد کنید.");
  if (!f.start || !f.end) return fail("ساعت شروع و پایان را وارد کنید.");
  if (!f.nightStart || !f.nightEnd) return fail("بازه شب‌کاری را وارد کنید.");
  if (!f.workDays.length) return fail("حداقل یک روز کاری انتخاب کنید.");
  const brk = Number(f.breakMinutes) || 0;
  if (brk < 0 || (preview.value && preview.value.minutes <= 0)) return fail("مدت استراحت نامعتبر است؛ کار برنامه‌ای باید مثبت باشد.");
  busy.value = true;
  const [, error] = await saveShift({ id: f.id, title: f.title.trim(), start: f.start, end: f.end, breakMinutes: brk, nightStart: f.nightStart, nightEnd: f.nightEnd, workDays: [...f.workDays].sort((a: number, b: number) => a - b), isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("شیفت ذخیره شد.");
  form.value = null;
  load();
}
const daysText = (s: any) => weekOrder.filter((d) => (s.workDays || []).includes(d)).map((d) => weekDays[d]).join("، ");
function toggle(day: number) {
  const list: number[] = form.value.workDays;
  const i = list.indexOf(day);
  if (i >= 0) list.splice(i, 1);
  else list.push(day);
}
onMounted(load);
</script>

<template>
  <div>
    <div class="inv-toolbar"><span class="inv-muted small">شیفت‌هایی که از نیمه‌شب می‌گذرند (مثلا ۲۲:۰۰ تا ۰۶:۰۰) مجازند.</span><button class="btn btn-sm btn-primary" @click="edit(null)">شیفت جدید</button></div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>عنوان</th><th>شروع</th><th>پایان</th><th>استراحت (دقیقه)</th><th>کار برنامه‌ای (ساعت:دقیقه)</th><th>بازه شب‌کاری</th><th>روزهای کاری</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!shifts.length"><td colspan="9" class="text-center inv-muted">شیفتی تعریف نشده است.</td></tr>
          <tr v-for="s in shifts" :key="s.id">
            <td>{{ s.title }}</td><td class="inv-ltr">{{ s.start }}</td><td class="inv-ltr">{{ s.end }}<span v-if="s.crossesMidnight" class="inv-badge info ms-1">فردا</span></td>
            <td class="fn-num">{{ s.breakMinutes }}</td><td class="fn-num">{{ minutesText(s.scheduledMinutes) }}</td><td class="inv-ltr">{{ s.nightStart }} - {{ s.nightEnd }}</td>
            <td>{{ daysText(s) }}</td><td><span :class="s.isActive ? 'ok' : 'warn'" class="inv-badge">{{ s.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button class="btn btn-sm btn-outline-secondary" @click="edit(s)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش شیفت" : "شیفت جدید" }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" placeholder="مثلا شیفت صبح" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شروع *</label><TimeField v-model="form.start" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">پایان *</label><TimeField v-model="form.end" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">استراحت (دقیقه)</label><input v-model="form.breakMinutes" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شروع شب‌کاری</label><TimeField v-model="form.nightStart" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">پایان شب‌کاری</label><TimeField v-model="form.nightEnd" /></div>
          <div class="col-md-4 d-flex align-items-end"><label class="small"><input v-model="form.isActive" type="checkbox" class="form-check-input me-1" />فعال</label></div>
          <div class="col-12">
            <label class="form-label small mb-1">روزهای کاری *</label>
            <div class="d-flex flex-wrap gap-3">
              <label v-for="d in weekOrder" :key="d" class="small"><input :checked="form.workDays.includes(d)" type="checkbox" class="form-check-input me-1" @change="toggle(d)" />{{ weekDays[d] }}</label>
            </div>
          </div>
          <div v-if="preview" class="col-12 small inv-muted">کار برنامه‌ای هر روز: <b>{{ minutesText(preview.minutes) }}</b> ساعت<span v-if="preview.crosses"> (شیفت از نیمه‌شب می‌گذرد: پایان در روز بعد)</span></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
