<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { groupDigits, numOrNull } from "@/components/sales/pipeline/pipelineKit";
import { getSlReasonCodes, getSlSettings, saveSlReasonCode, saveSlSetting } from "@/api/SalesAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { reasonKinds, rialText, salesSettingTitles } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// Sales settings (SlSettings): the proposal approval threshold (CEO) and the won / lost / no-bid reason codes (SALES, PMO).
const settings = ref<any[]>([]);
const values = ref<Record<string, string>>({});
const busy = ref(false);
const canSet = hasRole(SalesRoles.settingSetters);
const canEditReasons = hasRole(SalesRoles.masterWriters);
const filters = ref({ Kind: null as number | null, IsActive: null as boolean | null, Contains: "" });
const list = usePagedList(getSlReasonCodes, filters);
const form = ref<any>(null);

async function loadSettings() {
  const [d, error] = await getSlSettings();
  if (error) return fail(error);
  settings.value = Array.isArray(d) ? d : [];
  values.value = Object.fromEntries(settings.value.map((s) => [s.key, s.value !== null && s.value !== undefined ? groupDigits(s.value) : ""]));
}
async function saveSetting(key: string) {
  busy.value = true;
  const [, error] = await saveSlSetting({ key, value: numOrNull(values.value[key]) });
  busy.value = false;
  if (error) return fail(error);
  ok("تنظیم ذخیره شد.");
  loadSettings();
}
async function saveReason() {
  const f = form.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail({ message: "کد و عنوان دلیل را بنویسید." });
  busy.value = true;
  const [, error] = await saveSlReasonCode({ id: f.id, code: String(f.code).trim(), title: String(f.title).trim(), kind: f.kind, isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("دلیل ذخیره شد.");
  form.value = null;
  list.load();
}
onMounted(() => {
  loadSettings();
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">تنظیمات فروش</h1></div>

      <h2 class="inv-title mb-2">سقف تایید فروش</h2>
      <div v-for="s in settings" :key="s.key" class="border rounded p-2 mb-3">
        <label class="form-label mb-1">{{ s.title || salesSettingTitles[s.key] || s.key }}</label>
        <div class="d-flex gap-2 align-items-center flex-wrap">
          <input v-model="values[s.key]" :disabled="!canSet" class="form-control form-control-sm inv-ltr" style="max-width: 260px" inputmode="numeric" placeholder="تعیین نشده" @blur="values[s.key] = groupDigits(values[s.key])" />
          <span class="small text-muted">ریال</span>
          <button v-if="canSet" :disabled="busy" class="btn btn-sm btn-primary" @click="saveSetting(s.key)">ذخیره</button>
        </div>
        <div class="small text-muted mt-1">
          مقدار فعلی: {{ s.value === null || s.value === undefined ? "تعیین نشده" : rialText(s.value) + " ریال" }}.
          پیشنهادی که قیمتش از این سقف بیشتر باشد یا زیر بهای تمام‌شده باشد را فقط مدیرعامل تایید می‌کند. تا زمانی که سقف خالی است، همه پیشنهادها را مدیرعامل تایید می‌کند؛
          برای برگرداندن به این حالت، مقدار را خالی ذخیره کنید.
          <span v-if="s.note"> {{ s.note }}</span>
        </div>
        <div v-if="!canSet" class="small text-muted">فقط مدیرعامل این تنظیم را تغییر می‌دهد.</div>
      </div>
      <p v-if="!settings.length" class="inv-muted">تنظیمی برگردانده نشد.</p>

      <div class="inv-toolbar mt-4">
        <h2 class="inv-title mb-0">کدهای دلیل (برد، باخت، عدم شرکت)</h2>
        <button v-if="canEditReasons" class="btn btn-sm btn-primary" @click="form = { id: null, code: '', title: '', kind: 1, isActive: true }">دلیل جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keyup.enter="list.search" />
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in reasonKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.IsActive" class="form-select form-select-sm"><option :value="null">فعال و غیرفعال</option><option :value="true">فعال</option><option :value="false">غیرفعال</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="5" class="text-center inv-muted">دلیلی ثبت نشده است.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td>{{ reasonKinds[r.kind] }}</td>
              <td><span :class="r.isActive ? 'ok' : ''" class="inv-badge">{{ r.isActive ? "فعال" : "غیرفعال" }}</span></td>
              <td><button v-if="canEditReasons" class="btn btn-sm btn-outline-primary" @click="form = { id: r.id, code: r.code, title: r.title, kind: r.kind, isActive: r.isActive }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

      <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
        <div class="inv-modal" style="max-width: 480px">
          <h2 class="inv-title mb-2">{{ form.id ? "ویرایش دلیل" : "دلیل جدید" }}</h2>
          <div class="row g-2">
            <div class="col-md-4"><label class="form-label small mb-0">کد</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
            <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in reasonKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
            <div class="col-md-6 d-flex align-items-end"><label class="form-check"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> <span class="form-check-label">فعال</span></label></div>
          </div>
          <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveReason">ذخیره</button></div>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
