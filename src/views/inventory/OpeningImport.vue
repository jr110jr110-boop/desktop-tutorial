<script setup lang="ts">
// Opening balance from the cleaned old warehouse data (Excel). The API makes one draft per warehouse; the drafts need
// the approval of the warehouse supervisor and the planning manager before they are confirmed.
import { computed, ref } from "vue";
import * as XLSX from "xlsx";
import { importOpening } from "@/api/InventoryAPIs";
import { fail, ok, today } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

// the fields of OpeningImportRowDto and the header words that are recognised automatically
const fields = [
  { key: "itemCode", label: "کد کالا (جدید یا قدیمی) *", words: ["کد کالا", "کد قطعه", "کد", "code", "itemcode", "item code", "کد قدیم", "کد قدیمی"] },
  { key: "warehouseCode", label: "کد انبار *", words: ["کد انبار", "انبار", "warehouse"] },
  { key: "locationCode", label: "محل", words: ["محل", "کد محل", "location", "قفسه"] },
  { key: "quantity", label: "مقدار *", words: ["مقدار", "تعداد", "موجودی", "quantity", "qty"] },
  { key: "lotNumber", label: "بچ", words: ["بچ", "شماره بچ", "lot", "batch"] },
  { key: "heatNumber", label: "ذوب (Heat)", words: ["ذوب", "heat"] },
  { key: "certificateNumber", label: "گواهی (MTC)", words: ["گواهی", "mtc", "certificate"] },
  { key: "labelNumber", label: "شماره برچسب (YYDDDNNNN)", words: ["برچسب", "شماره برچسب", "label"] },
  { key: "serialNumbers", label: "سریال‌ها", words: ["سریال", "سریال‌ها", "serial", "s/n"] },
  { key: "unitPrice", label: "فی (ریال) *", words: ["فی", "قیمت", "قیمت واحد", "price", "unit price"] },
  { key: "description", label: "توضیح", words: ["توضیح", "توضیحات", "description", "note"] },
];

const state = ref<any>(null);
const header = ref({ documentDate: today(), reference: "", createLocations: true, importValidRows: false, allowZeroPrice: false });
const busy = ref(false);
const result = ref<any>(null);
// R1q D35: every row needs a unit price (stock without a price is valued at zero in the books); only the finance manager may let rows come in without one, knowingly
const canAllowZeroPrice = hasRole(InventoryRoles.zeroPriceAllowers);

const mapped = computed(() => (state.value ? fields.filter((f) => state.value.map[f.key] >= 0) : []));
// rows with a quantity but no usable price (the API lists each of them as an error unless the finance manager allows zero prices)
const unpricedRows = computed(() => {
  if (!state.value) return [] as number[];
  const out: number[] = [];
  state.value.rows.forEach((r: any[], i: number) => {
    const code = cell(r, "itemCode");
    const warehouse = cell(r, "warehouseCode");
    if (code === null && warehouse === null) return;
    const quantity = toNumber(cell(r, "quantity")) ?? 0;
    const serials = (cell(r, "serialNumbers") || "").trim();
    // a serial item takes its quantity from the serial list, so a row with serials counts too
    if (quantity <= 0 && !serials) return;
    const price = toNumber(cell(r, "unitPrice"));
    if (price === null || price <= 0) out.push(i + 2);
  });
  return out;
});

function normalize(text: any): string {
  return String(text ?? "").trim().toLowerCase().replace(/ي/g, "ی").replace(/ك/g, "ک").replace(/\s+/g, " ");
}

/** Persian and Arabic digits to Latin, so that a number typed on a Persian keyboard is read. */
function latin(text: any): string {
  return String(text ?? "")
    .replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d)))
    .replace(/[٠-٩]/g, (d) => String("٠١٢٣٤٥٦٧٨٩".indexOf(d)));
}

async function readFile(event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  result.value = null;
  try {
    const book = XLSX.read(await file.arrayBuffer(), { type: "array" });
    const sheet = book.Sheets[book.SheetNames[0]];
    // raw: false keeps the cells as they are shown, so a code with leading zeros stays a text
    const rows: any[][] = XLSX.utils.sheet_to_json(sheet, { header: 1, raw: false, defval: null, blankrows: false });
    if (rows.length < 2) return fail("فایل ردیفی ندارد.");
    const headers = rows[0].map((h) => String(h ?? "").trim());
    const map: Record<string, number> = {};
    fields.forEach((f) => {
      let index = headers.findIndex((h) => f.words.includes(normalize(h)));
      if (index < 0) index = headers.findIndex((h) => f.words.some((w) => normalize(h).includes(w)));
      map[f.key] = index;
    });
    state.value = { fileName: file.name, headers, rows: rows.slice(1), map };
  } catch (e: any) {
    fail("فایل خوانده نشد. فایل باید xlsx باشد. " + (e?.message || ""));
  }
}

function cell(row: any[], key: string): string | null {
  const index = state.value.map[key];
  if (index === undefined || index < 0) return null;
  const v = row[index];
  return v === null || v === undefined || String(v).trim() === "" ? null : String(v).trim();
}

function toNumber(text: string | null): number | null {
  if (text === null) return null;
  const n = Number(latin(text).replace(/[,٬\s]/g, ""));
  return isNaN(n) ? null : n;
}

function buildRows() {
  return state.value.rows.map((r: any[]) => ({
    itemCode: latin(cell(r, "itemCode")) || null,
    warehouseCode: cell(r, "warehouseCode"),
    locationCode: cell(r, "locationCode"),
    quantity: toNumber(cell(r, "quantity")) ?? 0,
    lotNumber: cell(r, "lotNumber"),
    heatNumber: cell(r, "heatNumber"),
    certificateNumber: cell(r, "certificateNumber"),
    labelNumber: latin(cell(r, "labelNumber")) || null,
    serialNumbers: (cell(r, "serialNumbers") || "")
      .split(/[\n,،;؛]+/)
      .map((x) => x.trim())
      .filter(Boolean),
    unitPrice: toNumber(cell(r, "unitPrice")),
    description: cell(r, "description"),
  }));
}

async function run() {
  if (state.value.map.itemCode < 0 || state.value.map.warehouseCode < 0 || state.value.map.quantity < 0)
    return fail("ستون کد کالا، کد انبار و مقدار را مشخص کنید.");
  const allowZeroPrice = canAllowZeroPrice && header.value.allowZeroPrice;
  if (state.value.map.unitPrice < 0 && !allowZeroPrice)
    return fail("ستون فی (قیمت واحد) را مشخص کنید؛ موجودی اول دوره بدون قیمت در دفتر با ارزش صفر ثبت می‌شود و فقط مدیر مالی می‌تواند آن را صریحاً مجاز کند.");
  busy.value = true;
  const [data, error] = await importOpening({
    documentDate: header.value.documentDate,
    reference: header.value.reference,
    createLocations: header.value.createLocations,
    importValidRows: header.value.importValidRows,
    allowZeroPrice,
    rows: buildRows(),
  });
  busy.value = false;
  if (error) return fail(error);
  result.value = data;
  if (data.documentIds?.length) ok(`${data.documentIds.length} پیش‌نویس موجودی اول دوره ساخته شد (${data.lines} ردیف).`);
  else if (data.errors?.length) fail("ورود انجام نشد؛ خطاها را اصلاح کنید یا فقط ردیف‌های درست را وارد کنید.");
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">ورود موجودی اول دوره از اکسل</h1>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'InventoryDocuments' }" class="btn btn-sm btn-outline-secondary">اسناد انبار</router-link>
          <label class="btn btn-sm btn-outline-primary mb-0">
            انتخاب فایل xlsx
            <input accept=".xlsx,.xls" class="d-none" type="file" @change="readFile" />
          </label>
        </div>
      </div>
      <p class="inv-muted mb-0">
        برای هر انبار یک سند «موجودی اول دوره» پیش‌نویس ساخته می‌شود که پیش از تایید، تایید سرپرست انبار و مدیر برنامه‌ریزی و کنترل پروژه را لازم دارد.
        کد کالا می‌تواند کد جدید یا کد قدیمی کالا باشد. سطر اول فایل عنوان ستون‌هاست.
      </p>
    </div>

    <div v-if="state" class="inv-card">
      <p class="small mb-2">فایل: {{ state.fileName }} — {{ state.rows.length }} ردیف. ستون هر فیلد را بررسی کنید.</p>
      <div class="row g-2">
        <div v-for="f in fields" :key="f.key" class="col-md-3">
          <label class="form-label small mb-0">{{ f.label }}</label>
          <select v-model="state.map[f.key]" class="form-select form-select-sm">
            <option :value="-1">— ندارد —</option>
            <option v-for="(h, i) in state.headers" :key="i" :value="i">{{ h || "ستون " + (i + 1) }}</option>
          </select>
        </div>
      </div>

      <div class="table-responsive mt-3" style="max-height: 220px">
        <table class="table table-sm table-bordered">
          <thead>
            <tr><th v-for="f in mapped" :key="f.key">{{ f.label }}</th></tr>
          </thead>
          <tbody>
            <tr v-for="(r, i) in state.rows.slice(0, 5)" :key="i">
              <td v-for="f in mapped" :key="f.key">{{ r[state.map[f.key]] }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="unpricedRows.length" class="alert py-2 small mt-2 mb-0" :class="header.allowZeroPrice && canAllowZeroPrice ? 'alert-warning' : 'alert-danger'">
        {{ unpricedRows.length }} ردیف فی (قیمت واحد) ندارد (ردیف
        <span class="inv-ltr">{{ unpricedRows.slice(0, 15).join("، ") }}</span><span v-if="unpricedRows.length > 15"> و …</span>).
        <template v-if="header.allowZeroPrice && canAllowZeroPrice">با مجوز مدیر مالی این ردیف‌ها با ارزش صفر وارد و در سابقه ممیزی ثبت می‌شوند.</template>
        <template v-else>
          موجودی اول دوره بدون قیمت در دفتر با ارزش صفر ثبت می‌شود؛ این ردیف‌ها خطا می‌گیرند و وارد نمی‌شوند. قیمت را در فایل بنویسید<span v-if="!canAllowZeroPrice"> یا از مدیر مالی بخواهید ورود را انجام دهد</span>.
        </template>
      </div>

      <div class="row g-2 mt-1 align-items-end">
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ سند</label>
          <input v-model="header.documentDate" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">مرجع (مثلاً شماره انبارگردانی)</label>
          <input v-model="header.reference" class="form-control form-control-sm" />
        </div>
        <div class="col-md-3">
          <label class="form-check small mb-0">
            <input v-model="header.createLocations" class="form-check-input" type="checkbox" />
            محل‌هایی که وجود ندارند ساخته شوند
          </label>
          <label class="form-check small mb-0">
            <input v-model="header.importValidRows" class="form-check-input" type="checkbox" />
            اگر بعضی ردیف‌ها خطا دارند، ردیف‌های درست وارد شوند
          </label>
          <label v-if="canAllowZeroPrice" class="form-check small mb-0">
            <input v-model="header.allowZeroPrice" class="form-check-input" type="checkbox" />
            ورود ردیف‌های بدون قیمت (ارزش صفر) را مجاز می‌کنم
          </label>
        </div>
        <div class="col-md-4 text-end">
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="run">ورود موجودی</button>
        </div>
      </div>
    </div>

    <div v-if="result" class="inv-card">
      <h2 class="inv-title mb-2">نتیجه</h2>
      <div class="d-flex flex-wrap gap-2 mb-2">
        <span class="inv-badge ok">{{ result.lines }} ردیف وارد شد</span>
        <span class="inv-badge">{{ result.locationsCreated }} محل جدید</span>
        <span class="inv-badge" :class="{ warn: result.skipped }">{{ result.skipped }} ردیف رد شد</span>
        <span class="inv-badge" :class="{ bad: result.errors?.length }">{{ result.errors?.length || 0 }} خطا</span>
      </div>
      <p v-if="result.documentIds?.length" class="small">
        پیش‌نویس‌های ساخته‌شده:
        <router-link v-for="id in result.documentIds" :key="id" :to="{ name: 'InventoryDocumentEdit', params: { id } }" class="me-2">سند {{ id }}</router-link>
        — هر کدام را باز کنید و تایید سرپرست انبار و برنامه‌ریزی را ثبت کنید.
      </p>
      <div v-if="result.zeroPricedLines?.length" class="alert alert-warning small">
        <b>{{ result.zeroPricedLines.length }} ردیف بدون قیمت با ارزش صفر وارد شد</b> (مجوز مدیر مالی؛ در سابقه ممیزی ثبت شد). پیش از تایید اسناد، قیمت این ردیف‌ها را در پیش‌نویس کامل کنید:
        <ul class="mb-0 mt-1" style="max-height: 220px; overflow-y: auto">
          <li v-for="(l, i) in result.zeroPricedLines" :key="i">{{ l }}</li>
        </ul>
      </div>
      <div v-if="result.errors?.length" class="table-responsive" style="max-height: 360px; overflow-y: auto">
        <h3 class="inv-title mb-1" style="font-size: 1rem">خطاهای ردیف‌ها (شماره ردیف = شماره سطر فایل، سطر اول عنوان است)</h3>
        <table class="table table-sm table-bordered">
          <thead><tr><th style="width: 3rem">#</th><th>خطا</th></tr></thead>
          <tbody>
            <tr v-for="(e, i) in result.errors" :key="i">
              <td>{{ i + 1 }}</td>
              <td>{{ e }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
