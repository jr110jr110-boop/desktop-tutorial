<script setup lang="ts">
// Link to Sepidar (finance): documents waiting to be posted, CSV export, and Sepidar codes of providers / work orders / contracts.
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { exportSepidar, getSepidarCheck, getSepidarMappings, getSepidarOutbox, saveSepidarMapping } from "@/api/InventoryAPIs";
import { documentStatuses, documentTypes, fail, jalali, num, ok, sepidarStatuses } from "@/composables/inventory";

const tab = ref<"outbox" | "codes">("outbox");
const check = ref<any>(null);
const status = ref<number | null>(1);
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 100;
const selected = ref<number[]>([]);
const markExported = ref(true);
const busy = ref(false);
const entityType = ref("Provider");
const mappings = ref<any[]>([]);
const mapFilter = ref("");

async function loadCheck() {
  const [data, error] = await getSepidarCheck();
  if (error) return fail(error);
  check.value = data;
}

async function loadOutbox() {
  const [data, error] = await getSepidarOutbox({ Status: status.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
  selected.value = [];
}

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));
const allSelected = computed(() => rows.value.length > 0 && selected.value.length === rows.value.length);

function toggleAll() {
  selected.value = allSelected.value ? [] : rows.value.map((r) => r.id);
}

async function doExport() {
  const ids = selected.value.length ? selected.value : rows.value.map((r) => r.id);
  if (!ids.length) return fail("سندی برای خروجی نیست.");
  busy.value = true;
  const error = await exportSepidar(ids, markExported.value);
  busy.value = false;
  if (error) return fail(error);
  ok(`${ids.length} سند در فایل CSV ذخیره شد.`);
  loadOutbox();
  loadCheck();
}

function printReport() {
  window.print();
}

async function loadMappings() {
  const [data, error] = await getSepidarMappings(entityType.value);
  if (error) return fail(error);
  mappings.value = (data || []).map((m: any) => ({ ...m, edit: m.sepidarCode || "" }));
}

const filteredMappings = computed(() => {
  const f = mapFilter.value.trim();
  return f ? mappings.value.filter((m) => `${m.code} ${m.title}`.includes(f)) : mappings.value;
});

async function saveMap(m: any) {
  const [, error] = await saveSepidarMapping({ entityType: entityType.value, entityId: m.entityId, sepidarCode: m.edit });
  if (error) return fail(error);
  m.sepidarCode = m.edit;
  ok("ذخیره شد.");
  loadCheck();
}

onMounted(() => {
  loadCheck();
  loadOutbox();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="اسناد انبار برای سپیدار" :subtitle="status !== null ? sepidarStatuses[status] : 'همه'" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">ارتباط با سپیدار (مالی)</h1>
        <div class="d-flex gap-2 d-print-none"><button class="btn btn-sm btn-outline-secondary" @click="printReport">چاپ</button><div class="btn-group btn-group-sm">
          <button :class="tab === 'outbox' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="tab = 'outbox'">اسناد برای سپیدار</button>
          <button :class="tab === 'codes' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="tab = 'codes'; loadMappings()">کدهای سپیدار</button>
        </div></div>
      </div>
      <div v-if="check" class="alert small py-2" :class="check.missingCodes.length ? 'alert-warning' : 'alert-success'">
        {{ check.pendingDocuments }} سند تاییدشده منتظر ارسال به سپیدار است.
        <template v-if="check.missingCodes.length">
          پیش از ثبت در سپیدار این کدها را کامل کنید:
          <ul class="mb-0 mt-1">
            <li v-for="(m, i) in check.missingCodes.slice(0, 30)" :key="i">{{ m }}</li>
          </ul>
          <span v-if="check.missingCodes.length > 30">و {{ check.missingCodes.length - 30 }} مورد دیگر</span>
        </template>
        <template v-else>همه کدهای لازم وجود دارد.</template>
      </div>
      <p class="inv-muted mb-0">
        فعلاً خروجی به شکل فایل CSV است که در سپیدار وارد می‌شود. پس از دریافت مستندات API سپیدار، ارسال مستقیم به همین صفحه اضافه می‌شود.
        سند باطل‌شده‌ای که قبلاً ارسال شده، با مقدار منفی (برگشت) دوباره در صف می‌آید.
      </p>
    </div>

    <div v-if="tab === 'outbox'" class="inv-card">
      <div class="inv-filters mb-3">
        <select v-model="status" class="form-select form-select-sm" @change="page = 1; loadOutbox()">
          <option :value="null">همه</option>
          <option v-for="(t, k) in sepidarStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <div class="form-check"><input id="mk" v-model="markExported" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="mk">پس از خروجی، «خروجی گرفته شد» علامت بخورد</label></div>
        <button :disabled="busy" class="btn btn-sm btn-success" @click="doExport">خروجی CSV {{ selected.length ? "(" + selected.length + " سند)" : "(همه این صفحه)" }}</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th><input :checked="allSelected" class="form-check-input" type="checkbox" @change="toggleAll" /></th>
              <th>شماره</th><th>نوع</th><th>تاریخ</th><th>انبار</th><th>طرف حساب</th><th>مقدار کل</th><th>وضعیت سند</th><th>سپیدار</th><th>زمان خروجی</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="10">سندی نیست.</td></tr>
            <tr v-for="d in rows" :key="d.id">
              <td><input v-model="selected" :value="d.id" class="form-check-input" type="checkbox" /></td>
              <td><router-link :to="{ name: 'InventoryDocumentEdit', params: { id: d.id } }" class="inv-ltr">{{ d.number }}</router-link></td>
              <td>{{ documentTypes[d.type] }}</td>
              <td>{{ jalali(d.documentDate) }}</td>
              <td>{{ d.warehouseTitle }}</td>
              <td>{{ d.providerName || d.workOrderCode || d.contractCode }}</td>
              <td>{{ num(d.totalQuantity) }}</td>
              <td>{{ documentStatuses[d.status] }}</td>
              <td>{{ sepidarStatuses[d.sepidarStatus] }}</td>
              <td>{{ jalali(d.sepidarExportedAt, true) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="inv-pager">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--; loadOutbox()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} ({{ total }} سند)</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++; loadOutbox()">بعدی</button>
      </div>
    </div>

    <div v-else class="inv-card">
      <p class="inv-muted">کد کالا در صفحه «کالاها» و کد انبار در صفحه «انبارها» ثبت می‌شود. اینجا کد تفصیلی تامین‌کنندگان، دستورهای ساخت (مرکز هزینه) و قراردادها در سپیدار ثبت می‌شود.</p>
      <div class="inv-filters mb-3">
        <select v-model="entityType" class="form-select form-select-sm" @change="loadMappings">
          <option value="Provider">تامین‌کنندگان</option>
          <option value="WorkOrder">دستورهای ساخت</option>
          <option value="Contract">قراردادها</option>
        </select>
        <input v-model="mapFilter" class="form-control form-control-sm" placeholder="جستجو" />
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>عنوان</th><th style="width: 14rem">کد در سپیدار</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!filteredMappings.length"><td class="text-center inv-muted" colspan="4">موردی نیست.</td></tr>
          <tr v-for="m in filteredMappings" :key="m.entityId">
            <td class="inv-ltr">{{ m.code }}</td>
            <td>{{ m.title }}</td>
            <td><input v-model="m.edit" class="form-control form-control-sm inv-ltr" @keydown.enter="saveMap(m)" /></td>
            <td><button :disabled="m.edit === (m.sepidarCode || '')" class="btn btn-sm btn-outline-primary py-0" @click="saveMap(m)">ذخیره</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
