<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import AuditPrint from "@/components/fn/print/AuditPrint.vue";
import QPager from "@/components/quality/QPager.vue";
import { getAuditTrail, verifyAuditChain } from "@/api/FnAPIs";
import { fail, jalali } from "@/composables/inventory";
import { printSheet, usePagedList } from "@/composables/fn";
import { jalaliInText } from "@/composables/fnF3";
import { hasRole } from "@/composables/roles";

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

// R1t D20: the check of the hash chain (AUDITOR and the finance manager)
const chain = ref<any>({ from: "", to: "", anchorSeq: "", anchorHash: "" });
const chainResult = ref<any>(null);
const chainBusy = ref(false);
const canVerify = hasRole(["AUDITOR", "FINANCEMANAGER"]);
async function verifyChain() {
  chainBusy.value = true;
  const c = chain.value;
  const [d, error] = await verifyAuditChain({ from: c.from || null, to: c.to || null, anchorSeq: c.anchorSeq || null, anchorHash: String(c.anchorHash || "").trim() || null });
  chainBusy.value = false;
  if (error) return fail(error);
  chainResult.value = d;
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
      <details v-if="canVerify" class="border rounded p-2 mb-3">
        <summary class="fw-bold small">بررسی درستی زنجیره‌ی مهر ردپا (hash chain)</summary>
        <p class="inv-muted small mt-2">هر ردیف ردپا با مهر ردیف پیشین پیوند دارد؛ تغییر، حذف یا اضافه‌کردن ردیف را می‌توان پیدا کرد. «سر زنجیره» (شماره و مهر) را یادداشت یا چاپ کنید و بعدها به‌عنوان لنگر بدهید تا ثابت شود چیزی از ابتدا تا آن ردیف بریده یا بازنویسی نشده است. هر بررسی حداکثر ۵۰٬۰۰۰ ردیف را می‌پیماید.</p>
        <div class="inv-filters mb-2">
          <input v-model="chain.from" class="form-control form-control-sm fn-num" placeholder="از جایگاه" style="max-width: 9rem" />
          <input v-model="chain.to" class="form-control form-control-sm fn-num" placeholder="تا جایگاه" style="max-width: 9rem" />
          <input v-model="chain.anchorSeq" class="form-control form-control-sm fn-num" placeholder="شماره‌ی لنگر" style="max-width: 9rem" />
          <input v-model="chain.anchorHash" class="form-control form-control-sm inv-ltr" placeholder="مهر لنگر" style="min-width: 16rem" />
          <button :disabled="chainBusy" class="btn btn-sm btn-secondary" @click="verifyChain">بررسی زنجیره</button>
        </div>
        <div v-if="chainResult">
          <p><span :class="chainResult.ok ? 'ok' : 'bad'" class="inv-badge">{{ chainResult.ok ? "زنجیره سالم است" : "زنجیره شکسته است" }}</span> {{ chainResult.message }}</p>
          <p class="small">ردیف بررسی‌شده: {{ chainResult.checked }} — مهرخورده: {{ chainResult.sealedRows }} — ردیف‌های قدیمی بدون مهر: {{ chainResult.legacyRows }} <span v-if="!chainResult.keyConfigured" class="inv-badge warn">کلید مهر تنظیم نشده؛ مهر ساده SHA-256 است</span></p>
          <p v-if="chainResult.headSeq" class="small">سر زنجیره: جایگاه <b class="fn-num">{{ chainResult.headSeq }}</b> — <span class="inv-ltr">{{ chainResult.headHash }}</span></p>
          <p v-if="chainResult.firstBreak" class="alert alert-danger py-2 small">نخستین شکست: {{ chainResult.firstBreak.message }} ({{ chainResult.firstBreak.kind }}، جایگاه {{ chainResult.firstBreak.seq ?? "-" }}، ردیف {{ chainResult.firstBreak.id }}، {{ chainResult.firstBreak.entityType }} {{ chainResult.firstBreak.action }})</p>
        </div>
      </details>
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
