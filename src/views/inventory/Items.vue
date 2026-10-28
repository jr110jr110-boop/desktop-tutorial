<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import readXlsxFile from "read-excel-file";
import CodeCheck from "@/components/inventory/CodeCheck.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import {
  decideItemQualityChange,
  deleteItem,
  getGroups,
  getItemQualityChanges,
  getItems,
  importItems,
  rowsOf,
  saveItem,
  saveItemQuality,
} from "@/api/InventoryAPIs";
import { certificateTypes } from "@/composables/quality";
import { fail, jalali, labelCode, num, ok, openLabels, trackingModes } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

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
  mescCode: "", legacyCode: "", requiresIncomingInspection: true, shelfLifeDays: null, requiredCertificateType: null,
});
const form = ref<any>(null);
const saving = ref(false);

// Quality gates of an item (inspection on receipt, required certificate, shelf life) are changed in their own panel by PMO, QC and QA; a change that reduces a control waits
// for the second approval of the QA manager and the item keeps its gates until then. The general form below sends the gates as they are, so it never changes them.
const canQuality = hasRole(InventoryRoles.itemQuality);
const canDecide = hasRole(InventoryRoles.itemQualityDeciders);
const quality = ref<any>(null);
const qualityBusy = ref(false);
// the group code of the code being typed (a new item starts with the gate of its group: group 05, consumables, is received without inspection)
const codeGroup = ref<string | null>(null);
const changes = ref<{ open: boolean; busy: boolean; status: number | null; itemId: number | null; rows: any[]; total: number; rejecting: any }>({
  open: false,
  busy: false,
  status: 0,
  itemId: null,
  rows: [],
  total: 0,
  rejecting: null,
});
const pendingCount = ref<number | null>(null);
const changeStatuses: Record<number, string> = { 0: "در انتظار تایید QA", 1: "تاییدشده", 2: "ردشده" };

const normalShelf = (v: any) => {
  const n = Number(v);
  return v === null || v === undefined || v === "" || !Number.isFinite(n) || n <= 0 ? null : Math.trunc(n);
};
const certificateNumber = (v: any) => (v === null || v === undefined || v === "" ? null : Number(v));
/** the same rule as the API (ItemQualityRules.IsWeakening): inspection switched off, certificate removed or lowered, shelf life removed or lengthened */
function isWeakening(oldInspection: boolean, oldCertificate: any, oldShelf: any, newInspection: boolean, newCertificate: any, newShelf: any) {
  const oc = certificateNumber(oldCertificate);
  const nc = certificateNumber(newCertificate);
  const os = normalShelf(oldShelf);
  const ns = normalShelf(newShelf);
  return (oldInspection && !newInspection) || (oc !== null && (nc === null || nc < oc)) || (os !== null && (ns === null || ns > os));
}
const qualityDirty = computed(() => {
  const q = quality.value;
  const f = form.value;
  if (!q || !f || !f.id) return false;
  return q.requiresIncomingInspection !== !!f.requiresIncomingInspection || certificateNumber(q.requiredCertificateType) !== certificateNumber(f.requiredCertificateType) || normalShelf(q.shelfLifeDays) !== normalShelf(f.shelfLifeDays);
});
const qualityWeakening = computed(() => {
  const q = quality.value;
  const f = form.value;
  return !!q && !!f && qualityDirty.value && isWeakening(!!f.requiresIncomingInspection, f.requiredCertificateType, f.shelfLifeDays, q.requiresIncomingInspection, q.requiredCertificateType, q.shelfLifeDays);
});
const gateText = (inspection: boolean, certificate: any, shelf: any) =>
  [
    inspection ? "بازرسی ورودی" : "بدون بازرسی ورودی",
    certificate ? "گواهی " + certificateTypes[Number(certificate)] : "بدون گواهی الزامی",
    normalShelf(shelf) ? "ماندگاری " + normalShelf(shelf) + " روز" : "بدون ماندگاری",
  ].join(" · ");

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

function resetQuality(item: any) {
  quality.value = { requiresIncomingInspection: !!item.requiresIncomingInspection, requiredCertificateType: item.requiredCertificateType ?? null, shelfLifeDays: item.shelfLifeDays ?? null, reason: "" };
}

function edit(item: any) {
  form.value = { ...emptyForm(), ...item };
  codeGroup.value = null;
  resetQuality(form.value);
}

function create() {
  form.value = emptyForm();
  codeGroup.value = null;
  quality.value = null;
}

// the code check tells the group of a typed code: a new item starts with the gate of that group
function onChecked(result: any) {
  codeGroup.value = result?.groupCode || null;
}

async function save() {
  const body = { ...form.value };
  if (!body.id) {
    // a new item is created with the gates of its group; the quality panel changes them afterwards
    body.requiresIncomingInspection = codeGroup.value !== "05";
    body.requiredCertificateType = null;
    body.shelfLifeDays = null;
  }
  saving.value = true;
  const [, error] = await saveItem(body);
  saving.value = false;
  if (error) return fail(error);
  ok("کالا ذخیره شد.");
  form.value = null;
  load();
}

/** POST inventory/items/quality: stricter gates apply at once; a reduction waits for the QA manager and needs a reason. */
async function saveQuality() {
  const q = quality.value;
  const f = form.value;
  if (!qualityDirty.value) return fail("مقدار جدیدی برای ویژگی‌های کیفی کالا وارد نشده است.");
  if (qualityWeakening.value && !String(q.reason || "").trim()) return fail("برای کم‌کردن یک کنترل کیفی (حذف بازرسی ورودی، کاهش نوع گواهی یا حذف/افزایش ماندگاری) دلیل تغییر را بنویسید.");
  qualityBusy.value = true;
  const [data, error] = await saveItemQuality({
    itemId: f.id,
    requiresIncomingInspection: q.requiresIncomingInspection,
    requiredCertificateType: certificateNumber(q.requiredCertificateType),
    shelfLifeDays: normalShelf(q.shelfLifeDays),
    reason: String(q.reason || "").trim() || null,
  });
  qualityBusy.value = false;
  if (error) return fail(error);
  // the answer is the item as it is now: with a pending request it still has its old gates
  form.value = { ...f, requiresIncomingInspection: data.requiresIncomingInspection, requiredCertificateType: data.requiredCertificateType, shelfLifeDays: data.shelfLifeDays, pendingQualityChangeId: data.pendingQualityChangeId };
  resetQuality(data);
  ok(data.pendingQualityChangeId ? "کاهش کنترل کیفی ثبت شد و منتظر تایید دوم مدیر QA است؛ تا آن زمان کالا با کنترل قبلی کار می‌کند." : "ویژگی‌های کیفی کالا اعمال شد.");
  load();
  countPending();
}

// ---------------------------------------------------------------------------- pending changes of the quality gates (the QA manager decides)
async function countPending() {
  if (!canQuality) return;
  const [data] = await getItemQualityChanges({ Status: 0, PageSize: 1 });
  pendingCount.value = data ? data.totalItems ?? rowsOf(data).length : null;
}

async function loadChanges() {
  const c = changes.value;
  c.busy = true;
  const [data, error] = await getItemQualityChanges({ Status: c.status, ItemId: c.itemId, PageSize: 100 });
  c.busy = false;
  if (error) return fail(error);
  c.rows = rowsOf(data);
  c.total = data?.totalItems ?? c.rows.length;
}

function openChanges(itemId: number | null = null) {
  changes.value = { open: true, busy: false, status: 0, itemId, rows: [], total: 0, rejecting: null };
  loadChanges();
}

async function decideChange(row: any, approve: boolean, note?: string) {
  changes.value.busy = true;
  const [, error] = await decideItemQualityChange({ id: row.id, approve, note: note || null });
  changes.value.busy = false;
  changes.value.rejecting = null;
  if (error) return fail(error);
  ok(approve ? "کاهش کنترل کیفی تایید و اعمال شد." : "درخواست رد شد.");
  loadChanges();
  load();
  countPending();
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
  countPending();
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
          <button v-if="canQuality" class="btn btn-sm btn-outline-secondary" @click="openChanges()">
            تغییرهای کیفی کالا<span v-if="pendingCount" class="inv-badge warn ms-1">{{ pendingCount }} در انتظار</span>
          </button>
          <button class="btn btn-sm btn-primary" @click="create">کالای جدید</button>
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
              <th>کنترل کیفی</th>
              <th>کد سپیدار</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td class="text-center" colspan="13">در حال بارگذاری...</td></tr>
            <tr v-else-if="!items.length"><td class="text-center inv-muted" colspan="13">کالایی نیست.</td></tr>
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
              <td class="small">
                <span class="inv-badge" :class="item.requiresIncomingInspection ? 'info' : ''" :title="gateText(item.requiresIncomingInspection, item.requiredCertificateType, item.shelfLifeDays)">
                  {{ item.requiresIncomingInspection ? "بازرسی ورودی" : "بدون بازرسی" }}
                </span>
                <span v-if="item.requiredCertificateType" class="inv-badge info ms-1">{{ certificateTypes[item.requiredCertificateType] }}</span>
                <span v-if="item.shelfLifeDays" class="inv-badge ms-1">ماندگاری {{ item.shelfLifeDays }} روز</span>
                <button
                  v-if="item.pendingQualityChangeId"
                  class="inv-badge warn ms-1 border-0"
                  title="کاهش کنترل کیفی این کالا منتظر تایید دوم مدیر QA است؛ کالا تا آن زمان کنترل قبلی را دارد."
                  @click="canQuality ? openChanges(item.id) : undefined">
                  در انتظار تایید QA
                </button>
              </td>
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
          <div class="col-12"><CodeCheck :code="form.code" :item-id="form.id" @checked="onChecked" /></div>
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
          <div class="col-12 form-check ms-2"><input id="itemActive" v-model="form.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="itemActive">فعال</label></div>
        </div>
        <p class="inv-muted mt-2">کد جدید را فقط برنامه‌ریزی و کنترل پروژه ثبت می‌کند؛ بقیه از «درخواست کد کالا» استفاده کنند. نوع ردیابی بعد از اولین گردش انبار قابل تغییر نیست. قطعات حساس توربین را «سریال» و مواد با گواهی ذوب را «بچ» تعریف کنید.</p>

        <!-- quality gates: their own panel (PMO, QC, QA) -->
        <div class="border rounded p-2 mt-2">
          <h3 class="inv-title mb-1" style="font-size: 1rem">ویژگی‌های کیفیت کالا</h3>
          <template v-if="!form.id">
            <p class="inv-muted small mb-0">
              کالای جدید با کنترل کیفی گروه خودش ثبت می‌شود (بازرسی ورودی لازم است، مگر در گروه ۰۵ مصرفی و اداری). بازرسی ورودی، گواهی الزامی و ماندگاری را پس از ثبت کالا QC یا QA در همین پنجره تغییر می‌دهد.
            </p>
          </template>
          <template v-else>
            <p class="small mb-1">
              وضع فعلی: <b>{{ gateText(form.requiresIncomingInspection, form.requiredCertificateType, form.shelfLifeDays) }}</b>
            </p>
            <p v-if="form.pendingQualityChangeId" class="small text-warning-emphasis mb-1">
              یک درخواست کاهش کنترل کیفی منتظر تایید دوم مدیر QA است؛ کالا تا آن زمان کنترل بالا را دارد و درخواست دیگری برای کاهش کنترل ثبت نمی‌شود.
              <button v-if="canQuality" class="btn btn-sm btn-link p-0" type="button" @click="openChanges(form.id)">دیدن درخواست</button>
            </p>
            <template v-if="canQuality && quality">
              <div class="row g-2 align-items-end">
                <div class="col-md-4">
                  <label class="form-check small mb-0">
                    <input v-model="quality.requiresIncomingInspection" class="form-check-input" type="checkbox" />
                    بازرسی ورودی لازم دارد (رسید در قرنطینه)
                  </label>
                </div>
                <div class="col-md-4">
                  <label class="form-label small mb-0">گواهی مواد لازم (EN 10204)</label>
                  <select v-model="quality.requiredCertificateType" class="form-select form-select-sm">
                    <option :value="null">لازم نیست</option>
                    <option v-for="(t, k) in certificateTypes" :key="k" :value="Number(k)">{{ t }}</option>
                  </select>
                </div>
                <div class="col-md-4">
                  <label class="form-label small mb-0">ماندگاری (روز)</label>
                  <input v-model="quality.shelfLifeDays" class="form-control form-control-sm" min="0" placeholder="الکترود، رنگ، مواد شیمیایی" type="number" />
                </div>
                <div class="col-12">
                  <label class="form-label small mb-0">دلیل تغییر <span v-if="qualityWeakening">*</span></label>
                  <input v-model="quality.reason" class="form-control form-control-sm" maxlength="1000" />
                  <span v-if="qualityWeakening" class="small text-warning-emphasis">
                    این تغییر یک کنترل را کم می‌کند؛ دلیل لازم است و پس از ثبت، تا تایید دوم مدیر QA اعمال نمی‌شود.
                  </span>
                  <span v-else-if="qualityDirty" class="small inv-muted">کنترل سخت‌تر بدون تایید دوم اعمال می‌شود و در سابقه کالا می‌ماند.</span>
                </div>
              </div>
              <div class="d-flex justify-content-end mt-2">
                <button :disabled="qualityBusy || !qualityDirty" class="btn btn-sm btn-outline-primary" type="button" @click="saveQuality">ثبت تغییر ویژگی‌های کیفی</button>
              </div>
            </template>
            <p v-else class="inv-muted small mb-0">تغییر بازرسی ورودی، گواهی الزامی و ماندگاری با کنترل کیفی (QC) و مدیر QA است؛ ذخیره این فرم آن‌ها را تغییر نمی‌دهد.</p>
          </template>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>

    <!-- requests to change the quality gates of items; the QA manager decides on a reduction -->
    <div v-if="changes.open" class="inv-modal-backdrop" @click.self="changes.open = false">
      <div class="inv-modal wide" role="dialog" aria-modal="true">
        <div class="inv-toolbar">
          <h2 class="inv-title">تغییرهای کنترل کیفی کالا</h2>
          <button class="btn btn-sm btn-outline-secondary" @click="changes.open = false">بستن</button>
        </div>
        <div class="inv-filters mb-2">
          <select v-model="changes.status" class="form-select form-select-sm" @change="loadChanges">
            <option :value="null">همه</option>
            <option v-for="(t, k) in changeStatuses" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
          <span v-if="changes.itemId" class="small">فقط یک کالا <button class="btn btn-sm btn-link p-0" @click="changes.itemId = null; loadChanges()">نمایش همه</button></span>
        </div>
        <p class="inv-muted small">
          کاهش کنترل کیفی (حذف بازرسی ورودی، کاهش نوع گواهی، حذف یا افزایش ماندگاری) را مدیر QA تایید می‌کند و آن شخص نباید خودش درخواست‌دهنده باشد. درخواستی که پس از ثبت، کنترل‌های کالا
          عوض شده (کهنه) فقط رد می‌شود.
        </p>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead>
              <tr>
                <th>کالا</th>
                <th>کنترل فعلی</th>
                <th>کنترل درخواستی</th>
                <th>دلیل</th>
                <th>درخواست‌کننده</th>
                <th>وضعیت</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="changes.busy"><td class="text-center" colspan="7">در حال بارگذاری...</td></tr>
              <tr v-else-if="!changes.rows.length"><td class="text-center inv-muted" colspan="7">موردی نیست.</td></tr>
              <tr v-for="c in changes.rows" :key="c.id">
                <td><span class="inv-ltr">{{ c.itemCode }}</span> — {{ c.itemTitle }}</td>
                <td class="small">{{ gateText(c.currentRequiresIncomingInspection, c.currentRequiredCertificateType, c.currentShelfLifeDays) }}</td>
                <td class="small">{{ gateText(c.requiresIncomingInspection, c.requiredCertificateType, c.shelfLifeDays) }}</td>
                <td class="small">
                  {{ c.reason || "-" }}
                  <div v-if="c.decisionNote" class="inv-muted">تصمیم: {{ c.decisionNote }}</div>
                </td>
                <td class="small">{{ c.requestedByName }}<div class="inv-muted">{{ jalali(c.requestedAt, true) }}</div></td>
                <td>
                  <span class="inv-badge" :class="c.status === 0 ? 'warn' : c.status === 1 ? 'ok' : 'bad'">{{ changeStatuses[c.status] }}</span>
                  <span v-if="c.stale" class="inv-badge bad ms-1" title="کنترل‌های کالا پس از ثبت این درخواست تغییر کرده‌اند">کهنه</span>
                  <div v-if="c.decidedAt" class="small inv-muted">{{ c.decidedByName }} {{ jalali(c.decidedAt, true) }}</div>
                </td>
                <td class="text-nowrap">
                  <template v-if="c.status === 0 && canDecide">
                    <button :disabled="changes.busy || c.stale" class="btn btn-sm btn-success py-0" :title="c.stale ? 'کنترل‌های کالا پس از این درخواست تغییر کرده‌اند؛ آن را رد کنید' : ''" @click="decideChange(c, true)">تایید</button>
                    <button :disabled="changes.busy" class="btn btn-sm btn-outline-danger py-0 ms-1" @click="changes.rejecting = c">رد</button>
                  </template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="small inv-muted mb-0">{{ changes.total }} مورد</p>
      </div>
    </div>
    <ReasonDialog
      v-if="changes.rejecting"
      title="رد درخواست تغییر کنترل کیفی"
      label="دلیل رد"
      confirm-text="رد درخواست"
      :busy="changes.busy"
      @confirm="(note) => decideChange(changes.rejecting, false, note)"
      @close="changes.rejecting = null" />

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
