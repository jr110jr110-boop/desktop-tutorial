<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getStockCounts, getWarehouses, startStockCount } from "@/api/InventoryAPIs";
import { countKinds, countStatuses, fail, jalali, ok, today } from "@/composables/inventory";
import { hasRole, InventoryRoles } from "@/composables/roles";

const router = useRouter();
const rows = ref<any[]>([]);
const warehouses = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const canStart = hasRole(InventoryRoles.counts);

async function load() {
  const [data, error] = await getStockCounts();
  if (error) return fail(error);
  rows.value = data || [];
}

function open(id: number) {
  router.push({ name: "InventoryStockCountEdit", params: { id } });
}

function create() {
  form.value = { kind: 1, countDate: today(), warehouseId: null, locationId: null, groupCode: "", description: "" };
}

async function start() {
  busy.value = true;
  const [data, error] = await startStockCount(form.value);
  busy.value = false;
  if (error) return fail(error);
  ok(`شمارش ${data.number} با ${data.linesCount} ردیف شروع شد.`);
  open(data.id);
}

const locationsOf = (warehouseId: number | null) => warehouses.value.find((w) => w.id === warehouseId)?.locations || [];

onMounted(async () => {
  const [list] = await getWarehouses();
  warehouses.value = (list || []).filter((w: any) => w.isActive);
  load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="شمارش موجودی و انبارگردانی" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">شمارش موجودی و انبارگردانی</h1>
        <button v-if="canStart" class="btn btn-sm btn-primary d-print-none" @click="create">+ شمارش جدید</button>
      </div>
      <p class="inv-muted screen-only">
        با شروع شمارش، موجودی سیستم در همان لحظه ثبت می‌شود. شمارنده مقدار واقعی را وارد می‌کند و ردیف‌های دارای اختلاف دوباره شمرده می‌شوند.
        اختلاف پس از تایید سرپرست انبار و مدیر برنامه‌ریزی و کنترل پروژه با سند تعدیل ثبت می‌شود. در طول انبارگردانی سالانه، آن انبار هیچ سند جدیدی نمی‌گیرد.
      </p>
      <table class="table table-sm table-hover">
        <thead>
          <tr>
            <th>شماره</th>
            <th>نوع</th>
            <th>تاریخ</th>
            <th>انبار</th>
            <th>گروه</th>
            <th>ردیف‌ها</th>
            <th>اختلاف‌ها</th>
            <th>وضعیت</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="!rows.length">
            <td class="text-center inv-muted" colspan="8">شمارشی ثبت نشده است.</td>
          </tr>
          <tr v-for="r in rows" :key="r.id" role="button" @click="open(r.id)">
            <td class="inv-ltr">{{ r.number }}</td>
            <td>{{ countKinds[r.kind] }}</td>
            <td>{{ jalali(r.countDate) }}</td>
            <td>{{ r.warehouseTitle }}</td>
            <td class="inv-ltr">{{ r.groupCode }}</td>
            <td>{{ r.linesCount }}</td>
            <td>{{ r.differencesCount }}</td>
            <td>
              <span class="inv-badge" :class="r.status === 3 ? 'ok' : r.status === 4 ? 'bad' : 'warn'">{{ countStatuses[r.status] }}</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">شروع شمارش</h2>
        <div class="row g-2">
          <div class="col-6">
            <label class="form-label small mb-0">نوع</label>
            <select v-model="form.kind" class="form-select form-select-sm">
              <option v-for="(t, k) in countKinds" :key="k" :value="Number(k)">{{ t }}</option>
            </select>
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">تاریخ</label>
            <input v-model="form.countDate" class="form-control form-control-sm" type="date" />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">انبار *</label>
            <select v-model="form.warehouseId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="w in warehouses" :key="w.id" :value="w.id">{{ w.code }} - {{ w.title }}</option>
            </select>
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">فقط یک محل</label>
            <select v-model="form.locationId" class="form-select form-select-sm">
              <option :value="null">همه محل‌ها</option>
              <option v-for="l in locationsOf(form.warehouseId)" :key="l.id" :value="l.id">{{ l.code }}</option>
            </select>
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">فقط کدهای شروع‌شونده با</label>
            <input v-model="form.groupCode" class="form-control form-control-sm inv-ltr" placeholder="مثلاً 02 یا 50." />
          </div>
          <div class="col-6">
            <label class="form-label small mb-0">توضیح</label>
            <input v-model="form.description" class="form-control form-control-sm" />
          </div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="start">شروع</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
