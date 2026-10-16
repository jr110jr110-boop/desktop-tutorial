<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import readXlsxFile from "read-excel-file";
import CodeCheck from "@/components/inventory/CodeCheck.vue";
import { deleteItem, getGroups, getItems, importItems, saveItem } from "@/api/InventoryAPIs";
import { fail, labelCode, num, ok, openLabels, trackingModes } from "@/composables/inventory";

const router = useRouter();
const items = ref<any[]>([]);
const groups = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 30;
const loading = ref(false);
const filters = ref({ Contains: "", ItemGroupId: null as number | null, TrackingMode: null as number | null, IsActive: null as boolean | null });
const selected = ref<number[]>([]);

const emptyForm = () => ({
  id: null, code: "", title: "", itemGroupId: null, unit: "عدد", trackingMode: 0, technicalCode: "", material: "",
  standard: "", specification: "", weight: null, minStock: 0, maxStock: null, isActive: true, sepidarCode: "",
  mescCode: "", legacyCode: "", requiresIncomingInspection: true, shelfLifeDays: null,
});
const form = ref<any>(null);
const saving = ref(false);

async function load() {
  loading.value = true;
  const [data, error] = await getItems({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  loading.value = false;
  if (error) return fail(error);
  items.value = data.data;
  total.value = data.totalItems;
}

async function loadGroups() {
  const [data, error] = await getGroups();
  if (error) return fail(error);
  groups.value = data;
}

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

function search() {
  page.value = 1;
  load();
}

function edit(item: any) {
  form.value = { ...emptyForm(), ...item };
}

async function save() {
  saving.value = true;
  const [, error] = await saveItem(form.value);
  saving.value = false;
  if (error) return fail(error);
  ok("کالا ذخیره شد.");
  form.value = null;
  load();
}

async function remove(item: any) {
  if (!confirm(`کالای ${item.code} حذف شود؟`)) return;
  const [, error] = await deleteItem(item.id);
  if (error) return fail(error);
  ok("حذف شد.");
  load();
}

function toggle(id: number) {
  const i = selected.value.indexOf(id);
  if (i >= 0) selected.value.splice(i, 1);
  else selected.value.push(id);
}

function printLabels() {
  const chosen = items.value.filter((i) => selected.value.includes(i.id));
  if (!chosen.length) return fail("ابتدا کالاها را انتخاب کنید.");
  openLabels(
    chosen.map((i) => ({ code: labelCode.item(i.code), title: i.title, lines: [i.code, i.technicalCode || ""].filter(Boolean) })),
    router
  );
}

// ---------------------------------------------------------------------------- Excel import
const fields = [
  { key: "code", label: "کد کالا *", words: ["کد کالا", "کد قطعه", "کد", "code", "itemcode", "item code"] },
  { key: "title", label: "شرح کالا *", words: ["شرح کالا", "نام کالا", "شرح", "نام", "عنوان", "title", "name", "description"] },
  { key: "groupCode", label: "کد گروه", words: ["کد گروه", "گروه", "group code", "group"] },
  { key: "groupTitle", label: "نام گروه", words: ["نام گروه", "شرح گروه", "group title", "group name"] },
  { key: "unit", label: "واحد", words: ["واحد", "واحد سنجش", "unit"] },
  { key: "trackingMode", label: "نوع ردیابی", words: ["نوع ردیابی", "ردیابی", "tracking"] },
  { key: "technicalCode", label: "شماره نقشه / فنی", words: ["شماره نقشه", "کد فنی", "شماره فنی", "part number", "drawing"] },
  { key: "material", label: "جنس", words: ["جنس", "material"] },
  { key: "standard", label: "استاندارد", words: ["استاندارد", "standard"] },
  { key: "specification", label: "مشخصات", words: ["مشخصات", "specification", "spec"] },
  { key: "weight", label: "وزن", words: ["وزن", "weight"] },
  { key: "minStock", label: "نقطه سفارش", words: ["نقطه سفارش", "حداقل موجودی", "min stock", "min"] },
  { key: "sepidarCode", label: "کد سپیدار", words: ["کد سپیدار", "sepidar"] },
  { key: "legacyCode", label: "کد قدیمی (فقط برای جستجو)", words: ["کد قدیم", "کد قبلی", "legacy", "old code"] },
  { key: "mescCode", label: "کد MESC", words: ["mesc", "کد mesc"] },
];
const importer = ref<any>(null);

function normalize(text: any) {
  return String(text ?? "").trim().toLowerCase().replace(/ي/g, "ی").replace(/ك/g, "ک").replace(/\s+/g, " ");
}

async function readFile(event: any) {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  try {
    const rows: any[][] = await readXlsxFile(file);
    if (rows.length < 2) return fail("فایل ردیفی ندارد.");
    const headers = rows[0].map((h) => String(h ?? "").trim());
    const map: Record<string, number> = {};
    fields.forEach((f) => {
      let index = headers.findIndex((h) => f.words.includes(normalize(h)));
      if (index < 0) index = headers.findIndex((h) => f.words.some((w) => normalize(h).includes(w)));
      map[f.key] = index;
    });
    importer.value = { fileName: file.name, headers, rows: rows.slice(1), map, result: null, busy: false };
  } catch (e: any) {
    fail("فایل خوانده نشد. فایل باید xlsx باشد. " + (e?.message || ""));
  }
}

function cell(row: any[], key: string) {
  const index = importer.value.map[key];
  if (index === undefined || index < 0) return null;
  const v = row[index];
  return v === null || v === undefined ? null : v;
}

async function runImport() {
  const imp = importer.value;
  if (imp.map.code < 0 || imp.map.title < 0) return fail("ستون کد و شرح کالا را مشخص کنید.");
  const toNumber = (v: any) => (v === null || v === "" || isNaN(Number(v)) ? null : Number(v));
  const rows = imp.rows.map((r: any[]) => ({
    code: cell(r, "code") !== null ? String(cell(r, "code")) : null,
    title: cell(r, "title") !== null ? String(cell(r, "title")) : null,
    groupCode: cell(r, "groupCode") !== null ? String(cell(r, "groupCode")) : null,
    groupTitle: cell(r, "groupTitle") !== null ? String(cell(r, "groupTitle")) : null,
    unit: cell(r, "unit") !== null ? String(cell(r, "unit")) : null,
    trackingMode: cell(r, "trackingMode") !== null ? String(cell(r, "trackingMode")) : null,
    technicalCode: cell(r, "technicalCode") !== null ? String(cell(r, "technicalCode")) : null,
    material: cell(r, "material") !== null ? String(cell(r, "material")) : null,
    standard: cell(r, "standard") !== null ? String(cell(r, "standard")) : null,
    specification: cell(r, "specification") !== null ? String(cell(r, "specification")) : null,
    weight: toNumber(cell(r, "weight")),
    minStock: toNumber(cell(r, "minStock")),
    sepidarCode: cell(r, "sepidarCode") !== null ? String(cell(r, "sepidarCode")) : null,
    legacyCode: cell(r, "legacyCode") !== null ? String(cell(r, "legacyCode")) : null,
    mescCode: cell(r, "mescCode") !== null ? String(cell(r, "mescCode")) : null,
  }));
  imp.busy = true;
  const [data, error] = await importItems(rows);
  imp.busy = false;
  if (error) return fail(error);
  imp.result = data;
  ok(`ورود انجام شد: ${data.created} کالای جدید، ${data.updated} به‌روزرسانی.`);
  load();
  loadGroups();
}

onMounted(() => {
  load();
  loadGroups();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">کالاها و قطعات (کدینگ)</h1>
        <div class="d-flex gap-2">
          <label class="btn btn-sm btn-outline-primary mb-0">
            ورود از اکسل
            <input accept=".xlsx" class="d-none" type="file" @change="readFile" />
          </label>
          <button class="btn btn-sm btn-outline-secondary" @click="printLabels">چاپ برچسب ({{ selected.length }})</button>
          <button class="btn btn-sm btn-primary" @click="form = emptyForm()">کالای جدید</button>
        </div>
      </div>

      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="جستجو در کد، شرح، شماره نقشه" @keydown.enter="search" />
        <select v-model="filters.ItemGroupId" class="form-select form-select-sm" @change="search">
          <option :value="null">همه گروه‌ها</option>
          <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option>
        </select>
        <select v-model="filters.TrackingMode" class="form-select form-select-sm" @change="search">
          <option :value="null">همه انواع ردیابی</option>
          <option v-for="(t, k) in trackingModes" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.IsActive" class="form-select form-select-sm" @change="search">
          <option :value="null">فعال و غیرفعال</option>
          <option :value="true">فقط فعال</option>
          <option :value="false">فقط غیرفعال</option>
        </select>
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th></th>
              <th>کد</th>
              <th>شرح</th>
              <th>گروه</th>
              <th>واحد</th>
              <th>ردیابی</th>
              <th>شماره نقشه</th>
              <th>جنس</th>
              <th>موجودی</th>
              <th>نقطه سفارش</th>
              <th>کد سپیدار</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td class="text-center" colspan="12">در حال بارگذاری...</td></tr>
            <tr v-else-if="!items.length"><td class="text-center inv-muted" colspan="12">کالایی نیست.</td></tr>
            <tr v-for="item in items" :key="item.id" :class="{ 'opacity-50': !item.isActive }">
              <td><input :checked="selected.includes(item.id)" class="form-check-input" type="checkbox" @change="toggle(item.id)" /></td>
              <td class="inv-ltr">{{ item.code }}</td>
              <td>{{ item.title }}</td>
              <td>{{ item.itemGroupTitle }}</td>
              <td>{{ item.unit }}</td>
              <td><span class="inv-badge" :class="{ info: item.trackingMode === 2, warn: item.trackingMode === 1 }">{{ trackingModes[item.trackingMode] }}</span></td>
              <td class="inv-ltr">{{ item.technicalCode }}</td>
              <td>{{ item.material }}</td>
              <td :class="{ 'text-danger fw-bold': item.onHand < item.minStock }">{{ num(item.onHand) }}</td>
              <td>{{ num(item.minStock) }}</td>
              <td class="inv-ltr">{{ item.sepidarCode }}</td>
              <td class="text-nowrap">
                <button class="btn btn-sm btn-link p-0 ms-2" @click="edit(item)">ویرایش</button>
                <router-link :to="{ name: 'InventoryKardex', query: { itemId: item.id } }" class="btn btn-sm btn-link p-0 ms-2">کاردکس</router-link>
                <button class="btn btn-sm btn-link text-danger p-0" @click="remove(item)">حذف</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-pager">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} کالا)</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; load()">بعدی</button>
      </div>
    </div>

    <!-- add / edit -->
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ form.id ? "ویرایش کالا" : "کالای جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small">کد * (۱۴ رقم یا MESC)</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-8"><label class="form-label small">شرح *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-12"><CodeCheck :code="form.code" :item-id="form.id" /></div>
          <div class="col-md-6">
            <label class="form-label small">گروه</label>
            <select v-model="form.itemGroupId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option>
            </select>
          </div>
          <div class="col-md-3"><label class="form-label small">واحد *</label><input v-model="form.unit" class="form-control form-control-sm" /></div>
          <div class="col-md-3">
            <label class="form-label small">نوع ردیابی</label>
            <select v-model="form.trackingMode" class="form-select form-select-sm">
              <option v-for="(t, k) in trackingModes" :key="k" :value="Number(k)">{{ t }}</option>
            </select>
          </div>
          <div class="col-md-4"><label class="form-label small">شماره نقشه / فنی</label><input v-model="form.technicalCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small">جنس</label><input v-model="form.material" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small">استاندارد</label><input v-model="form.standard" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small">مشخصات</label><textarea v-model="form.specification" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-md-3"><label class="form-label small">وزن (kg)</label><input v-model.number="form.weight" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-3"><label class="form-label small">نقطه سفارش</label><input v-model.number="form.minStock" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-3"><label class="form-label small">حداکثر موجودی</label><input v-model.number="form.maxStock" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-3"><label class="form-label small">کد سپیدار</label><input v-model="form.sepidarCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small">کد MESC</label><input v-model="form.mescCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small">کد قدیمی (جستجو)</label><input v-model="form.legacyCode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3">
            <label class="form-label small">ماندگاری (روز)</label>
            <input v-model.number="form.shelfLifeDays" class="form-control form-control-sm" placeholder="الکترود، رنگ، مواد شیمیایی" type="number" />
          </div>
          <div class="col-md-3 d-flex align-items-end">
            <label class="form-check small"><input v-model="form.requiresIncomingInspection" class="form-check-input" type="checkbox" /> بازرسی ورودی لازم دارد (رسید در قرنطینه)</label>
          </div>
          <div class="col-12 form-check ms-2"><input id="itemActive" v-model="form.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="itemActive">فعال</label></div>
        </div>
        <p class="inv-muted mt-2">کد جدید را فقط برنامه‌ریزی و کنترل پروژه ثبت می‌کند؛ بقیه از «درخواست کد کالا» استفاده کنند. نوع ردیابی بعد از اولین گردش انبار قابل تغییر نیست. قطعات حساس توربین را «سریال» و مواد با گواهی ذوب را «بچ» تعریف کنید.</p>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>

    <!-- Excel import -->
    <div v-if="importer" class="inv-modal-backdrop" @click.self="importer = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">ورود کدینگ از اکسل</h2>
        <p class="small mb-2">
          فایل: {{ importer.fileName }} — {{ importer.rows.length }} ردیف. ستون هر فیلد را بررسی کنید. کالایی که کدش وجود دارد به‌روز می‌شود و
          گروه‌های جدید خودکار ساخته می‌شوند.
        </p>
        <div class="row g-2">
          <div v-for="f in fields" :key="f.key" class="col-md-3">
            <label class="form-label small mb-0">{{ f.label }}</label>
            <select v-model="importer.map[f.key]" class="form-select form-select-sm">
              <option :value="-1">— ندارد —</option>
              <option v-for="(h, i) in importer.headers" :key="i" :value="i">{{ h || "ستون " + (i + 1) }}</option>
            </select>
          </div>
        </div>
        <div class="table-responsive mt-3" style="max-height: 220px">
          <table class="table table-sm table-bordered">
            <thead><tr><th v-for="f in fields.filter((x) => importer.map[x.key] >= 0)" :key="f.key">{{ f.label }}</th></tr></thead>
            <tbody>
              <tr v-for="(r, i) in importer.rows.slice(0, 5)" :key="i">
                <td v-for="f in fields.filter((x) => importer.map[x.key] >= 0)" :key="f.key">{{ r[importer.map[f.key]] }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="importer.result" class="alert alert-info small mt-2">
          {{ importer.result.created }} کالای جدید، {{ importer.result.updated }} به‌روزرسانی، {{ importer.result.groupsCreated }} گروه جدید.
          <ul v-if="importer.result.errors.length" class="mb-0 mt-1">
            <li v-for="(e, i) in importer.result.errors.slice(0, 50)" :key="i">{{ e }}</li>
          </ul>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="importer = null">بستن</button>
          <button :disabled="importer.busy" class="btn btn-sm btn-primary" @click="runImport">{{ importer.busy ? "در حال ورود..." : "ورود اطلاعات" }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
