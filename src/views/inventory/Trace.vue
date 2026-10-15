<script setup lang="ts">
// Part traceability: scan or search a serial / lot, see where it came from, what was done to it and where it went.
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import OperationsPanel from "@/components/inventory/OperationsPanel.vue";
import {
  createSerial, downloadFile, getMachines, getProviders, getWorkOrders, installPart, removePart, rowsOf, saveLot,
  traceLot, traceSearch, traceSerial, updateSerial, uploadFile,
} from "@/api/InventoryAPIs";
import { documentTypes, fail, jalali, labelCode, num, ok, openLabels, serialStatuses } from "@/composables/inventory";

const route = useRoute();
const router = useRouter();

const q = ref("");
const results = ref<any>(null);
const serial = ref<any>(null);
const lot = ref<any>(null);
const machines = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const providers = ref<any[]>([]);
const busy = ref(false);
const install = ref<any>(null);
const creating = ref<any>(null);
const editSerial = ref<any>(null);
const editLot = ref<any>(null);

async function search() {
  const text = q.value.trim();
  if (!text) return;
  const [data, error] = await traceSearch(text);
  if (error) return fail(error);
  const total = data.items.length + data.serials.length + data.lots.length + data.locations.length;
  if (data.serials.length === 1 && total === 1) return openSerial(data.serials[0].id);
  if (data.lots.length === 1 && total === 1) return openLot(data.lots[0].id);
  results.value = data;
  if (!total) fail("چیزی پیدا نشد.");
}

function openSerial(id: number) {
  results.value = null;
  router.replace({ name: "InventoryTrace", query: { serial: id } });
}

function openLot(id: number) {
  results.value = null;
  router.replace({ name: "InventoryTrace", query: { lot: id } });
}

async function load() {
  serial.value = null;
  lot.value = null;
  const sid = Number(route.query.serial);
  const lid = Number(route.query.lot);
  if (sid) {
    const [data, error] = await traceSerial(sid);
    if (error) return fail(error);
    serial.value = data;
  } else if (lid) {
    const [data, error] = await traceLot(lid);
    if (error) return fail(error);
    lot.value = data;
  }
}

watch(() => route.query, load);

const installedLinks = computed(() =>
  serial.value ? serial.value.assemblyHistory.filter((x: any) => x.parentSerialUnitId === serial.value.unit.id && !x.removedAt) : []
);

function onLotFile(event: Event) {
  const input = event.target as HTMLInputElement;
  editLot.value.file = input.files && input.files[0];
}

function statusClass(s: number) {
  return s === 2 ? "ok" : s === 1 ? "info" : s === 3 ? "warn" : s === 9 || s === 5 ? "bad" : "";
}

// ---------- assembly
function openInstall() {
  install.value = { text: "", child: null, lot: null, quantity: 1, notes: "" };
}

async function findChild() {
  const [data, error] = await traceSearch(install.value.text.trim());
  if (error) return fail(error);
  install.value.child = data.serials.length === 1 ? data.serials[0] : null;
  install.value.lot = !install.value.child && data.lots.length === 1 ? data.lots[0] : null;
  if (!install.value.child && !install.value.lot) fail("یک قطعه سریالی یا یک بچ مشخص را اسکن کنید.");
}

async function doInstall() {
  const i = install.value;
  busy.value = true;
  const [, error] = await installPart({
    parentSerialUnitId: serial.value.unit.id,
    childSerialUnitId: i.child ? i.child.id : null,
    childLotId: i.lot ? i.lot.id : null,
    quantity: i.lot ? i.quantity : null,
    workOrderId: serial.value.unit.workOrderId,
    notes: i.notes,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("قطعه نصب شد.");
  install.value = null;
  load();
}

async function doRemove(link: any) {
  const notes = prompt("دلیل باز کردن قطعه از مجموعه:");
  if (notes === null) return;
  const [, error] = await removePart({ assemblyLinkId: link.id, notes });
  if (error) return fail(error);
  ok("قطعه از مجموعه باز شد.");
  load();
}

// ---------- create / edit serial
function openCreate() {
  creating.value = { item: null, serialNumber: "", workOrderId: null, description: "", applyRouting: true };
}

async function doCreate() {
  const c = creating.value;
  if (!c.item) return fail("کالا را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await createSerial({ itemId: c.item.id, serialNumber: c.serialNumber, workOrderId: c.workOrderId, description: c.description, applyRouting: c.applyRouting });
  busy.value = false;
  if (error) return fail(error);
  ok(`سریال ${data.serialNumber} ساخته شد.`);
  creating.value = null;
  openSerial(data.id);
}

async function doEditSerial() {
  const e = editSerial.value;
  const [, error] = await updateSerial({ id: serial.value.unit.id, workOrderId: e.workOrderId, description: e.description });
  if (error) return fail(error);
  editSerial.value = null;
  load();
}

async function doEditLot() {
  const e = editLot.value;
  busy.value = true;
  if (e.file) {
    const [id, error] = await uploadFile(e.file, `گواهی ${e.lotNumber}`);
    if (error) {
      busy.value = false;
      return fail(error);
    }
    e.certificateDocumentId = id;
  }
  const [, error] = await saveLot({ ...e, file: undefined });
  busy.value = false;
  if (error) return fail(error);
  editLot.value = null;
  load();
}

function printReport() {
  window.print();
}

function printLabel() {
  if (serial.value) {
    const u = serial.value.unit;
    openLabels([{ code: labelCode.serial(u.itemCode, u.serialNumber), title: u.itemTitle, lines: [u.itemCode, "S/N " + u.serialNumber] }], router);
  } else if (lot.value) {
    const l = lot.value.lot;
    openLabels([{ code: labelCode.lot(l.itemCode, l.lotNumber), title: l.itemTitle, lines: [l.itemCode, "Lot " + l.lotNumber, l.heatNumber ? "Heat " + l.heatNumber : ""].filter(Boolean) }], router);
  }
}

onMounted(() => {
  getMachines().then(([m]) => (machines.value = rowsOf(m)));
  getWorkOrders().then(([w]) => (workOrders.value = rowsOf(w)));
  getProviders().then(([p]) => (providers.value = rowsOf(p)));
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader
      v-if="serial"
      :code="serial.unit.serialNumber"
      :subtitle="`${serial.unit.itemCode} - ${serial.unit.itemTitle}`"
      title="شناسنامه ردیابی قطعه" />
    <KsReportHeader v-if="lot" :code="lot.lot.lotNumber" :subtitle="`${lot.lot.itemCode} - ${lot.lot.itemTitle}`" title="شناسنامه ردیابی بچ مواد" />
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">ردیابی قطعات</h1>
        <button class="btn btn-sm btn-primary" @click="openCreate">ساخت سریال جدید (قطعه یا مجموعه تولیدی)</button>
      </div>
      <div class="d-flex gap-2">
        <input
          v-model="q"
          class="form-control inv-scan"
          placeholder="اسکن برچسب یا جستجوی سریال، بچ، شماره ذوب، کد کالا"
          @keydown.enter.prevent="search" />
        <button class="btn btn-secondary" @click="search">جستجو</button>
      </div>

      <div v-if="results" class="mt-3">
        <div v-if="results.serials.length">
          <b class="small">قطعات سریالی</b>
          <ul class="list-group mb-2">
            <li v-for="s in results.serials" :key="s.id" class="list-group-item list-group-item-action py-1 small" role="button" @click="openSerial(s.id)">
              <span class="inv-ltr">{{ s.serialNumber }}</span> — {{ s.itemCode }} {{ s.itemTitle }}
              <span class="inv-badge ms-2" :class="statusClass(s.status)">{{ serialStatuses[s.status] }}</span>
            </li>
          </ul>
        </div>
        <div v-if="results.lots.length">
          <b class="small">بچ‌ها</b>
          <ul class="list-group mb-2">
            <li v-for="l in results.lots" :key="l.id" class="list-group-item list-group-item-action py-1 small" role="button" @click="openLot(l.id)">
              <span class="inv-ltr">{{ l.lotNumber }}</span> (ذوب {{ l.heatNumber || "-" }}) — {{ l.itemCode }} {{ l.itemTitle }} · موجودی {{ num(l.onHand) }}
            </li>
          </ul>
        </div>
        <div v-if="results.items.length">
          <b class="small">کالاها</b>
          <ul class="list-group mb-2">
            <li v-for="i in results.items" :key="i.id" class="list-group-item py-1 small">
              <span class="inv-ltr">{{ i.code }}</span> — {{ i.title }} · موجودی {{ num(i.onHand) }} {{ i.unit }}
              <router-link :to="{ name: 'InventoryKardex', query: { itemId: i.id } }" class="ms-2">کاردکس</router-link>
            </li>
          </ul>
        </div>
        <div v-if="results.locations.length">
          <b class="small">محل‌ها</b>
          <ul class="list-group">
            <li v-for="l in results.locations" :key="l.id" class="list-group-item py-1 small">{{ l.warehouseCode }} / <span class="inv-ltr">{{ l.code }}</span> {{ l.title }}</li>
          </ul>
        </div>
      </div>
    </div>

    <!-- ===================== serial ===================== -->
    <template v-if="serial">
      <div class="inv-card">
        <div class="inv-toolbar">
          <div>
            <h2 class="inv-title">
              <span class="inv-ltr">{{ serial.unit.serialNumber }}</span> — {{ serial.unit.itemTitle }}
              <span class="inv-muted inv-ltr">{{ serial.unit.itemCode }}</span>
            </h2>
            <span class="inv-badge" :class="statusClass(serial.unit.status)">{{ serialStatuses[serial.unit.status] }}</span>
          </div>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" @click="printReport">چاپ شناسنامه</button>
            <button class="btn btn-sm btn-outline-secondary" @click="printLabel">چاپ برچسب</button>
            <button class="btn btn-sm btn-outline-secondary" @click="editSerial = { workOrderId: serial.unit.workOrderId, description: serial.unit.description }">ویرایش</button>
            <button v-if="serial.unit.status === 1" class="btn btn-sm btn-primary" @click="openInstall">نصب قطعه در این مجموعه</button>
          </div>
        </div>
        <div class="row small g-2">
          <div class="col-md-3">انبار / محل: {{ serial.unit.warehouseTitle || "-" }} <span class="inv-ltr">{{ serial.unit.locationCode }}</span></div>
          <div class="col-md-3">دستور ساخت: {{ serial.unit.workOrderCode || "-" }}</div>
          <div class="col-md-3">
            بچ مواد:
            <a v-if="serial.lot" href="#" @click.prevent="openLot(serial.lot.id)">{{ serial.lot.lotNumber }}</a>
            <span v-if="serial.lot"> (ذوب {{ serial.lot.heatNumber || "-" }}، گواهی {{ serial.lot.certificateNumber || "-" }})</span>
            <span v-else>-</span>
          </div>
          <div class="col-md-3">ثبت: {{ jalali(serial.unit.createDate, true) }}</div>
          <div v-if="serial.unit.description" class="col-12 inv-muted">{{ serial.unit.description }}</div>
        </div>
        <div v-if="serial.ancestors.length" class="mt-2 small">
          نصب‌شده در:
          <template v-for="(a, i) in serial.ancestors" :key="a.id">
            <span v-if="i"> ← </span>
            <a href="#" @click.prevent="openSerial(a.id)">{{ a.itemTitle }} <span class="inv-ltr">{{ a.serialNumber }}</span></a>
          </template>
        </div>
      </div>

      <div class="row g-3">
        <div class="col-lg-6">
          <div class="inv-card">
            <OperationsPanel :machines="machines" :operations="serial.operations" :serial-unit-id="serial.unit.id" @changed="load" />
          </div>
        </div>
        <div class="col-lg-6">
          <div class="inv-card">
            <h3 class="inv-title mb-2" style="font-size: 1rem">قطعات نصب‌شده در این مجموعه</h3>
            <table class="table table-sm">
              <thead><tr><th>قطعه</th><th>سریال / بچ</th><th>مقدار</th><th>نصب</th><th></th></tr></thead>
              <tbody>
                <tr v-if="!serial.children.length && !serial.lotComponents.length"><td class="inv-muted" colspan="5">قطعه‌ای نصب نشده است.</td></tr>
                <template v-for="h in installedLinks" :key="h.id">
                  <tr>
                    <td>{{ h.childItemTitle }} <span class="inv-muted inv-ltr">{{ h.childItemCode }}</span></td>
                    <td>
                      <a v-if="h.childSerialUnitId" class="inv-ltr" href="#" @click.prevent="openSerial(h.childSerialUnitId)">{{ h.childSerialNumber }}</a>
                      <a v-else class="inv-ltr" href="#" @click.prevent="openLot(h.childLotId)">{{ h.childLotNumber }}</a>
                    </td>
                    <td>{{ num(h.quantity) }}</td>
                    <td>{{ jalali(h.installedAt) }} {{ h.installedByName }}</td>
                    <td><button class="btn btn-sm btn-link text-danger p-0" @click="doRemove(h)">باز کردن</button></td>
                  </tr>
                </template>
              </tbody>
            </table>
            <h3 class="inv-title mb-2 mt-3" style="font-size: 1rem">سابقه نصب و باز کردن</h3>
            <table class="table table-sm">
              <thead><tr><th>مجموعه</th><th>قطعه</th><th>نصب</th><th>باز شد</th></tr></thead>
              <tbody>
                <tr v-if="!serial.assemblyHistory.length"><td class="inv-muted" colspan="4">-</td></tr>
                <tr v-for="h in serial.assemblyHistory" :key="h.id" :class="{ 'opacity-50': h.removedAt }">
                  <td><span class="inv-ltr">{{ h.parentSerialNumber }}</span> {{ h.parentItemTitle }}</td>
                  <td><span class="inv-ltr">{{ h.childSerialNumber || h.childLotNumber }}</span> {{ h.childItemTitle }}</td>
                  <td>{{ jalali(h.installedAt) }}</td>
                  <td>{{ h.removedAt ? jalali(h.removedAt) + " " + (h.removedByName || "") : "" }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div class="inv-card">
        <h3 class="inv-title mb-2" style="font-size: 1rem">گردش انبار این قطعه</h3>
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>سند</th><th>نوع</th><th>انبار / محل</th><th>مقدار</th><th>تامین‌کننده / دستور ساخت</th></tr></thead>
          <tbody>
            <tr v-if="!serial.movements.length"><td class="inv-muted" colspan="6">گردشی ثبت نشده (قطعه در کارگاه ساخته شده است).</td></tr>
            <tr v-for="(m, i) in serial.movements" :key="i" :class="{ 'text-danger': m.isReversal }">
              <td>{{ jalali(m.date) }}</td>
              <td><router-link :to="{ name: 'InventoryDocumentEdit', params: { id: m.documentId } }" class="inv-ltr">{{ m.documentNumber }}</router-link></td>
              <td>{{ documentTypes[m.documentType] }}<span v-if="m.isReversal"> (ابطال)</span></td>
              <td>{{ m.warehouseTitle }} <span class="inv-ltr">{{ m.locationCode }}</span></td>
              <td>{{ num(m.quantity) }}</td>
              <td>{{ m.providerName || m.workOrderCode }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>

    <!-- ===================== lot ===================== -->
    <template v-if="lot">
      <div class="inv-card">
        <div class="inv-toolbar">
          <div>
            <h2 class="inv-title">بچ <span class="inv-ltr">{{ lot.lot.lotNumber }}</span> — {{ lot.lot.itemTitle }} <span class="inv-muted inv-ltr">{{ lot.lot.itemCode }}</span></h2>
          </div>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" @click="printReport">چاپ شناسنامه</button>
            <button class="btn btn-sm btn-outline-secondary" @click="printLabel">چاپ برچسب</button>
            <button class="btn btn-sm btn-outline-secondary" @click="editLot = { ...lot.lot, file: null }">ویرایش گواهی</button>
          </div>
        </div>
        <div class="row small g-2">
          <div class="col-md-3">شماره ذوب: <span class="inv-ltr">{{ lot.lot.heatNumber || "-" }}</span></div>
          <div class="col-md-3">
            گواهی: {{ lot.lot.certificateType }} <span class="inv-ltr">{{ lot.lot.certificateNumber || "-" }}</span>
            <a v-if="lot.lot.certificateDocumentId" class="ms-1" href="#" @click.prevent="downloadFile(lot.lot.certificateDocumentId, 'certificate-' + lot.lot.lotNumber)">دانلود</a>
          </div>
          <div class="col-md-3">تامین‌کننده: {{ lot.lot.providerName || "-" }}</div>
          <div class="col-md-3">موجودی: <b>{{ num(lot.lot.onHand) }}</b> {{ lot.lot.unit }}</div>
        </div>
      </div>
      <div class="row g-3">
        <div class="col-lg-6">
          <div class="inv-card">
            <h3 class="inv-title mb-2" style="font-size: 1rem">کجا مصرف شد (Where-used)</h3>
            <table class="table table-sm">
              <thead><tr><th>مجموعه</th><th>مقدار</th><th>تاریخ</th></tr></thead>
              <tbody>
                <tr v-if="!lot.usedIn.length"><td class="inv-muted" colspan="3">در مجموعه‌ای نصب نشده است.</td></tr>
                <tr v-for="u in lot.usedIn" :key="u.id" :class="{ 'opacity-50': u.removedAt }">
                  <td><a class="inv-ltr" href="#" @click.prevent="openSerial(u.parentSerialUnitId)">{{ u.parentSerialNumber }}</a> {{ u.parentItemTitle }}</td>
                  <td>{{ num(u.quantity) }}</td>
                  <td>{{ jalali(u.installedAt) }}</td>
                </tr>
              </tbody>
            </table>
            <div v-if="lot.topAssemblies.length" class="small">
              محصولات نهایی شامل این بچ:
              <a v-for="t in lot.topAssemblies" :key="t.id" class="ms-2 inv-ltr" href="#" @click.prevent="openSerial(t.id)">{{ t.serialNumber }}</a>
            </div>
            <h3 class="inv-title mb-2 mt-3" style="font-size: 1rem">قطعات سریالی ساخته‌شده از این بچ</h3>
            <div v-if="!lot.serials.length" class="inv-muted small">-</div>
            <a v-for="s in lot.serials" :key="s.id" class="me-2 inv-ltr small" href="#" @click.prevent="openSerial(s.id)">{{ s.serialNumber }}</a>
            <h3 class="inv-title mb-2 mt-3" style="font-size: 1rem">موجودی به تفکیک محل</h3>
            <table class="table table-sm">
              <tbody>
                <tr v-for="(b, i) in lot.balances" :key="i"><td>{{ b.warehouseTitle }}</td><td class="inv-ltr">{{ b.locationCode }}</td><td>{{ num(b.quantity) }}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="col-lg-6">
          <div class="inv-card">
            <OperationsPanel :lot-id="lot.lot.id" :machines="machines" :operations="lot.operations" @changed="load" />
          </div>
        </div>
      </div>
      <div class="inv-card">
        <h3 class="inv-title mb-2" style="font-size: 1rem">گردش انبار این بچ</h3>
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>سند</th><th>نوع</th><th>انبار / محل</th><th>مقدار</th><th>تامین‌کننده / دستور ساخت</th></tr></thead>
          <tbody>
            <tr v-for="(m, i) in lot.movements" :key="i" :class="{ 'text-danger': m.isReversal }">
              <td>{{ jalali(m.date) }}</td>
              <td><router-link :to="{ name: 'InventoryDocumentEdit', params: { id: m.documentId } }" class="inv-ltr">{{ m.documentNumber }}</router-link></td>
              <td>{{ documentTypes[m.documentType] }}</td>
              <td>{{ m.warehouseTitle }} <span class="inv-ltr">{{ m.locationCode }}</span></td>
              <td>{{ num(m.quantity) }}</td>
              <td>{{ m.providerName || m.workOrderCode }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>

    <!-- install -->
    <div v-if="install" class="inv-modal-backdrop" @click.self="install = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">نصب قطعه در {{ serial.unit.itemTitle }} <span class="inv-ltr">{{ serial.unit.serialNumber }}</span></h2>
        <p class="small">برچسب قطعه سریالی (در حال ساخت) یا بچ ماده را اسکن کنید.</p>
        <div class="d-flex gap-2 mb-2">
          <input v-model="install.text" class="form-control form-control-sm inv-scan" placeholder="S/... یا L/..." @keydown.enter.prevent="findChild" />
          <button class="btn btn-sm btn-secondary" @click="findChild">یافتن</button>
        </div>
        <div v-if="install.child" class="alert alert-info small py-1">قطعه: {{ install.child.itemTitle }} <span class="inv-ltr">{{ install.child.serialNumber }}</span> ({{ serialStatuses[install.child.status] }})</div>
        <div v-if="install.lot" class="alert alert-info small py-1">
          بچ: {{ install.lot.itemTitle }} <span class="inv-ltr">{{ install.lot.lotNumber }}</span>
          <div class="mt-1">مقدار مصرف: <input v-model.number="install.quantity" class="form-control form-control-sm d-inline-block" style="width: 8rem" type="number" /> {{ install.lot.unit }}</div>
        </div>
        <input v-model="install.notes" class="form-control form-control-sm" placeholder="یادداشت" />
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="install = null">انصراف</button>
          <button :disabled="busy || (!install.child && !install.lot)" class="btn btn-sm btn-primary" @click="doInstall">نصب</button>
        </div>
      </div>
    </div>

    <!-- create serial -->
    <div v-if="creating" class="inv-modal-backdrop" @click.self="creating = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">ساخت سریال جدید</h2>
        <p class="small">برای قطعه یا مجموعه‌ای که در کارگاه ساخته می‌شود (مثل روتور یا پوسته). قطعات خریدنی با رسید انبار سریال می‌گیرند.</p>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small">کالا (سریالی) *</label><ItemPicker v-model="creating.item" /></div>
          <div class="col-md-6"><label class="form-label small">شماره سریال</label><input v-model="creating.serialNumber" class="form-control form-control-sm inv-ltr" placeholder="خالی = خودکار" /></div>
          <div class="col-md-6">
            <label class="form-label small">دستور ساخت</label>
            <select v-model="creating.workOrderId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
            </select>
          </div>
          <div class="col-12"><label class="form-label small">توضیح</label><input v-model="creating.description" class="form-control form-control-sm" /></div>
          <div class="col-12 form-check ms-2"><input id="cr" v-model="creating.applyRouting" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="cr">مسیر ساخت تعریف‌شده را اضافه کن</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="creating = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="doCreate">ساخت</button>
        </div>
      </div>
    </div>

    <!-- edit serial -->
    <div v-if="editSerial" class="inv-modal-backdrop" @click.self="editSerial = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">ویرایش سریال</h2>
        <label class="form-label small">دستور ساخت</label>
        <select v-model="editSerial.workOrderId" class="form-select form-select-sm mb-2">
          <option :value="null">-</option>
          <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
        </select>
        <label class="form-label small">توضیح</label>
        <textarea v-model="editSerial.description" class="form-control form-control-sm" rows="2"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="editSerial = null">انصراف</button>
          <button class="btn btn-sm btn-primary" @click="doEditSerial">ذخیره</button>
        </div>
      </div>
    </div>

    <!-- edit lot -->
    <div v-if="editLot" class="inv-modal-backdrop" @click.self="editLot = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">بچ {{ editLot.lotNumber }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small">شماره ذوب</label><input v-model="editLot.heatNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small">نوع گواهی</label><input v-model="editLot.certificateType" class="form-control form-control-sm inv-ltr" placeholder="EN 10204 3.1" /></div>
          <div class="col-md-6"><label class="form-label small">شماره گواهی</label><input v-model="editLot.certificateNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6">
            <label class="form-label small">تامین‌کننده</label>
            <select v-model="editLot.providerId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option>
            </select>
          </div>
          <div class="col-12"><label class="form-label small">فایل گواهی (PDF)</label><input class="form-control form-control-sm" type="file" @change="onLotFile" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="editLot = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="doEditLot">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
