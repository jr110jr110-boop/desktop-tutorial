<script setup lang="ts">
// Legacy archive import (SECRETARIAT only): paste a CSV manifest or fill a table of rows. Numbers are kept, the sequence is not used.
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ArchiveFilesPanel from "./ArchiveFilesPanel.vue";
import { importLetters } from "@/api/OfficeAPIs";
import { uploadFile } from "@/api/InventoryAPIs";
import { letterKinds } from "@/composables/office";
import { fail, ok } from "@/composables/inventory";

interface Row { number: string; date: string | null; kind: string; subject: string; party: string; fileNames: string[]; documentIds: string[] }
const mode = ref<"csv" | "rows" | "pdf">("csv");
const csv = ref("");
const rows = ref<Row[]>([blank()]);
const result = ref<any>(null);
const busy = ref(false);

function blank(): Row {
  return { number: "", date: null, kind: letterKinds[2], subject: "", party: "", fileNames: [], documentIds: [] };
}
async function onFiles(row: Row, event: any) {
  const files: File[] = Array.from(event.target.files || []);
  event.target.value = "";
  busy.value = true;
  for (const f of files) {
    const [id, error] = await uploadFile(f, f.name);
    if (error || !id) {
      fail(error || "بارگذاری فایل انجام نشد.");
      continue;
    }
    row.fileNames.push(f.name);
    row.documentIds.push(id as string);
  }
  busy.value = false;
}
async function run() {
  let dto: { rows?: any[]; csv?: string | null };
  if (mode.value === "csv") {
    if (!csv.value.trim()) return fail("متن CSV را وارد کنید.");
    dto = { csv: csv.value };
  } else {
    const used = rows.value.filter((r) => r.number.trim() || r.subject.trim());
    if (!used.length) return fail("حداقل یک ردیف را پر کنید.");
    if (used.some((r) => !r.number.trim() || !r.date)) return fail("شماره و تاریخ همه ردیف‌ها الزامی است.");
    dto = { rows: used.map((r) => ({ number: r.number.trim(), date: r.date, kind: r.kind, subject: r.subject.trim(), party: r.party.trim(), fileNames: r.fileNames, documentIds: r.documentIds })) };
  }
  busy.value = true;
  const [data, error] = await importLetters(dto);
  busy.value = false;
  if (error) return fail(error);
  result.value = data;
  ok(`${data?.imported ?? 0} نامه وارد شد.`);
  if (!data?.errors?.length && mode.value === "rows") rows.value = [blank()];
}
</script>

<template>
  <div>
    <div class="alert alert-info py-2">
      نامه‌های آرشیو قدیم با همان شماره قبلی ثبت می‌شوند و شماره‌گذاری جدید را مصرف نمی‌کنند. شماره تکراری رد می‌شود. فقط دبیرخانه می‌تواند این کار را انجام دهد.
    </div>
    <div class="btn-group btn-group-sm mb-3">
      <button :class="mode === 'csv' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="mode = 'csv'">چسباندن CSV</button>
      <button :class="mode === 'rows' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="mode = 'rows'">جدول ردیف‌ها</button>
      <button :class="mode === 'pdf' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="mode = 'pdf'">فایل‌های PDF راهکاران</button>
    </div>

    <ArchiveFilesPanel v-if="mode === 'pdf'" />

    <template v-else-if="mode === 'csv'">
      <p class="small inv-muted">هر سطر: شماره، تاریخ (شمسی مانند ۱۴۰۳/۰۵/۱۷)، نوع (وارده، صادره، داخلی)، موضوع، طرف مکاتبه، نام فایل‌ها (جدا شده با | ). فایل‌ها را در حالت «جدول ردیف‌ها» می‌توان بارگذاری کرد.</p>
      <textarea v-model="csv" class="form-control inv-ltr" dir="ltr" placeholder="12/ص/1/00045, 1403/05/17, صادره, موضوع نامه, نام طرف مکاتبه, scan1.pdf|scan2.pdf" rows="8"></textarea>
    </template>

    <div v-else-if="mode === 'rows'" class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>شماره *</th><th>تاریخ *</th><th>نوع</th><th>موضوع</th><th>طرف مکاتبه</th><th>فایل‌ها</th><th></th></tr></thead>
        <tbody>
          <tr v-for="(r, i) in rows" :key="i">
            <td><input v-model="r.number" class="form-control form-control-sm inv-ltr" /></td>
            <td style="min-width: 8rem"><FnDate v-model="r.date" /></td>
            <td><select v-model="r.kind" class="form-select form-select-sm"><option v-for="(t, k) in letterKinds" :key="k" :value="t">{{ t }}</option></select></td>
            <td><input v-model="r.subject" class="form-control form-control-sm" /></td>
            <td><input v-model="r.party" class="form-control form-control-sm" /></td>
            <td>
              <div v-for="(n, k) in r.fileNames" :key="k" class="small">{{ n }}</div>
              <label class="btn btn-sm btn-outline-secondary mb-0">فایل<input class="d-none" multiple type="file" @change="onFiles(r, $event)" /></label>
            </td>
            <td><button class="btn btn-sm btn-link text-danger" @click="rows.splice(i, 1)">حذف</button></td>
          </tr>
        </tbody>
      </table>
      <button class="btn btn-sm btn-outline-secondary" @click="rows.push(blank())">ردیف جدید</button>
    </div>

    <div v-if="mode !== 'pdf'" class="mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="run">ورود به آرشیو</button></div>

    <div v-if="result && mode !== 'pdf'" class="alert mt-3" :class="result.errors && result.errors.length ? 'alert-warning' : 'alert-success'">
      واردشده: <b>{{ result.imported }}</b>، ردشده: <b>{{ result.skipped }}</b>
      <ul v-if="result.errors && result.errors.length" class="mb-0 mt-1"><li v-for="(e, i) in result.errors" :key="i">{{ e }}</li></ul>
    </div>
  </div>
</template>
