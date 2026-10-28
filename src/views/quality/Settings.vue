<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getQualitySettings, saveQualitySetting } from "@/api/QualityAPIs";
import { fail, ok } from "@/composables/inventory";
import { resetSettingValues } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Numbers the company can change. R1q D30: every setting has a range (QualitySettingDto min / max) and takes whole numbers only; the screen shows the range and refuses a value
// outside it before it is sent, and the API checks it again.
const items = ref<any[]>([]);
const busy = ref(false);
const canWrite = hasRole(QualityRoles.settings);

const toRows = (data: any) => (Array.isArray(data) ? data : data?.data || []).map((s: any) => ({ ...s, edit: s.value }));

async function load() {
  const [data, error] = await getQualitySettings();
  if (error) return fail(error);
  items.value = toRows(data);
}

/** A range is known for every setting of the API; min = max = 0 means the API sent none (the value is then only checked by the API). */
const hasRange = (s: any) => !(Number(s.min) === 0 && Number(s.max) === 0);

/** Why the typed value is refused, or "" - the same wording as the API. */
function problem(s: any): string {
  const raw = s.edit;
  if (raw === "" || raw === null || raw === undefined || !Number.isFinite(Number(raw))) return "مقدار را به‌صورت عدد وارد کنید.";
  const v = Number(raw);
  if (hasRange(s) && (!Number.isInteger(v) || v < Number(s.min) || v > Number(s.max))) return `مقدار باید عدد صحیح بین ${s.min} تا ${s.max} باشد.`;
  if (!hasRange(s) && !Number.isInteger(v)) return "مقدار باید عدد صحیح باشد.";
  return "";
}

const changed = (s: any) => Number(s.edit) !== Number(s.value);

async function save(s: any) {
  const why = problem(s);
  if (why) return fail(`«${s.title}»: ${why}`);
  busy.value = true;
  const [data, error] = await saveQualitySetting(s.key, Number(s.edit));
  busy.value = false;
  if (error) return fail(error);
  // the pages that read a setting (the back-date window of the date pickers, the minimum notice days of an ITP) must read the new value
  resetSettingValues();
  ok("ذخیره شد.");
  if (Array.isArray(data)) items.value = toRows(data);
  else load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تنظیمات کیفیت</h1></div>
      <p class="inv-muted small">هر تنظیم عدد صحیح در محدوده مجاز خودش است؛ مقدار بیرون از محدوده ذخیره نمی‌شود. تغییر برای ثبت‌های بعدی اعمال می‌شود.</p>
      <table class="table table-sm align-middle">
        <thead><tr><th>تنظیم</th><th>مقدار</th><th>محدوده مجاز</th><th>مبنا</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!items.length"><td colspan="5" class="text-center inv-muted">تنظیمی نیست.</td></tr>
          <tr v-for="s in items" :key="s.key">
            <td>{{ s.title }}<div class="inv-muted small inv-ltr">{{ s.key }}</div></td>
            <td style="max-width: 160px">
              <input
                v-model.number="s.edit"
                :class="{ 'is-invalid': canWrite && changed(s) && problem(s) }"
                :disabled="!canWrite"
                :max="hasRange(s) ? s.max : undefined"
                :min="hasRange(s) ? s.min : undefined"
                class="form-control form-control-sm inv-ltr"
                step="1"
                type="number" />
              <div v-if="canWrite && changed(s) && problem(s)" class="text-danger small mt-1">{{ problem(s) }}</div>
            </td>
            <td class="small"><span v-if="hasRange(s)" class="inv-ltr">{{ s.min }} – {{ s.max }}</span><span v-else class="inv-muted">—</span></td>
            <td class="small">{{ s.isDefault ? "پیش‌فرض سیستم" : s.source }}</td>
            <td><button v-if="canWrite" :disabled="busy || !changed(s) || !!problem(s)" class="btn btn-sm btn-primary" @click="save(s)">ذخیره</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
