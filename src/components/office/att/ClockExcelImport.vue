<script setup lang="ts">
// Excel export of the attendance software (PW Kara; devices US 10C and M Face 302), imported through a column mapping.
// Steps: choose the file -> the server shows the sheets and columns -> map them (or pick a saved profile) -> import. A mapping can be saved under a name and reused for the next export.
// Dates may be Persian or Gregorian. Personnel numbers nobody has are reported, never created. Importing the same file again stores nothing twice.
import { computed, onMounted, ref } from "vue";
import { getClockImportProfiles, importClockFile, previewClockFile, saveClockImportProfile } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";

const MaxFileBytes = 20 * 1048576; // the server refuses a larger file
const emit = defineEmits(["done"]);
const file = ref<File | null>(null);
const preview = ref<any>(null);
const profiles = ref<any[]>([]);
const profileId = ref<number | null>(null);
const saveAs = ref("");
const result = ref<any>(null);
const busy = ref(false);
const sheet = ref("");
const timeMode = ref<"split" | "single">("split");
const map = ref<any>(blank());

function blank() {
  return { headerRow: 1, firstDataRow: 0, codeColumn: "", dateColumn: "", timeColumn: "", dateTimeColumn: "", directionColumn: "", inValues: "", outValues: "", deviceColumn: "", siteColumn: "", defaultDeviceCode: "", dateOrder: "" };
}
const columns = computed<any[]>(() => preview.value?.columns || []);
const fieldRows = computed(() => [
  { key: "codeColumn", label: "شماره‌ی پرسنلی / کارت *" },
  ...(timeMode.value === "split" ? [{ key: "dateColumn", label: "تاریخ *" }, { key: "timeColumn", label: "ساعت *" }] : [{ key: "dateTimeColumn", label: "تاریخ و ساعت (یک ستون) *" }]),
  { key: "directionColumn", label: "ورود / خروج" }, { key: "deviceColumn", label: "دستگاه" }, { key: "siteColumn", label: "محل" },
]);
/** A column reference of a mapping (heading text or letter) as the letter of a column of this file, "" when the file has no such column. */
function toLetter(ref: string | null | undefined): string {
  const text = String(ref ?? "").trim();
  if (!text) return "";
  const byHeader = columns.value.find((c) => String(c.header ?? "").trim() === text);
  if (byHeader) return byHeader.letter;
  const byLetter = columns.value.find((c) => String(c.letter).toLowerCase() === text.toLowerCase());
  return byLetter ? byLetter.letter : "";
}
function applyMapping(m: any) {
  const next = { ...blank(), ...(m || {}) };
  for (const k of ["codeColumn", "dateColumn", "timeColumn", "dateTimeColumn", "directionColumn", "deviceColumn", "siteColumn"]) next[k] = columns.value.length ? toLetter(next[k]) : next[k] || "";
  // how a date with the year LAST is read; a profile saved before this field existed takes the guess of the file
  next.dateOrder = String(m?.dateOrder || preview.value?.suggested?.dateOrder || "").toUpperCase();
  next.headerRow = Number(m?.headerRow ?? 1);
  next.firstDataRow = Number(m?.firstDataRow ?? 0);
  map.value = next;
  timeMode.value = next.dateTimeColumn && !next.dateColumn ? "single" : "split";
}
async function loadProfiles() {
  const [d] = await getClockImportProfiles();
  profiles.value = Array.isArray(d) ? d : [];
}
async function choose(event: Event) {
  const input = event.target as HTMLInputElement;
  file.value = input.files?.[0] || null;
  preview.value = null;
  result.value = null;
  if (file.value && file.value.size > MaxFileBytes) {
    file.value = null;
    input.value = "";
    return fail("حجم فایل اکسل از ۲۰ مگابایت بیشتر است؛ خروجی را در بازه‌های کوتاه‌تر بگیرید.");
  }
  if (file.value) await read("");
}
async function read(sheetName: string) {
  if (!file.value) return;
  busy.value = true;
  const [d, error] = await previewClockFile(file.value, sheetName || null);
  busy.value = false;
  if (error) {
    preview.value = null;
    return fail(error);
  }
  preview.value = d;
  sheet.value = d?.sheet || "";
  const profile = profiles.value.find((p) => p.id === profileId.value);
  applyMapping(profile ? profile.mapping : d?.suggested);
}
function pickProfile() {
  const profile = profiles.value.find((p) => p.id === profileId.value);
  if (profile) {
    saveAs.value = profile.name;
    applyMapping(profile.mapping);
  }
}
function mappingPayload() {
  const m = { ...map.value, sheet: sheet.value || null };
  if (timeMode.value === "split") m.dateTimeColumn = "";
  else {
    m.dateColumn = "";
    m.timeColumn = "";
  }
  for (const k of Object.keys(m)) if (m[k] === "") m[k] = null;
  m.headerRow = Number(map.value.headerRow) || 0;
  m.firstDataRow = Number(map.value.firstDataRow) || 0;
  return m;
}
function validate(): string | null {
  const m = map.value;
  if (!file.value) return "فایل اکسل را انتخاب کنید.";
  if (!m.codeColumn) return "ستون شماره‌ی پرسنلی را مشخص کنید.";
  if (timeMode.value === "split" && (!m.dateColumn || !m.timeColumn)) return "ستون تاریخ و ستون ساعت را مشخص کنید.";
  if (timeMode.value === "single" && !m.dateTimeColumn) return "ستون تاریخ و ساعت را مشخص کنید.";
  if (!m.dateOrder) return "ترتیب تاریخ (سال-ماه-روز، روز/ماه/سال یا ماه/روز/سال) را انتخاب کنید.";
  return null;
}
async function run() {
  const problem = validate();
  if (problem) return fail(problem);
  busy.value = true;
  const [d, error] = await importClockFile(file.value as File, { mapping: mappingPayload(), sheet: sheet.value || null, saveProfileAs: saveAs.value });
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok(`${d?.imported ?? 0} تردد وارد شد.`);
  if (saveAs.value.trim()) await loadProfiles();
  emit("done");
}
async function saveProfile() {
  const problem = validate();
  if (problem) return fail(problem);
  if (!saveAs.value.trim()) return fail("برای ذخیره‌ی الگو یک نام بنویسید.");
  const existing = profiles.value.find((p) => p.name === saveAs.value.trim());
  const [, error] = await saveClockImportProfile({ id: existing?.id || null, name: saveAs.value.trim(), mapping: mappingPayload() });
  if (error) return fail(error);
  ok("الگوی ستون‌بندی ذخیره شد.");
  await loadProfiles();
  profileId.value = profiles.value.find((p) => p.name === saveAs.value.trim())?.id ?? null;
}
async function deleteProfile() {
  const profile = profiles.value.find((p) => p.id === profileId.value);
  if (!profile || !window.confirm(`الگوی «${profile.name}» حذف شود؟`)) return;
  const [, error] = await saveClockImportProfile({ id: profile.id, name: profile.name, mapping: profile.mapping, delete: true });
  if (error) return fail(error);
  profileId.value = null;
  await loadProfiles();
}
onMounted(loadProfiles);
</script>

<template>
  <div>
    <p class="inv-muted small">
      فایل اکسل (xlsx) خروجی نرم‌افزار حضور و غیاب (PW Kara) را انتخاب کنید و ستون‌ها را به فیلدهای زیر نسبت بدهید؛ نسبت‌دهی را می‌توان با یک نام ذخیره کرد تا در بارگذاری بعدی یک انتخاب کافی باشد.
      تاریخ می‌تواند شمسی یا میلادی باشد. شماره‌ی پرسنلی که در پرونده‌ی پرسنل نباشد فقط گزارش می‌شود و پرسنل جدیدی ساخته نمی‌شود. بارگذاری دوباره‌ی همان فایل چیزی را دوبار ثبت نمی‌کند.
    </p>
    <div class="row g-2 align-items-end mb-2">
      <div class="col-md-5"><label class="form-label small mb-0">فایل اکسل</label><input :disabled="busy" accept=".xlsx" class="form-control form-control-sm" type="file" @change="choose" /></div>
      <div class="col-md-4">
        <label class="form-label small mb-0">الگوی ذخیره‌شده</label>
        <select v-model="profileId" class="form-select form-select-sm" @change="pickProfile"><option :value="null">بدون الگو</option><option v-for="p in profiles" :key="p.id" :value="p.id">{{ p.name }}</option></select>
      </div>
      <div class="col-md-3"><button v-if="profileId" class="btn btn-sm btn-link text-danger" @click="deleteProfile">حذف این الگو</button></div>
    </div>

    <template v-if="preview">
      <div v-if="preview.sheets?.length > 1" class="mb-2">
        <label class="form-label small mb-0">برگه</label>
        <select :value="sheet" class="form-select form-select-sm" style="max-width: 16rem" @change="read(($event.target as HTMLSelectElement).value)"><option v-for="s in preview.sheets" :key="s" :value="s">{{ s }}</option></select>
      </div>
      <div class="table-responsive mb-2" style="max-height: 200px; overflow: auto">
        <table class="table table-sm small">
          <thead><tr><th v-for="c in columns" :key="c.letter" class="text-nowrap"><span class="inv-ltr">{{ c.letter }}</span> {{ c.header }}</th></tr></thead>
          <tbody><tr v-for="(r, i) in (preview.rows || []).slice(0, 6)" :key="i"><td v-for="(c, k) in columns" :key="k" class="text-nowrap">{{ r[k] }}</td></tr></tbody>
        </table>
      </div>
      <div class="small inv-muted mb-2">{{ preview.rowCount }} ردیف در برگه.</div>
      <div v-for="c in columns.filter((x) => (x.sampleDates || []).some((d: string) => d))" :key="'sd' + c.letter" class="small mb-1">
        نمونه‌ی تاریخ ستون <span class="inv-ltr">{{ c.letter }}</span> به شمسی:
        <span v-for="(d, i) in c.sampleDates" :key="i" :class="d === 'مبهم' ? 'text-danger' : ''" class="me-2 inv-ltr" dir="ltr">{{ d || "-" }}</span>
      </div>

      <div class="row g-2 mb-2">
        <div class="col-md-3"><label class="form-label small mb-0">ردیف عنوان ستون‌ها</label><input v-model.number="map.headerRow" class="form-control form-control-sm fn-num" /><div class="inv-muted small">۰ = بدون ردیف عنوان</div></div>
        <div class="col-md-3"><label class="form-label small mb-0">اولین ردیف داده</label><input v-model.number="map.firstDataRow" class="form-control form-control-sm fn-num" /><div class="inv-muted small">۰ = ردیف بعد از عنوان</div></div>
        <div class="col-md-6 d-flex align-items-end gap-3">
          <label class="small"><input v-model="timeMode" class="me-1" type="radio" value="split" />تاریخ و ساعت جدا</label>
          <label class="small"><input v-model="timeMode" class="me-1" type="radio" value="single" />تاریخ و ساعت در یک ستون</label>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">ترتیب تاریخ *</label>
          <select v-model="map.dateOrder" class="form-select form-select-sm"><option value="">انتخاب کنید</option><option value="YMD">سال-ماه-روز (۱۴۰۵/۰۷/۱۱ یا 2026-10-03)</option><option value="DMY">روز/ماه/سال (03/10/2026)</option><option value="MDY">ماه/روز/سال (10/03/2026)</option></select>
          <div class="inv-muted small">تاریخ سال‌اولی هرگز مبهم نیست؛ تاریخ سال‌آخری مثل 03/10/2026 بدون این انتخاب پذیرفته نمی‌شود و کل بارگذاری رد می‌شود.</div>
        </div>
        <div v-for="f in fieldRows" :key="f.key" class="col-md-4">
          <label class="form-label small mb-0">{{ f.label }}</label>
          <select v-model="map[f.key]" class="form-select form-select-sm"><option value="">-</option><option v-for="c in columns" :key="c.letter" :value="c.letter">{{ c.letter }} - {{ c.header || "(بدون عنوان)" }}</option></select>
        </div>
        <template v-if="map.directionColumn">
          <div class="col-md-4"><label class="form-label small mb-0">متن‌های «ورود» (با کاما)</label><input v-model="map.inValues" class="form-control form-control-sm" placeholder="خالی = ورود، in" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">متن‌های «خروج» (با کاما)</label><input v-model="map.outValues" class="form-control form-control-sm" placeholder="خالی = خروج، out" /></div>
        </template>
        <div v-if="!map.deviceColumn && !map.siteColumn" class="col-md-4"><label class="form-label small mb-0">کد دستگاه برای همه‌ی ردیف‌ها</label><input v-model="map.defaultDeviceCode" class="form-control form-control-sm inv-ltr" placeholder="PWKARA" /></div>
      </div>

      <div class="d-flex align-items-end gap-2 flex-wrap mb-2">
        <div><label class="form-label small mb-0">ذخیره‌ی ستون‌بندی با نام</label><input v-model="saveAs" class="form-control form-control-sm" maxlength="200" placeholder="مثلا PW Kara - دفتر مرکزی" style="min-width: 16rem" /></div>
        <button :disabled="busy || !saveAs.trim()" class="btn btn-sm btn-outline-secondary" @click="saveProfile">فقط ذخیره‌ی الگو</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="run">ورود ترددها{{ saveAs.trim() ? " و ذخیره‌ی الگو" : "" }}</button>
      </div>
    </template>

    <div v-if="result" class="mb-2">
      <span class="inv-badge ok me-1">وارد شده: {{ result.imported }}</span>
      <span class="inv-badge info me-1">تکراری: {{ result.duplicates }}</span>
      <span :class="result.unknown ? 'warn' : 'ok'" class="inv-badge me-1">شماره‌ی ناشناس: {{ result.unknown }}</span>
      <span :class="result.rejected ? 'bad' : 'ok'" class="inv-badge me-1">ردشده: {{ result.rejected }}</span>
      <span class="inv-badge info">ردیف خوانده‌شده: {{ result.rowsRead }}</span>
      <div v-if="result.unknownCodes?.length" class="small mt-2"><b>شماره‌های پرسنلی که در پرونده‌ی پرسنل نیستند (ساخته نشدند):</b> <span class="inv-ltr" dir="ltr">{{ result.unknownCodes.join(" ، ") }}</span></div>
      <ul v-if="result.errors?.length" class="small mt-2" style="max-height: 180px; overflow: auto"><li v-for="(e, i) in result.errors" :key="i">{{ e }}</li></ul>
    </div>
  </div>
</template>
