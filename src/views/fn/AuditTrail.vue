<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import AuditPrint from "@/components/fn/print/AuditPrint.vue";
import QPager from "@/components/quality/QPager.vue";
import { getAuditTrail } from "@/api/FnAPIs";
import { fail, jalali } from "@/composables/inventory";
import { printSheet, usePagedList } from "@/composables/fn";
import { jalaliInText } from "@/composables/fnF3";

// Audit trail (ردپای حسابرسی): the append-only log of the finance module. Read only for the auditor, the finance manager and the CEO.
const filters = ref<any>({ EntityType: "", EntityId: "", UserId: "", Action: "", From: null as string | null, To: null as string | null });
const list = usePagedList(getAuditTrail, filters);
const detail = ref<any>(null);
const printRows = ref<any[]>([]);
const printing = ref(false);

const printFields = computed(() => {
  const f = filters.value;
  const out: { label: string; value: any }[] = [{ label: "از تاریخ", value: f.From ? jalali(f.From) : "ابتدا" }, { label: "تا تاریخ", value: f.To ? jalali(f.To) : "اکنون" }];
  if (f.EntityType) out.push({ label: "نوع رکورد", value: f.EntityType });
  if (f.Action) out.push({ label: "عملیات", value: f.Action });
  return out;
});

/** The JSON text of a before / after image, indented when it can be parsed. */
function pretty(text: string | null | undefined): string {
  if (!text) return "-";
  try {
    return jalaliInText(JSON.stringify(JSON.parse(text), null, 2));
  } catch {
    return jalaliInText(text);
  }
}

async function print() {
  const [data, error] = await getAuditTrail({ ...filters.value, PageNumber: 1, PageSize: 500 });
  if (error) return fail(error);
  printRows.value = Array.isArray(data) ? data : data?.data || [];
  printing.value = true;
  await nextTick();
  printSheet();
}

onMounted(() => list.load());
</script>

<template>
  <div class="inv-page">
    <AuditPrint v-if="printing" :fields="printFields" :rows="printRows" />

    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">ردپای حسابرسی</h1>
        <button class="btn btn-sm btn-outline-secondary" @click="print">چاپ گزارش حسابرسی</button>
      </div>
      <p class="inv-muted">فهرست غیرقابل ویرایش رویدادهای سامانه مالی: چه کسی، چه زمانی و چه چیزی را تغییر داده است. حداکثر ۵۰۰ رکورد در چاپ می‌آید.</p>
      <div class="inv-filters mb-3">
        <input v-model="filters.EntityType" class="form-control form-control-sm" placeholder="نوع رکورد (مثلا Voucher)" @keydown.enter="list.search" />
        <input v-model="filters.EntityId" class="form-control form-control-sm" placeholder="شناسه رکورد" type="number" @keydown.enter="list.search" />
        <input v-model="filters.UserId" class="form-control form-control-sm" placeholder="شناسه کاربر" type="number" @keydown.enter="list.search" />
        <input v-model="filters.Action" class="form-control form-control-sm" placeholder="عملیات" @keydown.enter="list.search" />
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.From" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.To" /></div>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>تاریخ و ساعت</th><th>کاربر</th><th>نوع رکورد</th><th>شناسه</th><th>عملیات</th><th>خلاصه</th><th>نشانی</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">رکوردی نیست.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id" style="cursor: pointer" @click="detail = r">
              <td class="inv-ltr">{{ jalali(r.at, true) }}</td><td>{{ r.userName }}</td><td class="inv-ltr">{{ r.entityType }}</td><td>{{ r.entityId }}</td><td>{{ r.action }}</td><td>{{ jalaliInText(r.summary) }}</td><td class="inv-ltr">{{ r.ip }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">جزئیات رکورد ردپا</h2>
        <div class="row g-2 mb-2">
          <div class="col-md-4"><b>زمان:</b> <span class="inv-ltr">{{ jalali(detail.at, true) }}</span></div>
          <div class="col-md-4"><b>کاربر:</b> {{ detail.userName }}</div>
          <div class="col-md-4"><b>نشانی:</b> <span class="inv-ltr">{{ detail.ip }}</span></div>
          <div class="col-md-4"><b>نوع رکورد:</b> <span class="inv-ltr">{{ detail.entityType }}</span></div>
          <div class="col-md-4"><b>شناسه:</b> {{ detail.entityId }}</div>
          <div class="col-md-4"><b>عملیات:</b> {{ detail.action }}</div>
          <div class="col-12"><b>خلاصه:</b> {{ jalaliInText(detail.summary) }}</div>
        </div>
        <div class="row g-2">
          <div class="col-md-6"><div class="fn-section">پیش از تغییر</div><pre class="inv-ltr small border rounded p-2 bg-light" style="max-height: 280px; overflow: auto">{{ pretty(detail.beforeJson) }}</pre></div>
          <div class="col-md-6"><div class="fn-section">پس از تغییر</div><pre class="inv-ltr small border rounded p-2 bg-light" style="max-height: 280px; overflow: auto">{{ pretty(detail.afterJson) }}</pre></div>
        </div>
        <div class="d-flex justify-content-end mt-2"><button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
