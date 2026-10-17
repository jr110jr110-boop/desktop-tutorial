<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { exportAccountingEvents, getAccountingEvents, markAccountingEvents } from "@/api/FinanceAPIs";
import { fail, jalali, ok, printPage, rial } from "@/composables/inventory";
import { eventStatuses, eventTypes, roleTitle, sourceRoute } from "@/composables/finance";
import { FinanceRoles, hasRole } from "@/composables/roles";

// Accounting events (one future Sepidar voucher each): list, lines, voucher CSV and the mark as sent / exported.
const rows = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = 50;
const filters = ref({ Status: 0 as number | null, Type: null as number | null, From: "", To: "", Contains: "", IncludeNotRequired: false });
const selected = ref<number[]>([]);
const open = ref<number | null>(null);
const markExported = ref(true);
const reference = ref("");
const busy = ref(false);
const canWrite = hasRole(FinanceRoles.writers);
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)));

async function load() {
  const [data, error] = await getAccountingEvents({ ...filters.value, PageNumber: page.value, PageSize: pageSize });
  if (error) return fail(error);
  rows.value = data.data;
  total.value = data.totalItems;
  selected.value = [];
}

function search() {
  page.value = 1;
  load();
}

function toggle(id: number) {
  const i = selected.value.indexOf(id);
  if (i >= 0) selected.value.splice(i, 1);
  else selected.value.push(id);
}

const waiting = (r: any) => r.status === 0 || r.status === 2;
const chosen = computed(() => (selected.value.length ? selected.value : rows.value.filter(waiting).map((r) => r.id)));

async function exportFile() {
  if (!chosen.value.length) return fail("رویدادی برای خروجی نیست.");
  busy.value = true;
  const error = await exportAccountingEvents(chosen.value, markExported.value);
  busy.value = false;
  if (error) return fail(error);
  ok(`${chosen.value.length} سند در فایل CSV ذخیره شد.`);
  load();
}

async function mark(status: number) {
  if (!selected.value.length) return fail("ابتدا رویدادها را انتخاب کنید.");
  busy.value = true;
  const [, error] = await markAccountingEvents(selected.value, status, reference.value);
  busy.value = false;
  if (error) return fail(error);
  ok("وضعیت ثبت شد.");
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="رویدادهای حسابداری (سند سپیدار)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">رویدادهای حسابداری</h1>
        <div class="d-flex flex-wrap gap-2 align-items-center d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <template v-if="canWrite">
            <label class="form-check small mb-0">
              <input v-model="markExported" class="form-check-input" type="checkbox" />
              پس از خروجی، «خروجی گرفته شد» ثبت شود
            </label>
            <button :disabled="busy" class="btn btn-sm btn-success" @click="exportFile">
              خروجی CSV سند حسابداری {{ selected.length ? "(" + selected.length + ")" : "(در انتظار این صفحه)" }}
            </button>
          </template>
        </div>
      </div>
      <p class="inv-muted screen-only">
        هر رویداد یک سند حسابداری متوازن است. تا زمان اتصال مستقیم به سپیدار، فایل CSV گرفته و در سپیدار وارد می‌شود؛ هر رویداد کلید یکتا دارد و دوبار ارسال نمی‌شود.
      </p>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره منبع یا شرح" @keydown.enter="search" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in eventStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <select v-model="filters.Type" class="form-select form-select-sm" @change="search">
          <option :value="null">همه انواع</option>
          <option v-for="(t, k) in eventTypes" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="filters.From" class="form-control form-control-sm" title="از تاریخ" type="date" @change="search" />
        <input v-model="filters.To" class="form-control form-control-sm" title="تا تاریخ" type="date" @change="search" />
        <button class="btn btn-sm btn-secondary" @click="search">جستجو</button>
      </div>
      <div v-if="canWrite && selected.length" class="d-flex flex-wrap gap-2 align-items-center mb-2 d-print-none">
        <input v-model="reference" class="form-control form-control-sm" placeholder="شماره سند سپیدار (اختیاری)" style="max-width: 240px" />
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="mark(1)">ثبت: در سپیدار ثبت شد</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="mark(3)">ثبت: خروجی گرفته شد</button>
        <button :disabled="busy" class="btn btn-sm btn-outline-warning" @click="mark(0)">برگرداندن به صف</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr>
              <th v-if="canWrite" class="d-print-none"></th>
              <th>تاریخ</th>
              <th>نوع</th>
              <th>شماره منبع</th>
              <th>شرح</th>
              <th>مبلغ (ریال)</th>
              <th>وضعیت</th>
              <th>شماره سند سپیدار</th>
              <th class="d-print-none"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="!rows.length">
              <td colspan="9" class="text-center inv-muted">رویدادی نیست.</td>
            </tr>
            <template v-for="r in rows" :key="r.id">
              <tr :class="{ 'table-warning': r.status === 2 }">
                <td v-if="canWrite" class="d-print-none">
                  <input :checked="selected.includes(r.id)" class="form-check-input" type="checkbox" @change="toggle(r.id)" />
                </td>
                <td>{{ jalali(r.eventDate) }}</td>
                <td>{{ eventTypes[r.type] }}</td>
                <td class="inv-ltr">
                  <router-link v-if="sourceRoute(r.sourceType, r.sourceId)" :to="sourceRoute(r.sourceType, r.sourceId)!">{{ r.sourceNumber }}</router-link>
                  <span v-else>{{ r.sourceNumber }}</span>
                </td>
                <td>{{ r.description }}<div v-if="r.error" class="text-danger small">{{ r.error }}</div></td>
                <td class="num">{{ rial(r.amount) }}</td>
                <td>
                  <span class="inv-badge" :class="{ ok: r.status === 1 || r.status === 3, warn: r.status === 0, bad: r.status === 2 }">{{ eventStatuses[r.status] }}</span>
                </td>
                <td class="inv-ltr">{{ r.externalReference }}</td>
                <td class="d-print-none">
                  <button class="btn btn-sm btn-link p-0" @click="open = open === r.id ? null : r.id">{{ open === r.id ? "بستن" : "ردیف‌ها" }}</button>
                </td>
              </tr>
              <tr v-if="open === r.id">
                <td :colspan="canWrite ? 9 : 8">
                  <table class="table table-sm table-bordered mb-0">
                    <thead>
                      <tr><th>#</th><th>حساب</th><th>بدهکار</th><th>بستانکار</th><th>تامین‌کننده</th><th>مرکز هزینه</th><th>شرح</th></tr>
                    </thead>
                    <tbody>
                      <tr v-for="l in r.lines" :key="l.rowNumber">
                        <td>{{ l.rowNumber }}</td>
                        <td>{{ roleTitle(l.accountRole) }} <span class="inv-ltr inv-muted">{{ l.accountRole }}</span></td>
                        <td class="num">{{ l.debit ? rial(l.debit) : "" }}</td>
                        <td class="num">{{ l.credit ? rial(l.credit) : "" }}</td>
                        <td>{{ l.providerName }}</td>
                        <td class="inv-ltr">{{ l.costCenter }}</td>
                        <td>{{ l.description }}</td>
                      </tr>
                    </tbody>
                  </table>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
      <div class="d-flex gap-2 align-items-center d-print-none">
        <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="page--, load()">قبلی</button>
        <span class="small">صفحه {{ page }} از {{ pages }} — {{ total }} رویداد</span>
        <button :disabled="page >= pages" class="btn btn-sm btn-outline-secondary" @click="page++, load()">بعدی</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
