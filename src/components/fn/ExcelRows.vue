<script setup lang="ts">
// Reads an Excel / CSV file into rows for an import: the columns are found by their header words (see fnExcel.ts), dates are Persian text in the file and yyyy-mm-dd in the rows.
// The rows are handed to the page with the event "rows"; the first rows are shown as a preview.
import { ref } from "vue";
import { cellDate, cellNumber, cellText, readExcel, type ExcelField } from "@/composables/fnExcel";
import { fail, jalali } from "@/composables/inventory";
import { money } from "@/composables/fn";

export interface ImportField extends ExcelField {
  type?: "text" | "number" | "date";
}

const props = defineProps<{ fields: ImportField[]; hint?: string; disabled?: boolean }>();
const emit = defineEmits(["rows"]);
const rows = ref<Record<string, any>[]>([]);
const fileName = ref("");

async function onFile(event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  try {
    const sheet = await readExcel(file, props.fields);
    const missing = props.fields.filter((f) => f.required && sheet.map[f.key] < 0);
    if (missing.length) return fail(`ستون «${missing.map((m) => m.label).join("، ")}» در فایل پیدا نشد.`);
    rows.value = sheet.rows.map((r) => {
      const o: Record<string, any> = {};
      for (const f of props.fields) {
        if (f.type === "number") o[f.key] = cellNumber(sheet, r, f.key);
        else if (f.type === "date") o[f.key] = cellDate(sheet, r, f.key);
        else o[f.key] = cellText(sheet, r, f.key);
      }
      return o;
    }).filter((o) => Object.values(o).some((v) => v !== null && v !== ""));
    fileName.value = file.name;
    emit("rows", rows.value);
  } catch (e: any) {
    fail("فایل خوانده نشد. فایل باید xlsx یا csv باشد. " + (e?.message || ""));
  }
}

function clear() {
  rows.value = [];
  fileName.value = "";
  emit("rows", []);
}
const show = (f: ImportField, v: any) => (v === null || v === undefined ? "" : f.type === "date" ? jalali(v) : f.type === "number" ? money(v) : v);
</script>

<template>
  <div class="mb-2">
    <div class="d-flex align-items-center gap-2 flex-wrap">
      <input :disabled="disabled" accept=".xlsx,.xls,.csv" class="form-control form-control-sm" style="max-width: 300px" type="file" @change="onFile" />
      <span v-if="fileName" class="inv-muted">{{ fileName }} - {{ rows.length }} ردیف</span>
      <button v-if="rows.length" class="btn btn-sm btn-outline-secondary" @click="clear">پاک‌کردن فایل</button>
    </div>
    <p v-if="hint" class="inv-muted mb-1">{{ hint }}</p>
    <p class="inv-muted mb-1">ستون‌های فایل: {{ fields.map((f) => f.label + (f.required ? " *" : "")).join("، ") }}. تاریخ‌ها شمسی (مثل ۱۴۰۵/۰۷/۱۵).</p>
    <div v-if="rows.length" class="table-responsive" style="max-height: 220px; overflow: auto">
      <table class="table table-sm">
        <thead><tr><th v-for="f in fields" :key="f.key">{{ f.label }}</th></tr></thead>
        <tbody>
          <tr v-for="(r, i) in rows.slice(0, 8)" :key="i"><td v-for="f in fields" :key="f.key" :class="{ 'fn-num': f.type === 'number' }">{{ show(f, r[f.key]) }}</td></tr>
          <tr v-if="rows.length > 8"><td :colspan="fields.length" class="inv-muted">و {{ rows.length - 8 }} ردیف دیگر...</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
