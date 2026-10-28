<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import { getInspections, getWarehouses, recordInspection, rowsOf, uploadFile } from "@/api/InventoryAPIs";
import { getEquipmentList } from "@/api/QualityAPIs";
import {
  distributionText,
  documentTypes,
  fail,
  inspectionStatuses,
  jalali,
  num,
  ok,
  printPage,
  warehouseKinds,
} from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

// Incoming inspection queue: goods received into the quarantine warehouse wait here for the quality control result.
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const status = ref<number | null>(0);
const contains = ref("");
const warehouses = ref<any[]>([]);
const form = ref<any>(null);
const saving = ref(false);
const printing = ref<any>(null);
const canRecord = hasRole(InventoryRoles.qc);
// measuring instruments (quality module): an inspection names the instruments used and every measured value names its own instrument; the API refuses an instrument
// whose calibration is not valid today (overdue or out of service), so only the usable ones are offered
const equipment = ref<any[]>([]);
const usableEquipment = computed(() => equipment.value.filter((e) => e.status !== 2 && e.status !== 3));
const equipmentLabel = (id: number) => {
  const e = equipment.value.find((x) => x.id === id);
  return e ? `${e.code} ${e.name}` : "#" + id;
};

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));
// the accepted goods are released to a normal warehouse, not to quarantine, scrap or the contractor
const targetWarehouses = computed(() => warehouses.value.filter((w) => w.isActive && ![5, 6, 7].includes(w.kind)));
const targetLocations = computed(() => warehouses.value.find((w) => w.id === form.value?.targetWarehouseId)?.locations || []);

async function load() {
  const [data, error] = await getInspections({ Status: status.value, Contains: contains.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
}

function search() {
  page.value = 1;
  load();
}

function openResult(row: any) {
  form.value = {
    row,
    id: row.id,
    acceptedQuantity: row.quantity,
    rejectedQuantity: 0,
    rejected: [] as string[],
    targetWarehouseId: row.targetWarehouseId,
    targetLocationId: null,
    reportNumber: "",
    reportDocumentId: null,
    note: "",
    rejectReason: "",
    equipmentIds: [] as number[],
    measurements: [] as any[],
  };
}

function addMeasurement() {
  form.value.measurements.push({ characteristic: "", nominal: null, tolPlus: 0, tolMinus: 0, measured: null, unit: "", equipmentId: form.value.equipmentIds[0] ?? null });
}

// the display of the verdict only; the API decides (a value out of tolerance cannot be accepted)
function passes(m: any) {
  if (m.nominal === null || m.nominal === "" || m.measured === null || m.measured === "") return null;
  const round = (v: number) => Math.round(v * 1e9) / 1e9;
  const measured = round(Number(m.measured));
  return measured >= round(Number(m.nominal) - (Number(m.tolMinus) || 0)) && measured <= round(Number(m.nominal) + (Number(m.tolPlus) || 0));
}
const hasFailedMeasurement = computed(() => !!form.value && form.value.measurements.some((m: any) => passes(m) === false));

// an instrument that is ticked stays selectable for the values; a value whose instrument was un-ticked keeps it (the API still checks it)
function measurementsBody(): any[] | null {
  const out: any[] = [];
  for (const m of form.value.measurements) {
    const name = String(m.characteristic || "").trim();
    const numbers = [m.nominal, m.measured, m.tolPlus, m.tolMinus];
    if (!name) {
      fail("نام مشخصه اندازه‌گیری‌شده را وارد کنید.");
      return null;
    }
    if (numbers.some((v) => v === null || v === "" || !Number.isFinite(Number(v)))) {
      fail(`مقدار اسمی، تلرانس‌ها و مقدار اندازه‌گیری‌شده «${name}» را به عدد وارد کنید.`);
      return null;
    }
    if (Number(m.tolPlus) < 0 || Number(m.tolMinus) < 0) {
      fail("تلرانس‌ها را به صورت مقدار مثبت وارد کنید (+ و −).");
      return null;
    }
    if (!m.equipmentId) {
      fail(`برای اندازه‌گیری «${name}» ابزار اندازه‌گیری به‌کاررفته را انتخاب کنید.`);
      return null;
    }
    out.push({
      characteristic: name,
      nominal: Number(m.nominal),
      tolPlus: Number(m.tolPlus),
      tolMinus: Number(m.tolMinus),
      measured: Number(m.measured),
      unit: String(m.unit || "").trim() || null,
      equipmentId: Number(m.equipmentId),
    });
  }
  return out;
}

// serial items: the rejected pieces are ticked one by one
function toggleSerial(serial: string) {
  const f = form.value;
  const at = f.rejected.indexOf(serial);
  if (at >= 0) f.rejected.splice(at, 1);
  else f.rejected.push(serial);
  f.rejectedQuantity = f.rejected.length;
  f.acceptedQuantity = f.row.quantity - f.rejected.length;
}

function recount() {
  const f = form.value;
  f.rejectedQuantity = Math.max(0, Number(f.row.quantity) - (Number(f.acceptedQuantity) || 0));
}

async function attach(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files && input.files[0];
  if (!file) return;
  const [id, error] = await uploadFile(file, "گزارش بازرسی " + form.value.row.number);
  input.value = "";
  if (error) return fail(error);
  form.value.reportDocumentId = id;
  ok("گزارش بازرسی پیوست شد.");
}

async function save() {
  const f = form.value;
  const measurements = measurementsBody();
  if (!measurements) return;
  saving.value = true;
  const [data, error] = await recordInspection({
    id: f.id,
    acceptedQuantity: Number(f.acceptedQuantity) || 0,
    rejectedQuantity: Number(f.rejectedQuantity) || 0,
    rejectedSerialNumbers: f.rejected,
    targetWarehouseId: f.targetWarehouseId,
    targetLocationId: f.targetLocationId,
    reportNumber: f.reportNumber,
    reportDocumentId: f.reportDocumentId,
    note: f.note,
    rejectReason: f.rejectReason,
    // the API takes "measured" from the values (the flag is ignored there); the instruments are checked for a valid calibration on the day
    measured: measurements.length > 0,
    equipmentIds: f.equipmentIds,
    measurements,
  });
  saving.value = false;
  if (error) return fail(error);
  form.value = null;
  ok(`نتیجه ثبت شد${data.releaseDocumentNumber ? "؛ ترخیص " + data.releaseDocumentNumber : ""}${data.ncrNumber ? "؛ گزارش عدم انطباق " + data.ncrNumber : ""}.`);
  load();
}

async function printReport(row: any) {
  printing.value = row;
  await nextTick();
  window.print();
  printing.value = null;
}

onMounted(async () => {
  const [w] = await getWarehouses();
  warehouses.value = w || [];
  load();
  // the list is read by the quality readers; a user outside them gets an error that is not worth showing here
  getEquipmentList({ PageSize: 300 }).then(([list]) => (equipment.value = rowsOf(list)));
});
</script>

<template>
  <div class="inv-page ks-report">
    <PrintForm
      v-if="printing"
      title="گزارش بازرسی ورودی کالا"
      :fields="[
        { label: 'شماره بازرسی', value: printing.number, ltr: true },
        { label: 'تاریخ بازرسی', value: jalali(printing.inspectedAt) },
        { label: 'رسید', value: printing.receiptNumber + ' (' + documentTypes[printing.receiptType] + ')', ltr: true },
        { label: 'تاریخ رسید', value: jalali(printing.receiptDate) },
        { label: 'تامین‌کننده', value: printing.providerName || '-' },
        { label: 'شماره گزارش بازرسی', value: printing.reportNumber || '-' },
        ...(printing.reinspectionOfNcrNumber ? [{ label: 'بازرسی مجدد پس از اصلاح گزارش', value: printing.reinspectionOfNcrNumber, ltr: true }] : []),
        ...(printing.equipmentIds && printing.equipmentIds.length ? [{ label: 'ابزار اندازه‌گیری', value: printing.equipmentIds.map(equipmentLabel).join('، '), ltr: true }] : []),
      ]"
      :notes="printing.note ? [{ label: 'توضیح', value: printing.note }] : []"
      :signatures="[
        { title: 'بازرس کنترل کیفیت', name: printing.inspectedByName, date: jalali(printing.inspectedAt) },
        { title: 'مدیر کنترل کیفیت' },
        { title: 'انباردار' },
      ]"
      :distribution="distributionText">
      <table>
        <thead>
          <tr>
            <th>کد کالا</th>
            <th>نام کالا</th>
            <th>بچ / ذوب</th>
            <th>مقدار رسید</th>
            <th>قبول</th>
            <th>رد</th>
            <th>ترخیص به</th>
            <th>گزارش عدم انطباق</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td class="inv-ltr">{{ printing.itemCode }}</td>
            <td>{{ printing.itemTitle }}</td>
            <td class="inv-ltr">{{ printing.lotNumber }} {{ printing.heatNumber }}</td>
            <td class="num">{{ num(printing.quantity) }} {{ printing.unit }}</td>
            <td class="num">{{ num(printing.acceptedQuantity) }}</td>
            <td class="num">
              {{ num(printing.rejectedQuantity) }}
              <span v-if="printing.rejectedSerialNumbers.length" class="inv-ltr">({{ printing.rejectedSerialNumbers.join("، ") }})</span>
            </td>
            <td>
              {{ printing.targetWarehouseTitle || "-" }}
              <span class="inv-ltr">{{ printing.releaseDocumentNumber }}</span>
            </td>
            <td class="inv-ltr">{{ printing.ncrNumber || "-" }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="printing.serialNumbers.length" class="small m-1">
        <b>سریال‌ها:</b> <span class="inv-ltr">{{ printing.serialNumbers.join("، ") }}</span>
      </p>
      <table v-if="printing.measurements && printing.measurements.length" class="mt-1">
        <thead>
          <tr>
            <th>مشخصه</th>
            <th>اسمی</th>
            <th>+ تلرانس</th>
            <th>− تلرانس</th>
            <th>اندازه‌گیری‌شده</th>
            <th>واحد</th>
            <th>ابزار</th>
            <th>نتیجه</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(m, i) in printing.measurements" :key="i">
            <td>{{ m.characteristic }}</td>
            <td class="num">{{ num(m.nominal) }}</td>
            <td class="num">{{ num(m.tolPlus) }}</td>
            <td class="num">{{ num(m.tolMinus) }}</td>
            <td class="num">{{ num(m.measured) }}</td>
            <td>{{ m.unit }}</td>
            <td class="inv-ltr">{{ m.equipmentId ? equipmentLabel(m.equipmentId) : "-" }}</td>
            <td>{{ m.pass ? "قبول" : "رد" }}</td>
          </tr>
        </tbody>
      </table>
    </PrintForm>

    <div :class="{ 'screen-only': printing }">
      <KsReportHeader title="صف بازرسی ورودی (قرنطینه)" :subtitle="status === null ? 'همه' : inspectionStatuses[status]" />
      <div class="inv-card">
        <div class="inv-toolbar">
          <h1 class="inv-title screen-only">قرنطینه و بازرسی ورودی</h1>
          <button class="btn btn-sm btn-outline-secondary d-print-none" @click="printPage">چاپ فهرست</button>
        </div>
        <p class="inv-muted screen-only">
          کالای خریداری‌شده، برگشتی از پیمانکار و اموال مشتری ابتدا در انبار قرنطینه رسید می‌شود. تا نتیجه بازرسی ثبت نشود از قرنطینه خارج نمی‌شود. مقدار
          قبول‌شده با سند «ترخیص از قرنطینه» به انبار مقصد می‌رود؛ مقدار ردشده گزارش عدم انطباق (NCR) می‌سازد.
        </p>
        <div class="inv-filters mb-3">
          <select v-model="status" class="form-select form-select-sm" @change="search">
            <option :value="null">همه</option>
            <option v-for="(t, k) in inspectionStatuses" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
          <input v-model="contains" class="form-control form-control-sm" placeholder="کالا، رسید، سریال، بچ" @keydown.enter="search" />
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead>
              <tr>
                <th>شماره</th>
                <th>رسید</th>
                <th>تاریخ</th>
                <th>تامین‌کننده</th>
                <th>کالا</th>
                <th>بچ</th>
                <th>مقدار</th>
                <th>قبول / رد</th>
                <th>وضعیت</th>
                <th class="d-print-none"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="!rows.length">
                <td class="text-center inv-muted" colspan="10">موردی نیست.</td>
              </tr>
              <tr v-for="r in rows" :key="r.id">
                <td class="inv-ltr">
                  {{ r.number }}
                  <div v-if="r.reinspectionOfNcrId" class="small">
                    <span class="inv-badge info">بازرسی مجدد</span>
                    <router-link :to="{ name: 'InventoryNcrEdit', params: { id: r.reinspectionOfNcrId } }" class="inv-ltr ms-1" :title="'بازرسی مجدد پس از اصلاح گزارش عدم انطباق ' + r.reinspectionOfNcrNumber">{{ r.reinspectionOfNcrNumber }}</router-link>
                  </div>
                </td>
                <td>
                  <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: r.receiptDocumentId } }" class="inv-ltr">{{ r.receiptNumber }}</router-link>
                </td>
                <td>{{ jalali(r.receiptDate) }}</td>
                <td>{{ r.providerName }}</td>
                <td>
                  <span class="inv-ltr">{{ r.itemCode }}</span> — {{ r.itemTitle }}
                </td>
                <td class="inv-ltr">{{ r.lotNumber }}</td>
                <td class="num">{{ num(r.quantity) }} {{ r.unit }}</td>
                <td>
                  <template v-if="r.status === 1">{{ num(r.acceptedQuantity) }} / {{ num(r.rejectedQuantity) }}</template>
                </td>
                <td>
                  <span class="inv-badge" :class="r.status === 1 ? 'ok' : 'warn'">{{ inspectionStatuses[r.status] }}</span>
                  <router-link v-if="r.ncrId" :to="{ name: 'InventoryNcrEdit', params: { id: r.ncrId } }" class="inv-ltr ms-1 small">{{ r.ncrNumber }}</router-link>
                </td>
                <td class="d-print-none text-nowrap">
                  <button v-if="r.status === 0 && canRecord" class="btn btn-sm btn-primary py-0" @click="openResult(r)">ثبت نتیجه</button>
                  <button v-else-if="r.status === 1" class="btn btn-sm btn-outline-secondary py-0" @click="printReport(r)">چاپ گزارش</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="inv-pager">
          <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; load()">قبلی</button>
          <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} مورد)</span>
          <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; load()">بعدی</button>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal wide" style="max-width: 860px">
        <h2 class="inv-title mb-2">نتیجه بازرسی {{ form.row.number }}</h2>
        <p class="small mb-2">
          <span class="inv-ltr">{{ form.row.itemCode }}</span> — {{ form.row.itemTitle }} · {{ num(form.row.quantity) }} {{ form.row.unit }} · رسید
          <span class="inv-ltr">{{ form.row.receiptNumber }}</span>
        </p>
        <p v-if="form.row.reinspectionOfNcrId" class="small mb-2">
          <span class="inv-badge info">بازرسی مجدد</span>
          کالای برگشته از دوباره‌کاری یا تعمیر گزارش
          <router-link :to="{ name: 'InventoryNcrEdit', params: { id: form.row.reinspectionOfNcrId } }" class="inv-ltr">{{ form.row.reinspectionOfNcrNumber }}</router-link>
          است؛ گزارش فقط با قبول‌شدن این بازرسی بسته می‌شود.
        </p>
        <div v-if="form.row.serialNumbers.length" class="mb-2">
          <div class="small mb-1">قطعات مردود را علامت بزنید:</div>
          <label v-for="s in form.row.serialNumbers" :key="s" class="form-check form-check-inline small">
            <input :checked="form.rejected.includes(s)" class="form-check-input" type="checkbox" @change="toggleSerial(s)" />
            <span class="inv-ltr">{{ s }}</span>
          </label>
        </div>
        <div class="row g-2">
          <div class="col-6">
            <label class="form-label small mb-0">مقدار قبول</label>
            <input
              v-model.number="form.acceptedQuantity"
              :disabled="form.row.serialNumbers.length > 0"
              class="form-control form-control-sm"
              type="number"
              @input="recount" />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">مقدار رد</label>
            <input :value="form.rejectedQuantity" class="form-control form-control-sm" disabled />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">انبار مقصد کالای قبول‌شده</label>
            <select v-model="form.targetWarehouseId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="w in targetWarehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }} ({{ warehouseKinds[w.kind] }})</option>
            </select>
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">محل</label>
            <select v-model="form.targetLocationId" class="form-select form-select-sm">
              <option :value="null">بدون محل</option>
              <option v-for="l in targetLocations" :key="l.id" :value="l.id">{{ l.code }}</option>
            </select>
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">شماره گزارش بازرسی</label>
            <input v-model="form.reportNumber" class="form-control form-control-sm" />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">پیوست گزارش</label>
            <input class="form-control form-control-sm" type="file" @change="attach" />
          </div>
          <div v-if="form.rejectedQuantity > 0" class="col-12">
            <label class="form-label small mb-0">شرح عدم انطباق (برای NCR) *</label>
            <textarea v-model="form.rejectReason" class="form-control form-control-sm" rows="2"></textarea>
          </div>
          <div class="col-12">
            <label class="form-label small mb-0">ابزارهای اندازه‌گیری به‌کاررفته</label>
            <div class="d-flex flex-wrap gap-2">
              <label v-for="e in usableEquipment" :key="e.id" class="form-check small border rounded px-2 py-1">
                <input v-model="form.equipmentIds" :value="e.id" class="form-check-input ms-1" type="checkbox" />
                <span class="inv-ltr">{{ e.code }}</span> {{ e.name }}
                <span v-if="e.status === 1" class="inv-badge warn">نزدیک سررسید</span>
              </label>
              <span v-if="!usableEquipment.length" class="inv-muted small">ابزار اندازه‌گیری با کالیبراسیون معتبر ثبت نشده است.</span>
            </div>
            <span class="inv-muted small">ابزار سررسیدگذشته یا خارج از رده در فهرست نیست؛ کالیبراسیون ابزار در روز ثبت بررسی می‌شود.</span>
          </div>
          <div class="col-12">
            <div class="d-flex justify-content-between align-items-center">
              <label class="form-label small mb-0">مقادیر اندازه‌گیری‌شده (ابعاد، ضخامت، سختی…)</label>
              <button class="btn btn-sm btn-outline-primary py-0" type="button" @click="addMeasurement">+ مقدار</button>
            </div>
            <div v-if="form.measurements.length" class="table-responsive">
              <table class="table table-sm mb-1">
                <thead>
                  <tr>
                    <th>مشخصه *</th>
                    <th>اسمی</th>
                    <th>+ تلرانس</th>
                    <th>− تلرانس</th>
                    <th>اندازه‌گیری‌شده</th>
                    <th>واحد</th>
                    <th>ابزار *</th>
                    <th></th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(m, i) in form.measurements" :key="i">
                    <td><input v-model="m.characteristic" class="form-control form-control-sm" maxlength="200" /></td>
                    <td><input v-model="m.nominal" class="form-control form-control-sm inv-ltr" step="any" type="number" /></td>
                    <td><input v-model="m.tolPlus" class="form-control form-control-sm inv-ltr" min="0" step="any" type="number" /></td>
                    <td><input v-model="m.tolMinus" class="form-control form-control-sm inv-ltr" min="0" step="any" type="number" /></td>
                    <td><input v-model="m.measured" class="form-control form-control-sm inv-ltr" step="any" type="number" /></td>
                    <td><input v-model="m.unit" class="form-control form-control-sm" maxlength="30" style="width: 4.5rem" /></td>
                    <td>
                      <select v-model="m.equipmentId" class="form-select form-select-sm">
                        <option :value="null">-</option>
                        <option v-for="e in usableEquipment" :key="e.id" :value="e.id">{{ e.code }} {{ e.name }}</option>
                      </select>
                    </td>
                    <td class="text-nowrap">
                      <span v-if="passes(m) === true" class="inv-badge ok">در تلرانس</span>
                      <span v-else-if="passes(m) === false" class="inv-badge bad">خارج از تلرانس</span>
                    </td>
                    <td><button class="btn btn-sm btn-outline-danger py-0" type="button" @click="form.measurements.splice(i, 1)">حذف</button></td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p v-else class="inv-muted small mb-0">بازرسی چشمی یا مدرکی مقدار اندازه‌گیری‌شده ندارد؛ برای بازرسی ابعادی هر مقدار را با ابزارش ثبت کنید.</p>
            <p v-if="hasFailedMeasurement && !(form.rejectedQuantity > 0)" class="small text-danger mb-0">
              مقدار خارج از تلرانس ثبت شده است؛ کل مقدار قبول نمی‌شود، مقدار رد را مشخص کنید.
            </p>
          </div>
          <div class="col-12">
            <label class="form-label small mb-0">توضیح</label>
            <input v-model="form.note" class="form-control form-control-sm" />
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-success" @click="save">ثبت نتیجه</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
