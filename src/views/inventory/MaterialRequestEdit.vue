<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import PrintForm from "@/components/print/PrintForm.vue";
import {
  deleteMaterialRequest,
  getMaterialRequest,
  getWarehouses,
  getWorkOrders,
  materialRequestAction,
  rowsOf,
  saveMaterialRequest,
} from "@/api/InventoryAPIs";
import { distributionText, fail, jalali, materialRequestStatuses, num, ok, printPage, today } from "@/composables/inventory";
import { hasRole, InventoryRoles, Roles } from "@/composables/roles";

// A material request: no issue without an approved request. Planning and the manufacturing manager approve.
const route = useRoute();
const router = useRouter();
const request = ref<any>(null);
const warehouses = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const busy = ref(false);
const rejectReason = ref<string | null>(null);

const isNew = computed(() => !request.value?.id);
const editable = computed(() => request.value && request.value.status === 0 && hasRole(InventoryRoles.materialRequest));
const shortages = computed(() => (request.value?.lines || []).filter((l: any) => l.shortage > 0));
const canApprovePlanning = hasRole(["PLANNING", "PMO"]);
const canApproveManufacturing = hasRole([Roles.CONSTRUCTION]);
const canReject = canApprovePlanning || canApproveManufacturing;
const canClose = hasRole(["PLANNING", "PMO", "WAREHOUSE", "WHSUPERVISOR"]);
const canIssue = hasRole(InventoryRoles.documents);

function newLine(item: any = null) {
  return { key: Math.random().toString(36).slice(2), item, quantity: 1, description: "" };
}

function load(dto: any) {
  request.value = {
    ...dto,
    requestDate: (dto.requestDate || "").slice(0, 10),
    lines: dto.lines.map((l: any) => ({
      ...l,
      key: String(l.id),
      item: { id: l.itemId, code: l.itemCode, title: l.itemTitle, unit: l.unit, trackingMode: l.trackingMode },
    })),
  };
}

async function save(silent = false): Promise<boolean> {
  const r = request.value;
  busy.value = true;
  const [data, error] = await saveMaterialRequest({
    id: r.id,
    requestDate: r.requestDate,
    workOrderId: r.workOrderId,
    warehouseId: r.warehouseId,
    purpose: r.purpose,
    lines: r.lines.filter((l: any) => l.item).map((l: any) => ({ itemId: l.item.id, quantity: Number(l.quantity) || 0, description: l.description })),
  });
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  load(data);
  if (!silent) ok("ذخیره شد.");
  if (route.params.id === "new") router.replace({ name: "InventoryMaterialRequestEdit", params: { id: data.id } });
  return true;
}

async function act(action: string) {
  if (action === "submit" && !(await save(true))) return;
  if (action === "close" && !confirm("درخواست بسته شود؟ مانده آن دیگر حواله نمی‌شود.")) return;
  busy.value = true;
  const [data, error] = await materialRequestAction(request.value.id, action, rejectReason.value || "");
  busy.value = false;
  if (error) return fail(error);
  rejectReason.value = null;
  load(data);
  ok("ثبت شد.");
}

async function remove() {
  if (!confirm("پیش‌نویس حذف شود؟")) return;
  const [, error] = await deleteMaterialRequest(request.value.id);
  if (error) return fail(error);
  router.push({ name: "InventoryMaterialRequests" });
}

function issue() {
  router.push({ name: "InventoryDocumentEdit", params: { id: "new" }, query: { type: 4, request: request.value.id } });
}

onMounted(async () => {
  const [w] = await getWarehouses();
  // quarantine, scrap, contractor and customer property stock cannot be requested
  warehouses.value = (w || []).filter((x: any) => x.isActive && ![5, 6, 7, 8].includes(x.kind));
  getWorkOrders().then(([list]) => (workOrders.value = rowsOf(list)));
  if (route.params.id === "new") {
    request.value = { id: null, status: 0, requestDate: today(), workOrderId: null, warehouseId: null, purpose: "", lines: [newLine()] };
  } else {
    const [data, error] = await getMaterialRequest(Number(route.params.id));
    if (error) return fail(error);
    load(data);
  }
});
</script>

<template>
  <div v-if="request" class="inv-page">
    <PrintForm
      title="درخواست کالا از انبار"
      :fields="[
        { label: 'شماره درخواست', value: request.number || 'پیش‌نویس', ltr: true },
        { label: 'تاریخ', value: jalali(request.requestDate) },
        { label: 'دستور ساخت', value: request.workOrderCode || '-' },
        { label: 'انبار', value: request.warehouseTitle || '-' },
      ]"
      :notes="[{ label: 'شرح / محل مصرف', value: request.purpose || '-' }]"
      :signatures="[
        { title: 'درخواست‌کننده', name: request.creatorName },
        { title: 'مدیر برنامه‌ریزی و کنترل پروژه', name: request.planningApprovedByName, date: jalali(request.planningApprovedAt) },
        { title: 'مدیر تولید', name: request.manufacturingApprovedByName, date: jalali(request.manufacturingApprovedAt) },
        { title: 'انباردار' },
      ]"
      :distribution="distributionText">
      <table>
        <thead>
          <tr>
            <th>ردیف</th>
            <th>کد کالا</th>
            <th>شرح</th>
            <th>واحد</th>
            <th>مقدار درخواست</th>
            <th>حواله‌شده</th>
            <th>مانده</th>
            <th>موجودی</th>
            <th>توضیح</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in request.lines" :key="l.key">
            <td class="num">{{ i + 1 }}</td>
            <td class="inv-ltr">{{ l.item?.code }}</td>
            <td>{{ l.item?.title }}</td>
            <td class="num">{{ l.item?.unit }}</td>
            <td class="num">{{ num(l.quantity) }}</td>
            <td class="num">{{ num(l.issuedQuantity) }}</td>
            <td class="num">{{ num(l.remaining) }}</td>
            <td class="num">{{ num(l.available) }}</td>
            <td>{{ l.description }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="request.issues && request.issues.length" class="small m-1">
        حواله‌ها: <span class="inv-ltr">{{ request.issues.join("، ") }}</span>
      </p>
    </PrintForm>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">
            درخواست کالا
            <span v-if="request.number" class="inv-ltr ms-2">{{ request.number }}</span>
          </h1>
          <span class="inv-badge">{{ materialRequestStatuses[request.status] }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2">
          <router-link :to="{ name: 'InventoryMaterialRequests' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="!isNew" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <template v-if="editable">
            <button v-if="!isNew" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="remove">حذف</button>
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save()">ذخیره</button>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="act('submit')">ارسال برای تایید</button>
          </template>
          <template v-if="request.status === 1">
            <button
              v-if="!request.planningApproved && canApprovePlanning"
              :disabled="busy"
              class="btn btn-sm btn-outline-success"
              @click="act('approve-planning')">
              تایید برنامه‌ریزی
            </button>
            <button
              v-if="!request.manufacturingApproved && canApproveManufacturing"
              :disabled="busy"
              class="btn btn-sm btn-outline-success"
              @click="act('approve-manufacturing')">
              تایید مدیر تولید
            </button>
            <button v-if="canReject" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="rejectReason = ''">رد</button>
          </template>
          <template v-if="request.status === 2">
            <button v-if="canIssue" :disabled="busy" class="btn btn-sm btn-primary" @click="issue">صدور حواله</button>
            <button v-if="canClose" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('close')">بستن درخواست</button>
          </template>
        </div>
      </div>

      <div v-if="request.status === 3" class="alert alert-danger small py-2">
        رد شد: {{ request.rejectedByName }} {{ jalali(request.rejectedAt, true) }} — {{ request.rejectReason }}
      </div>

      <div class="row g-2">
        <div class="col-md-2">
          <label class="form-label small mb-0">تاریخ</label>
          <input v-model="request.requestDate" :disabled="!editable" class="form-control form-control-sm" type="date" />
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">دستور ساخت (پروژه)</label>
          <select v-model="request.workOrderId" :disabled="!editable" class="form-select form-select-sm">
            <option :value="null">مصرف عمومی / تعمیرات</option>
            <option v-for="w in workOrders" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div class="col-md-3">
          <label class="form-label small mb-0">از انبار</label>
          <select v-model="request.warehouseId" :disabled="!editable" class="form-select form-select-sm">
            <option :value="null">هر انبار</option>
            <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
          </select>
        </div>
        <div class="col-md-4">
          <label class="form-label small mb-0">شرح / محل مصرف{{ request.workOrderId ? "" : " *" }}</label>
          <input v-model="request.purpose" :disabled="!editable" class="form-control form-control-sm" />
        </div>
      </div>
      <div class="d-flex flex-wrap gap-2 mt-2 small">
        <span class="inv-badge" :class="{ ok: request.planningApproved }">برنامه‌ریزی: {{ request.planningApprovedByName || "—" }}</span>
        <span class="inv-badge" :class="{ ok: request.manufacturingApproved }">مدیر تولید: {{ request.manufacturingApprovedByName || "—" }}</span>
        <span v-if="request.issues && request.issues.length">
          حواله‌ها: <span class="inv-ltr">{{ request.issues.join("، ") }}</span>
        </span>
      </div>
    </div>

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h2 class="inv-title">اقلام</h2>
        <button v-if="editable" class="btn btn-sm btn-outline-primary" @click="request.lines.push(newLine())">+ ردیف</button>
      </div>
      <div v-if="shortages.length" class="alert alert-warning small py-2">
        {{ shortages.length }} قلم کمبود دارد؛ برای آن‌ها درخواست خرید (بخش تدارکات) لازم است.
      </div>
      <table class="table table-sm table-bordered">
        <thead>
          <tr>
            <th>#</th>
            <th style="min-width: 260px">کالا</th>
            <th>مقدار</th>
            <th v-if="!isNew">حواله‌شده</th>
            <th v-if="!isNew">مانده</th>
            <th v-if="!isNew">موجودی قابل حواله</th>
            <th v-if="!isNew">کمبود</th>
            <th>توضیح</th>
            <th v-if="editable"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in request.lines" :key="l.key">
            <td>{{ i + 1 }}</td>
            <td>
              <ItemPicker v-if="editable" v-model="l.item" />
              <span v-else>
                <span class="inv-ltr">{{ l.item?.code }}</span> — {{ l.item?.title }}
              </span>
            </td>
            <td style="width: 110px">
              <input v-model.number="l.quantity" :disabled="!editable" class="form-control form-control-sm" step="any" type="number" />
            </td>
            <td v-if="!isNew" class="num">{{ num(l.issuedQuantity) }}</td>
            <td v-if="!isNew" class="num">{{ num(l.remaining) }}</td>
            <td v-if="!isNew" class="num">{{ num(l.available) }}</td>
            <td v-if="!isNew" class="num" :class="{ 'text-danger fw-bold': l.shortage > 0 }">{{ l.shortage ? num(l.shortage) : "" }}</td>
            <td><input v-model="l.description" :disabled="!editable" class="form-control form-control-sm" /></td>
            <td v-if="editable">
              <button class="btn btn-sm btn-link text-danger p-0" @click="request.lines.splice(i, 1)">×</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="rejectReason !== null" class="inv-modal-backdrop" @click.self="rejectReason = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">رد درخواست {{ request.number }}</h2>
        <textarea v-model="rejectReason" class="form-control form-control-sm" placeholder="علت رد *" rows="3"></textarea>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="rejectReason = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-danger" @click="act('reject')">رد درخواست</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
