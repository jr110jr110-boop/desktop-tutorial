<script setup lang="ts">
import { onMounted, ref } from "vue";
import { approveFreight, getFreight, getFreights, saveFreight, voidFreight } from "@/api/InventoryAPIs";
import { freightBases, freightStatuses, fail, jalali, ok, rial, today } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

// PB5: freight and landed cost. The amount (rial) is spread over the lines of a confirmed purchase receipt (weight for metals, value for the others);
// the accountant approves it (never the person who prepared it); a voided freight reverses the cost.
const rows = ref<any[]>([]);
const loading = ref(false);
const statusFilter = ref<number | null>(null);
const form = ref<any>({ receiptDocumentId: null, freightDate: today(), vehiclePlate: "", goodsType: "", billOfLadingNumber: "", amount: null, description: "" });
const detail = ref<any>(null);
const canApprove = hasRole(InventoryRoles.freightApprovers);

async function load() {
  loading.value = true;
  const [data, error] = await getFreights({ Status: statusFilter.value });
  loading.value = false;
  if (error) return fail(error);
  rows.value = data || [];
}

async function save() {
  const [data, error] = await saveFreight({ ...form.value });
  if (error) return fail(error);
  ok("کرایه ثبت شد؛ سهم هر ردیف رسید در جزئیات است.");
  detail.value = data;
  await load();
}

async function open(id: number) {
  const [data, error] = await getFreight(id);
  if (error) return fail(error);
  detail.value = data;
}

async function approve(id: number) {
  const [data, error] = await approveFreight(id);
  if (error) return fail(error);
  ok("کرایه تایید شد و به بهای کالاها اضافه شد.");
  detail.value = data;
  await load();
}

async function voidIt(id: number) {
  const reason = window.prompt("دلیل ابطال کرایه");
  if (!reason) return;
  const [data, error] = await voidFreight(id, reason);
  if (error) return fail(error);
  ok("کرایه باطل شد.");
  detail.value = data;
  await load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">کرایه حمل و بهای تمام‌شده کالا</h1></div>
      <div class="inv-filters mb-3">
        <input v-model.number="form.receiptDocumentId" class="form-control form-control-sm inv-ltr" placeholder="شناسه رسید خرید تاییدشده" type="number" />
        <input v-model="form.freightDate" class="form-control form-control-sm inv-ltr" type="date" />
        <input v-model="form.vehiclePlate" class="form-control form-control-sm" placeholder="پلاک خودرو" />
        <input v-model="form.goodsType" class="form-control form-control-sm" placeholder="نوع کالا" />
        <input v-model="form.billOfLadingNumber" class="form-control form-control-sm inv-ltr" placeholder="شماره بارنامه" />
        <input v-model.number="form.amount" class="form-control form-control-sm inv-ltr" placeholder="مبلغ کرایه (ریال)" type="number" />
        <input v-model="form.description" class="form-control form-control-sm" placeholder="توضیح" />
        <button class="btn btn-sm btn-primary" @click="save">ثبت کرایه</button>
      </div>
      <div class="inv-filters mb-2">
        <select v-model="statusFilter" class="form-select form-select-sm" @change="load">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in freightStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>رسید</th><th>تاریخ</th><th>بارنامه</th><th>مبلغ (ریال)</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-for="r in rows" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td class="inv-ltr">{{ r.receiptNumber }}</td><td>{{ jalali(r.freightDate) }}</td><td class="inv-ltr">{{ r.billOfLadingNumber }}</td>
              <td class="text-end inv-ltr">{{ r.amount == null ? "محرمانه" : rial(r.amount) }}</td><td>{{ freightStatuses[r.status] }}</td>
              <td><button class="btn btn-sm btn-outline-primary" @click="open(r.id)">جزئیات</button></td>
            </tr>
            <tr v-if="!rows.length && !loading"><td class="inv-muted text-center" colspan="7">کرایه‌ای ثبت نشده است.</td></tr>
          </tbody>
        </table>
      </div>
      <div v-if="detail" class="inv-card mt-3">
        <h2 class="h6">{{ detail.number }} - {{ freightStatuses[detail.status] }}</h2>
        <p class="small inv-muted">
          تهیه‌کننده: {{ detail.creatorName }}
          <span v-if="detail.approvedByName"> · تاییدکننده: {{ detail.approvedByName }} ({{ jalali(detail.approvedAt, true) }})</span>
          <span v-if="detail.voidReason"> · دلیل ابطال: {{ detail.voidReason }}</span>
        </p>
        <table class="table table-sm">
          <thead><tr><th>ردیف</th><th>کالا</th><th>مبنا</th><th>مقدار مبنا</th><th>سهم کرایه (ریال)</th></tr></thead>
          <tbody>
            <tr v-for="a in detail.allocations" :key="a.documentLineId">
              <td>{{ a.rowNumber }}</td><td>{{ a.itemCode }} - {{ a.itemTitle }}</td><td>{{ freightBases[a.basis] }}</td>
              <td class="text-end inv-ltr">{{ a.basisAmount == null ? "محرمانه" : rial(a.basisAmount) }}</td><td class="text-end inv-ltr">{{ a.amount == null ? "محرمانه" : rial(a.amount) }}</td>
            </tr>
          </tbody>
        </table>
        <div class="d-flex gap-2">
          <button v-if="detail.status === 0 && canApprove" class="btn btn-sm btn-success" @click="approve(detail.id)">تایید حسابدار</button>
          <button v-if="detail.status !== 2" class="btn btn-sm btn-outline-danger" @click="voidIt(detail.id)">ابطال</button>
        </div>
      </div>
    </div>
  </div>
</template>
