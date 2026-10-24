<script setup lang="ts">
// Numbering rules: one counter per rule and Persian year. Kind and unit are fixed after creation; the pattern and the active flag can change.
import { computed, onMounted, ref } from "vue";
import { getNumberingRules, saveNumberingRule } from "@/api/OfficeAPIs";
import { letterKinds } from "@/composables/office";
import { jalaliNow } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { fa, loadAllUnits } from "./letterKit";

const rows = ref<any[]>([]);
const units = ref<any[]>([]);
const edit = ref<any>(null);
const busy = ref(false);
const kindLetters: Record<number, string> = { 1: "و", 2: "ص", 3: "د" };

async function load() {
  const [data, error] = await getNumberingRules({ PageNumber: 1, PageSize: 200 });
  if (error) return fail(error);
  rows.value = Array.isArray(data) ? data : data?.data || [];
}
function start(r?: any) {
  edit.value = r ? { ...r } : { id: null, kind: 2, unitId: null, pattern: "{yy}/{kind}/{unit}/{seq:5}", isActive: true };
}
const preview = computed(() => {
  const e = edit.value;
  if (!e) return "";
  const y = jalaliNow().year;
  const unit = units.value.find((u) => u.id === e.unitId)?.code || "";
  return fa(String(e.pattern || "")
    .replace(/\{yyyy\}/g, String(y)).replace(/\{yy\}/g, String(y % 100).padStart(2, "0"))
    .replace(/\{seq:(\d+)\}/g, (_m: string, n: string) => "1".padStart(Number(n), "0")).replace(/\{seq\}/g, "1")
    .replace(/\{unit\}/g, unit).replace(/\{kind\}/g, kindLetters[e.kind] || ""));
});
async function submit(dto = edit.value) {
  if (!String(dto.pattern || "").includes("{seq")) return fail("الگو باید شامل {seq} یا {seq:n} باشد.");
  busy.value = true;
  const [, error] = await saveNumberingRule({ id: dto.id || null, kind: dto.kind, unitId: dto.unitId || null, pattern: dto.pattern.trim(), isActive: dto.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("قاعده شماره‌گذاری ذخیره شد.");
  edit.value = null;
  load();
}
const toggle = (r: any) => submit({ ...r, isActive: !r.isActive });
onMounted(async () => {
  load();
  units.value = await loadAllUnits();
});
</script>

<template>
  <div>
    <div class="d-flex justify-content-between align-items-center mb-2">
      <p class="inv-muted mb-0">قاعده مخصوص یک واحد بر قاعده عمومی همان نوع نامه مقدم است. شماره نامه‌های آرشیو قدیم تغییر نمی‌کند.</p>
      <button class="btn btn-sm btn-primary" @click="start()">قاعده جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>نوع نامه</th><th>واحد</th><th>الگو</th><th>شماره بعدی (پیش‌نمایش)</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="6" class="text-center inv-muted">قاعده‌ای نیست.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td>{{ letterKinds[r.kind] }}</td>
            <td>{{ r.unitCode || "عمومی" }}</td>
            <td class="inv-ltr">{{ r.pattern }}</td>
            <td>{{ fa(r.preview) }}</td>
            <td><span :class="r.isActive ? 'ok' : 'bad'" class="inv-badge">{{ r.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td class="text-nowrap">
              <button class="btn btn-sm btn-link" @click="start(r)">ویرایش</button>
              <button :disabled="busy" class="btn btn-sm btn-link" @click="toggle(r)">{{ r.isActive ? "غیرفعال‌سازی" : "فعال‌سازی" }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="edit" class="inv-modal-backdrop" @click.self="edit = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ edit.id ? "ویرایش قاعده" : "قاعده شماره‌گذاری جدید" }}</h2>
        <div class="row g-2 mb-2">
          <div class="col-md-6">
            <label class="form-label small mb-0">نوع نامه</label>
            <select v-model.number="edit.kind" :disabled="!!edit.id" class="form-select form-select-sm"><option v-for="(t, k) in letterKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
          </div>
          <div class="col-md-6">
            <label class="form-label small mb-0">واحد سازمانی</label>
            <select v-model="edit.unitId" :disabled="!!edit.id" class="form-select form-select-sm"><option :value="null">عمومی (همه واحدها)</option><option v-for="u in units" :key="u.id" :value="u.id">{{ u.title }} ({{ u.code }})</option></select>
          </div>
        </div>
        <p v-if="edit.id" class="small inv-muted">نوع و واحد بعد از ساخت قاعده تغییر نمی‌کنند.</p>
        <label class="form-label small mb-0">الگو *</label>
        <input v-model="edit.pattern" class="form-control form-control-sm inv-ltr" />
        <div class="small mt-1">پیش‌نمایش: <b>{{ preview }}</b></div>
        <div class="small inv-muted mt-2">
          نشانه‌ها: <code>{yyyy}</code> سال شمسی چهار رقمی، <code>{yy}</code> دو رقمی، <code>{seq:n}</code> شماره ترتیبی با n رقم (الزامی)، <code>{unit}</code> کد واحد، <code>{kind}</code> حرف نوع (و، ص، د).
          شمارنده برای هر قاعده و هر سال شمسی از ابتدا شروع می‌شود.
        </div>
        <label class="small d-flex align-items-center gap-1 mt-2"><input v-model="edit.isActive" type="checkbox" /> فعال</label>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="edit = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="submit()">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>
