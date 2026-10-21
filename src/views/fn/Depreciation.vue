<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { depreciationAction, getDepreciationRun, getDepreciationRuns, getFiscalYears, prepareDepreciation } from "@/api/FnAPIs";
import { money, runStatuses, usePagedList } from "@/composables/fn";
import { fail, jalali, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Monthly depreciation: the accountant prepares a run for an open Persian month, the finance manager (another person) approves it and the voucher is issued.
const filters = ref({ Year: null as number | null, Status: null as number | null });
const list = usePagedList(getDepreciationRuns, filters);
const years = ref<any[]>([]);
const periodId = ref<number | null>(null);
const detail = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);
const canApprove = hasRole(FnRoles.managers);
const openPeriods = computed(() => years.value.flatMap((y) => (y.periods || []).filter((p: any) => p.status === 0).map((p: any) => ({ id: p.id, title: `${p.title} ${y.title || ""}` }))));

async function prepare() {
  if (!periodId.value) return fail("دوره مالی را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await prepareDepreciation(periodId.value);
  busy.value = false;
  if (error) return fail(error);
  ok("استهلاک دوره آماده شد؛ پیش از تایید بررسی کنید.");
  list.search();
  if (d) detail.value = d;
}

async function open(id: number) {
  const [d, error] = await getDepreciationRun(id);
  if (error) return fail(error);
  detail.value = d;
}

async function act(run: any, action: string) {
  if (action === "delete" && !confirm("پیش‌نویس استهلاک حذف شود؟")) return;
  busy.value = true;
  const [, error] = await depreciationAction(run.id, action);
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "استهلاک تایید و سند حسابداری ثبت شد." : "حذف شد.");
  detail.value = null;
  list.load();
}

onMounted(async () => {
  const [y] = await getFiscalYears();
  years.value = y || [];
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">استهلاک ماهانه دارایی‌ها</h1></div>
      <div v-if="canWrite" class="inv-filters mb-3">
        <select v-model="periodId" class="form-select form-select-sm"><option :value="null">دوره مالی باز...</option><option v-for="p in openPeriods" :key="p.id" :value="p.id">{{ p.title }}</option></select>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="prepare">آماده‌سازی استهلاک</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>دوره</th><th>از</th><th>تا</th><th>تعداد دارایی</th><th>مبلغ (ریال)</th><th>تهیه‌کننده</th><th>تاییدکننده</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">اجرایی نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ r.periodTitle }}</td><td>{{ jalali(r.periodStart) }}</td><td>{{ jalali(r.periodEnd) }}</td><td>{{ r.assetCount }}</td><td class="fn-num">{{ money(r.totalAmount) }}</td>
              <td>{{ r.preparedByName }}</td><td>{{ r.approvedByName }}</td><td><span :class="r.status === 1 ? 'ok' : 'warn'" class="inv-badge">{{ runStatuses[r.status] }}</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="open(r.id)">ردیف‌ها</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">استهلاک {{ detail.periodTitle }} ({{ detail.number }})</h2>
        <div class="table-responsive" style="max-height: 55vh; overflow-y: auto">
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>دارایی</th><th>رده</th><th>ماه</th><th>ماه بلااستفاده</th><th>ارزش دفتری قبل</th><th>استهلاک ماه</th><th>انباشته پس از</th></tr></thead>
            <tbody>
              <tr v-for="l in detail.lines" :key="l.assetId"><td class="inv-ltr">{{ l.assetCode }}</td><td>{{ l.assetTitle }}</td><td>{{ l.classCode }}</td><td>{{ l.months }}</td><td>{{ l.idleMonths }}</td><td class="fn-num">{{ money(l.bookValueBefore) }}</td><td class="fn-num">{{ money(l.amount) }}</td><td class="fn-num">{{ money(l.accumulatedAfter) }}</td></tr>
            </tbody>
            <tfoot><tr class="fn-total"><td colspan="6">جمع</td><td class="fn-num">{{ money(detail.totalAmount) }}</td><td></td></tr></tfoot>
          </table>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <template v-if="detail.status === 0">
            <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act(detail, 'delete')">حذف پیش‌نویس</button>
            <button v-if="canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="act(detail, 'approve')">تایید و ثبت سند</button>
          </template>
          <button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button>
        </div>
        <p v-if="detail.status === 0" class="inv-muted small mt-2 mb-0">تایید فقط توسط مدیر مالی و غیر از تهیه‌کننده انجام می‌شود.</p>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
