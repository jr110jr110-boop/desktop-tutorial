<script setup lang="ts">
// The PDFs of the Rahkaran archive export: one PDF per imported letter, matched by file name (the old number without the extension, or the group «number» of a pattern).
// The answer lists what was attached, the files nothing matched, and the imported letters that still have no file.
import { computed, ref } from "vue";
import { importLetterFiles } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";

const files = ref<File[]>([]);
const namePattern = ref("");
const csv = ref("");
const result = ref<any>(null);
const busy = ref(false);
const resultTitles: Record<string, string> = { attached: "پیوست شد", "already-attached": "قبلاً پیوست شده", unmatched: "نامه‌ای پیدا نشد", ambiguous: "چند نامه با این شماره", "not-pdf": "PDF نیست", invalid: "نامعتبر" };
const resultBadge = (r: string) => (r === "attached" ? "ok" : r === "already-attached" ? "info" : "bad");
const sizeText = computed(() => `${(files.value.reduce((a, f) => a + f.size, 0) / 1048576).toFixed(1)} مگابایت`);

function pick(event: Event) {
  const input = event.target as HTMLInputElement;
  files.value = Array.from(input.files || []);
  result.value = null;
}
async function run() {
  if (!files.value.length) return fail("فایل‌های PDF را انتخاب کنید.");
  if (files.value.length > 300) return fail("در هر بار حداکثر ۳۰۰ فایل مجاز است؛ فایل‌ها را در چند نوبت بفرستید.");
  if (namePattern.value.trim()) {
    try {
      new RegExp(namePattern.value);
    } catch {
      return fail("عبارت منظم نام فایل معتبر نیست.");
    }
  }
  busy.value = true;
  const [data, error] = await importLetterFiles(files.value, { namePattern: namePattern.value, csv: csv.value });
  busy.value = false;
  if (error) return fail(error);
  result.value = data;
  ok(`${data?.attached ?? 0} فایل به نامه‌ها پیوست شد.`);
}
</script>

<template>
  <div>
    <div class="alert alert-info py-2">
      برای هر نامه‌ی واردشده از آرشیو راهکاران یک فایل PDF بفرستید. فایل با نامش به نامه وصل می‌شود: نام فایل بدون پسوند باید با شماره‌ی قدیمی نامه برابر باشد
      (نویسه‌هایی که در نام فایل نمی‌آیند، مانند / و \ و :، با <code>_</code> جایگزین می‌شوند؛ مثلا <code dir="ltr">1399_OUT_45.pdf</code> برای نامه‌ی <code dir="ltr">1399/OUT/45</code>).
      فایلی که نامه‌ای برایش نیست یا PDF نیست رد و گزارش می‌شود، و فهرست نامه‌های بدون فایل در پایان می‌آید. ارسال دوباره‌ی همان فایل چیزی را دوبار پیوست نمی‌کند.
    </div>
    <div class="row g-3 mb-2">
      <div class="col-md-6">
        <label class="form-label small mb-0">فایل‌های PDF (حداکثر ۳۰۰ فایل، هر کدام تا ۵۰ مگابایت)</label>
        <input :disabled="busy" accept=".pdf,application/pdf" class="form-control form-control-sm" multiple type="file" @change="pick" />
        <div v-if="files.length" class="small mt-1">{{ files.length }} فایل، {{ sizeText }}</div>
      </div>
      <div class="col-md-6">
        <label class="form-label small mb-0">عبارت منظم برای جدا کردن شماره از نام فایل (اختیاری)</label>
        <input v-model="namePattern" class="form-control form-control-sm inv-ltr" dir="ltr" placeholder="مثلا ^Letter-(?<number>.+)$ ؛ خالی = کل نام بدون پسوند" />
        <div class="small inv-muted">شماره از گروه «number» یا گروه اول خوانده می‌شود.</div>
      </div>
      <div class="col-12">
        <label class="form-label small mb-0">فهرست نامه‌ها (CSV، اختیاری)؛ اگر نامه‌ها هنوز وارد نشده‌اند، پیش از پیوست کردن وارد می‌شوند</label>
        <textarea v-model="csv" class="form-control form-control-sm inv-ltr" dir="ltr" placeholder="شماره, تاریخ, نوع, موضوع, طرف مکاتبه" rows="3"></textarea>
      </div>
    </div>
    <button :disabled="busy || !files.length" class="btn btn-sm btn-primary" @click="run">{{ busy ? "در حال ارسال..." : "پیوست فایل‌ها" }}</button>

    <div v-if="result" class="mt-3">
      <div class="mb-2">
        <span class="inv-badge ok me-1">پیوست‌شده: {{ result.attached }}</span>
        <span class="inv-badge info me-1">قبلاً پیوست‌شده: {{ result.alreadyAttached }}</span>
        <span :class="result.unmatched ? 'bad' : 'ok'" class="inv-badge me-1">فایل بدون نامه: {{ result.unmatched }}</span>
        <span :class="result.lettersWithoutFileCount ? 'warn' : 'ok'" class="inv-badge">نامه‌ی بدون فایل: {{ result.lettersWithoutFileCount }}</span>
      </div>
      <div v-if="result.import" class="small mb-2">فهرست CSV: واردشده {{ result.import.imported }}، ردشده {{ result.import.skipped }}<span v-if="result.import.errors?.length"> ({{ result.import.errors.join("؛ ") }})</span></div>
      <div class="table-responsive" style="max-height: 320px; overflow: auto">
        <table class="table table-sm">
          <thead><tr><th>فایل</th><th>شماره‌ی خوانده‌شده</th><th>نتیجه</th></tr></thead>
          <tbody>
            <tr v-for="(f, i) in result.files" :key="i">
              <td class="inv-ltr">{{ f.fileName }}</td>
              <td class="inv-ltr">{{ f.number }}</td>
              <td><span :class="resultBadge(f.result)" class="inv-badge">{{ resultTitles[f.result] || f.result }}</span> <span v-if="f.message" class="small inv-muted">{{ f.message }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="result.lettersWithoutFile?.length" class="mt-2">
        <b class="small">نامه‌های واردشده‌ی بدون فایل<span v-if="result.lettersWithoutFileCount > result.lettersWithoutFile.length"> (فقط {{ result.lettersWithoutFile.length }} مورد اول از {{ result.lettersWithoutFileCount }})</span>:</b>
        <div class="small inv-ltr" dir="ltr" style="max-height: 160px; overflow: auto">{{ result.lettersWithoutFile.join(" ، ") }}</div>
      </div>
    </div>
  </div>
</template>
