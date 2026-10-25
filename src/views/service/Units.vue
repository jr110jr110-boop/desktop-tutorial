<script setup lang="ts">
// Installed base (SvUnits): list + filters, and the unit page (/service/units/:id) with data, warranty, confirm (maker-checker), recommended spares and the as-built tree.
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import AsBuiltTree from "@/components/service/AsBuiltTree.vue";
import CustomerPick from "@/components/service/CustomerPick.vue";
import { isoDay, serialLabel, searchSerials, sitesOf, typedNumber } from "@/components/service/svKit";
import { getSvAsBuilt, getSvUnit, getSvUnits, saveSvUnit, svUnitAction } from "@/api/ServiceAPIs";
import { usePagedList, camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ServiceRoles } from "@/composables/roles";
import { badgeOfUnit, dateText, productTypeName, productTypes, qtyText, serviceActionTitles, spareSources, unitStatuses, warrantySources } from "@/composables/service";

const route = useRoute();
const router = useRouter();
const canWrite = hasRole(ServiceRoles.writers);

const customer = ref<any>(null);
const filters = ref<any>({ Status: null, NeedsConfirm: null, Contains: "" });
const productType = ref<number | null>(null);
const list = usePagedList(getSvUnits, computed(() => ({ ...filters.value, CustomerId: customer.value?.id ?? null })) as any);
const rows = computed(() => (productType.value ? list.rows.value.filter((u: any) => u.productType === productType.value) : list.rows.value));

const unit = ref<any>(null);
const asBuilt = ref<any>(null);
const tab = ref("info");
const busy = ref(false);
const form = ref<any>(null);
const sites = ref<any[]>([]);
const serial = ref<any>(null);
const formCustomer = ref<any>(null);
const prompt = ref<null | { action: "confirm" | "decommission" | "reactivate" }>(null);
const tabs = [{ key: "info", title: "مشخصات و گارانتی" }, { key: "spares", title: "قطعات یدکی پیشنهادی" }, { key: "asbuilt", title: "as-built" }];

const unitId = computed(() => Number(route.params.id) || null);

async function loadUnit(id: number) {
  const [data, error] = await getSvUnit(id);
  if (error) return fail(error);
  unit.value = data;
  asBuilt.value = null;
  tab.value = "info";
  form.value = null;
}
async function loadAsBuilt() {
  if (!unit.value) return;
  const [data, error] = await getSvAsBuilt(unit.value.id);
  if (error) return fail(error);
  asBuilt.value = camelize(data);
}
watch(tab, (t) => t === "asbuilt" && !asBuilt.value && loadAsBuilt());
watch(unitId, (id) => (id ? loadUnit(id) : (unit.value = null)));

function open(u: any) {
  router.push("/service/units/" + u.id);
}
function back() {
  router.push("/service/units");
  load();
}
const load = () => list.load();

// ------------------------------------------------------------ edit form
async function startEdit(u: any | null) {
  form.value = {
    id: u?.id ?? null, tag: u?.tag || "", title: u?.title || "", productType: u?.productType || 1, customerId: u?.customerId ?? null, siteId: u?.siteId ?? null,
    handoverDate: isoDay(u?.handoverDate), commissioningDate: isoDay(u?.commissioningDate),
    warrantyMode: u?.warrantySource === 3 ? "dates" : "months", warrantyMonths: u?.warrantyMonths ? String(u.warrantyMonths) : "", warrantyStart: isoDay(u?.warrantyStart), warrantyEnd: isoDay(u?.warrantyEnd),
    note: u?.note || "", rowVersion: u?.rowVersion || null,
    spares: (u?.spares || []).filter((s: any) => s.source === 1).map((s: any) => ({ item: { id: s.itemId, code: s.itemCode, title: s.itemName }, quantity: String(s.quantity), note: s.note || "" })),
  };
  formCustomer.value = u?.customerId ? { id: u.customerId, name: u.customerName } : null;
  serial.value = u?.serialUnitId ? { id: u.serialUnitId, serialNumber: u.serialNumber } : null;
  sites.value = await sitesOf(form.value.customerId);
}
async function pickCustomer(c: any) {
  form.value.customerId = c?.id ?? null;
  form.value.siteId = null;
  sites.value = await sitesOf(form.value.customerId);
}
function pickSerial(s: any) {
  form.value.serialUnitId = s?.id ?? null;
}
async function save() {
  const f = form.value;
  if (!String(f.title).trim()) return fail("عنوان واحد را وارد کنید.");
  const dto: any = {
    id: f.id, tag: f.tag || null, title: f.title, productType: f.productType, customerId: f.customerId, siteId: f.siteId, serialUnitId: serial.value?.id ?? null,
    handoverDate: f.handoverDate, commissioningDate: f.commissioningDate, note: f.note, rowVersion: f.rowVersion,
    spares: f.spares.filter((s: any) => s.item?.id).map((s: any) => ({ itemId: s.item.id, quantity: typedNumber(s.quantity) ?? 0, note: s.note || null })),
  };
  if (f.warrantyMode === "dates") {
    dto.warrantyStart = f.warrantyStart;
    dto.warrantyEnd = f.warrantyEnd;
  } else if (f.warrantyMonths) dto.warrantyMonths = typedNumber(f.warrantyMonths);
  busy.value = true;
  const [data, error] = await saveSvUnit(dto);
  busy.value = false;
  if (error) return fail(error);
  ok("واحد ذخیره شد.");
  form.value = null;
  if (data?.id) {
    unit.value = data;
    asBuilt.value = null;
    if (unitId.value !== data.id) router.push("/service/units/" + data.id);
  }
  load();
}
async function act(action: "confirm" | "decommission" | "reactivate", note?: string) {
  busy.value = true;
  const [data, error] = await svUnitAction({ id: unit.value.id, action, note: note || null, rowVersion: unit.value.rowVersion });
  busy.value = false;
  if (error) {
    fail(error);
    return loadUnit(unit.value.id);
  }
  unit.value = data;
  ok(serviceActionTitles[action] + " انجام شد.");
  load();
}
const addSpare = () => form.value.spares.push({ item: null, quantity: "1", note: "" });

onMounted(() => {
  list.load();
  if (unitId.value) loadUnit(unitId.value);
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <!-- ================= list ================= -->
      <template v-if="!unitId && !form">
        <div class="inv-toolbar">
          <h1 class="inv-title">واحدهای نصب‌شده</h1>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="startEdit(null)">واحد جدید</button>
        </div>
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">مشتری</label><CustomerPick v-model="customer" /></div>
          <div><label class="form-label small mb-0">وضعیت</label>
            <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in unitStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div><label class="form-label small mb-0">نوع محصول (روی همین صفحه)</label>
            <select v-model="productType" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in productTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div><label class="form-label small mb-0">tag یا عنوان</label><input v-model="filters.Contains" class="form-control form-control-sm" @keyup.enter="list.search" /></div>
          <label class="form-check mb-0"><input v-model="filters.NeedsConfirm" class="form-check-input" type="checkbox" :true-value="true" :false-value="null" /> <span class="form-check-label">منتظر تایید</span></label>
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>tag</th><th>عنوان</th><th>نوع</th><th>مشتری</th><th>سایت</th><th>پایان گارانتی</th><th>کارکرد (ساعت)</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">واحدی نیست.</td></tr>
              <tr v-for="u in rows" :key="u.id">
                <td class="inv-ltr">{{ u.tag }}</td><td>{{ u.title }}</td><td>{{ u.productTypeText }}</td><td>{{ u.customerName }}</td><td>{{ u.siteTitle }}</td>
                <td>{{ u.warrantyEndText || "-" }}</td><td>{{ qtyText(u.runningHours) }}</td>
                <td>
                  <span :class="badgeOfUnit(u.status)" class="inv-badge">{{ u.statusText }}</span>
                  <span v-if="!u.isConfirmed && !u.decommissioned" class="inv-badge warn ms-1">منتظر تایید</span>
                </td>
                <td><button class="btn btn-sm btn-outline-primary" @click="open(u)">باز کردن</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
      </template>

      <!-- ================= edit form ================= -->
      <template v-else-if="form">
        <div class="inv-toolbar"><h1 class="inv-title">{{ form.id ? "ویرایش واحد" : "واحد نصب‌شده جدید" }}</h1></div>
        <p v-if="form.id && unit?.isConfirmed" class="alert alert-warning py-1 small">ویرایش واحد تاییدشده آن را دوباره به وضعیت «منتظر تایید» برمی‌گرداند و نفر دوم باید تایید کند.</p>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">شماره tag (خالی = خودکار)</label><input v-model="form.tag" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع محصول</label>
            <select v-model="form.productType" class="form-select form-select-sm"><option v-for="(t, k) in productTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">مشتری</label><CustomerPick v-model="formCustomer" :fallback-text="unit?.customerName || ''" @pick="pickCustomer" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">سایت نصب</label>
            <select v-model="form.siteId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="s in sites" :key="s.id" :value="s.id">{{ s.title }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">قطعه سریالی (ردیابی انبار)</label>
            <LookupBox v-model="serial" :fetch="searchSerials" :label="serialLabel" :fallback-text="unit?.serialNumber || ''" placeholder="شماره سریال" @pick="pickSerial" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ تحویل</label><FnDate v-model="form.handoverDate" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ راه‌اندازی</label><FnDate v-model="form.commissioningDate" /></div>
          <div class="col-md-6">
            <label class="form-label small mb-0">گارانتی</label>
            <div class="d-flex gap-2 align-items-center flex-wrap">
              <select v-model="form.warrantyMode" class="form-select form-select-sm" style="max-width: 12rem"><option value="months">ماه از تاریخ تحویل</option><option value="dates">تاریخ شروع و پایان</option></select>
              <input v-if="form.warrantyMode === 'months'" v-model="form.warrantyMonths" class="form-control form-control-sm fn-num" inputmode="numeric" placeholder="ماه" style="max-width: 6rem" />
              <template v-else><FnDate v-model="form.warrantyStart" /><FnDate v-model="form.warrantyEnd" /></template>
            </div>
            <div class="inv-muted">خالی بماند و دستی هم تعیین نشود: پایان تضمین گارانتی قرارداد به کار می‌رود.</div>
          </div>
          <div class="col-12"><label class="form-label small mb-0">یادداشت</label><textarea v-model="form.note" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <h3 class="inv-title mt-3 mb-2">قطعات یدکی پیشنهادی (دستی)</h3>
        <table class="table table-sm">
          <thead><tr><th>قطعه</th><th style="width: 8rem">تعداد</th><th>توضیح</th><th></th></tr></thead>
          <tbody>
            <tr v-for="(s, i) in form.spares" :key="i">
              <td><ItemPicker v-model="s.item" @select="s.item = $event" /></td>
              <td><input v-model="s.quantity" class="form-control form-control-sm fn-num" inputmode="decimal" /></td>
              <td><input v-model="s.note" class="form-control form-control-sm" /></td>
              <td><button class="btn btn-sm btn-outline-danger" @click="form.spares.splice(i, 1)">حذف</button></td>
            </tr>
          </tbody>
        </table>
        <button class="btn btn-sm btn-outline-secondary" @click="addSpare">افزودن قطعه</button>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </template>

      <!-- ================= unit page ================= -->
      <template v-else-if="unit">
        <div class="inv-toolbar">
          <div>
            <h1 class="inv-title mb-0">{{ unit.title }} <span class="inv-ltr">{{ unit.tag }}</span></h1>
            <span :class="badgeOfUnit(unit.status)" class="inv-badge">{{ unit.statusText }}</span>
            <span v-if="!unit.isConfirmed && !unit.decommissioned" class="inv-badge warn ms-1">منتظر تایید</span>
            <span v-if="unit.isConfirmed" class="inv-muted ms-2">تایید: {{ unit.confirmedAtText }}</span>
          </div>
          <button class="btn btn-sm btn-outline-secondary" @click="back">بازگشت به فهرست</button>
        </div>
        <div v-if="canWrite" class="d-flex gap-2 flex-wrap mb-3">
          <button v-if="!unit.decommissioned" class="btn btn-sm btn-outline-primary" @click="startEdit(unit)">ویرایش</button>
          <button v-if="!unit.isConfirmed && !unit.decommissioned" :disabled="busy" class="btn btn-sm btn-success" @click="prompt = { action: 'confirm' }">تایید واحد</button>
          <button v-if="!unit.decommissioned" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="prompt = { action: 'decommission' }">از رده خارج کردن</button>
          <button v-else :disabled="busy" class="btn btn-sm btn-outline-primary" @click="prompt = { action: 'reactivate' }">فعال‌سازی دوباره</button>
        </div>
        <p v-if="!unit.isConfirmed && !unit.decommissioned" class="inv-muted">واحدی که سیستم هنگام تحویل پیش‌نویس کرده با تایید نفر دوم (غیر از ویرایشگر) معتبر می‌شود؛ tag، مشتری و سایت نصب باید پر باشد.</p>

        <PcTabs v-model="tab" :tabs="tabs" />

        <div v-if="tab === 'info'" class="row g-2 small">
          <div class="col-md-4"><b>مشتری:</b> {{ unit.customerName || "-" }}</div>
          <div class="col-md-4"><b>سایت نصب:</b> {{ unit.siteTitle || "-" }}</div>
          <div class="col-md-4"><b>نوع محصول:</b> {{ productTypeName(unit.productType) }}</div>
          <div class="col-md-4"><b>شماره سریال:</b> <span class="inv-ltr">{{ unit.serialNumber || "-" }}</span></div>
          <div class="col-md-4"><b>قرارداد:</b> <span class="inv-ltr">{{ unit.contractCode || "-" }}</span></div>
          <div class="col-md-4"><b>دستور ساخت:</b> <span class="inv-ltr">{{ unit.workOrderCode || "-" }}</span></div>
          <div class="col-md-4"><b>محموله:</b> <span class="inv-ltr">{{ unit.shipmentNumber || "-" }}</span></div>
          <div class="col-md-4"><b>تاریخ تحویل:</b> {{ unit.handoverDateText || "-" }}</div>
          <div class="col-md-4"><b>تاریخ راه‌اندازی:</b> {{ unit.commissioningDateText || "-" }}</div>
          <div class="col-md-4"><b>منبع گارانتی:</b> {{ warrantySources[unit.warrantySource] }}<span v-if="unit.warrantyMonths"> ({{ unit.warrantyMonths }} ماه)</span></div>
          <div class="col-md-4"><b>شروع گارانتی:</b> {{ unit.warrantyStartText || "-" }}</div>
          <div class="col-md-4"><b>پایان گارانتی:</b> {{ unit.warrantyEndText || "-" }} <span v-if="unit.inWarranty" class="inv-badge ok">در گارانتی</span></div>
          <div class="col-md-4"><b>کارکرد (ساعت):</b> {{ qtyText(unit.runningHours) }} <span v-if="unit.runningHoursAtText" class="inv-muted">تا {{ unit.runningHoursAtText }}</span></div>
          <div class="col-12"><b>یادداشت:</b> {{ unit.note || "-" }}</div>
        </div>

        <div v-else-if="tab === 'spares'">
          <p v-if="unit.spareListDocumentNumber" class="small">
            فهرست قطعات یدکی (MDR): <span class="inv-ltr">{{ unit.spareListDocumentNumber }}</span> - {{ unit.spareListSourceText }}
          </p>
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>قطعه</th><th>تعداد</th><th>منبع</th><th>توضیح</th></tr></thead>
            <tbody>
              <tr v-if="!(unit.spares || []).length"><td colspan="5" class="text-center inv-muted">قطعه‌ای ثبت نشده است.</td></tr>
              <tr v-for="s in unit.spares" :key="s.id"><td class="inv-ltr">{{ s.itemCode }}</td><td>{{ s.itemName }}</td><td>{{ qtyText(s.quantity) }}</td><td>{{ spareSources[s.source] }}</td><td>{{ s.note }}</td></tr>
            </tbody>
          </table>
        </div>

        <AsBuiltTree v-else-if="tab === 'asbuilt'" :as-built="asBuilt" :has-serial="!!unit.serialUnitId" />
      </template>
    </div>

    <PromptModal v-if="prompt" :title="serviceActionTitles[prompt.action]" :fields="[{ key: 'note', label: 'یادداشت', type: 'textarea' }]" :confirm-text="serviceActionTitles[prompt.action]"
      @cancel="prompt = null" @confirm="(v: any) => { const a = prompt!.action; prompt = null; act(a, v.note); }" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
