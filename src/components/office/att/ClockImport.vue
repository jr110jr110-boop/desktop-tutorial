<script setup lang="ts">
// Import of clock events from an Excel / CSV file: columns personnel code, Persian date, time, device, direction.
// The file is read in the browser, shown as a preview, sent as rows and the server answers accepted / duplicates / rejected / errors.
import { computed, ref } from "vue";
import ExcelRows from "@/components/fn/ExcelRows.vue";
import type { ImportField } from "@/components/fn/ExcelRows.vue";
import { importClock } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import { normalizeTime } from "./attCommon";

const emit = defineEmits(["close", "done"]);
const fields: ImportField[] = [
  { key: "code", label: "کد پرسنلی", words: ["کد پرسنلی", "کد", "پرسنلی", "کد کارت", "personnelcode", "code"], required: true },
  { key: "date", label: "تاریخ", words: ["تاریخ", "date"], type: "date", required: true },
  { key: "time", label: "ساعت", words: ["ساعت", "زمان", "time"], required: true },
  { key: "device", label: "دستگاه", words: ["دستگاه", "device"] },
  { key: "direction", label: "ورود/خروج", words: ["جهت", "ورود/خروج", "نوع", "direction", "in/out"] },
];
const rows = ref<any[]>([]);
const deviceCode = ref("");
const busy = ref(false);
const result = ref<any>(null);
const badRows = computed(() => rows.value.filter((r) => !r.code || !r.date || !normalizeTime(r.time)).length);

async function run() {
  if (!rows.value.length) return fail("ابتدا فایل را انتخاب کنید.");
  busy.value = true;
  const payload = rows.value.map((r) => ({ code: r.code, date: r.date, time: normalizeTime(r.time) || r.time, device: r.device, direction: r.direction }));
  const [d, error] = await importClock({ deviceCode: deviceCode.value.trim() || null, rows: payload });
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok(`${d?.imported ?? 0} تردد وارد شد.`);
  emit("done");
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal wide">
      <h2 class="inv-title mb-2">ورود ترددها از فایل</h2>
      <p class="inv-muted small">فایل xlsx یا csv با ردیف اول به عنوان عنوان ستون‌ها. ترددهای تکراری شمرده می‌شوند و دوباره ثبت نمی‌شوند؛ زمان آینده یا ماه با حقوق تایید شده رد می‌شود. ستون جهت: ورود، خروج، in یا out (خالی = نامشخص).</p>
      <ExcelRows :fields="fields" :disabled="busy" @rows="rows = $event; result = null" />
      <div class="d-flex align-items-end gap-3 flex-wrap mb-2">
        <div><label class="form-label small mb-0">کد دستگاه (برای ردیف‌های بدون ستون دستگاه)</label><input v-model="deviceCode" class="form-control form-control-sm inv-ltr" style="max-width: 200px" /></div>
        <span v-if="rows.length" class="small">{{ rows.length }} ردیف خوانده شد</span>
        <span v-if="badRows" class="inv-badge warn">{{ badRows }} ردیف کد، تاریخ یا ساعت خوانا ندارد (سرور آن را خطا می‌شمارد)</span>
      </div>
      <div v-if="result" class="mb-2">
        <span class="inv-badge ok me-1">پذیرفته‌شده: {{ result.imported }}</span>
        <span class="inv-badge info me-1">تکراری: {{ result.duplicates }}</span>
        <span :class="result.rejected ? 'bad' : 'ok'" class="inv-badge me-1">ردشده: {{ result.rejected }}</span>
        <span :class="result.errors?.length ? 'bad' : 'ok'" class="inv-badge">خطا: {{ result.errors?.length || 0 }}</span>
        <ul v-if="result.errors?.length" class="small mt-2" style="max-height: 180px; overflow: auto"><li v-for="(e, i) in result.errors" :key="i">{{ e }}</li></ul>
      </div>
      <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">بستن</button><button :disabled="busy || !rows.length" class="btn btn-sm btn-primary" @click="run">ورود ترددها</button></div>
    </div>
  </div>
</template>
