<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import PromptModal from "@/components/fn/PromptModal.vue";
import QPager from "@/components/quality/QPager.vue";
import { bulkApproveSpItems, decideSpItem, fixSpItem, getSpItem, getSpItems, getSpSummary, getSpTypes, switchSpType } from "@/api/FnAPIs";
import { spAttemptOutcomes, spStatusBadge, spStatuses, usePagedList } from "@/composables/fn";
import { faDigits } from "@/composables/ceo";
import { fail, jalali, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Sepidar transfer per document. All finance work is done inside Orchestra; only the kinds that the owner switches on are queued here as items, a person approves each item
// (never the maker of the source document), and the sender hands it over. Until a sender is configured the approved items wait in the queue.
// Every button is enabled exactly when the server would accept the action, and a disabled button shows the server's own reason.
const route = useRoute();
const router = useRouter();

const tab = ref<"queue" | "types">("queue");
const filters = ref({ Status: null as number | null, TypeCode: "" as string, Search: "" as string });
const list = usePagedList(getSpItems, filters, 25);
const summary = ref<any>(null);
const types = ref<any[]>([]);
const detail = ref<any>(null);
const selected = ref<number[]>([]);
const bulkResult = ref<any>(null);
const dialog = ref<null | { kind: "reject" | "cancel" | "enable"; item?: any; type?: any }>(null);
const busy = ref(false);

const isDecider = hasRole(FnRoles.sepidarTransferDeciders);
const isSwitcher = hasRole(FnRoles.sepidarTransferSwitchers);

const senderNone = computed(() => summary.value?.senderName === "None");
const senderNotReady = computed(() => !!summary.value && !summary.value.isConfigured && summary.value.senderName !== "None");
const statusCount = (status: number): number => summary.value?.counts?.find((c: any) => c.status === status)?.count ?? 0;
const num = (n: number | null | undefined) => faDigits(n ?? 0);
const when = (iso: string | null | undefined) => (iso ? faDigits(jalali(iso, true)) : "—");
const selectable = (item: any) => isDecider && item.canDecide;

async function loadSummary() {
  const [d, error] = await getSpSummary();
  if (error) return fail(error);
  summary.value = d;
}
async function loadTypes() {
  const [d, error] = await getSpTypes();
  if (error) return fail(error);
  types.value = Array.isArray(d) ? d : [];
}
async function refresh() {
  selected.value = [];
  await Promise.all([list.load(), loadSummary(), loadTypes()]);
}

async function openItem(id: number) {
  const [d, error] = await getSpItem(id);
  if (error) {
    detail.value = null;
    return fail(error);
  }
  detail.value = d;
}
async function reloadDetail() {
  if (detail.value) await openItem(detail.value.id);
}

function toggle(id: number) {
  selected.value = selected.value.includes(id) ? selected.value.filter((x) => x !== id) : [...selected.value, id];
}
const allSelectable = computed(() => list.rows.value.filter(selectable).map((r: any) => r.id));
const allChecked = computed(() => allSelectable.value.length > 0 && allSelectable.value.every((id: number) => selected.value.includes(id)));
function toggleAll() {
  selected.value = allChecked.value ? [] : [...allSelectable.value];
}

async function approve(item: any) {
  busy.value = true;
  const [, error] = await decideSpItem(item.id, "approve", item.rowVersion);
  busy.value = false;
  if (error) fail(error);
  else ok("قلم تایید شد؛ تا تنظیم اتصال، در صف ارسال می‌ماند.");
  await refresh();
  await reloadDetail();
}
async function retry(item: any) {
  busy.value = true;
  const [, error] = await fixSpItem(item.id, "retry", item.rowVersion);
  busy.value = false;
  if (error) fail(error);
  else ok("قلم دوباره در صف ارسال قرار گرفت.");
  await refresh();
  await reloadDetail();
}
async function runDialog(v: any) {
  const d = dialog.value;
  dialog.value = null;
  if (!d) return;
  busy.value = true;
  if (d.kind === "enable") {
    const [, error] = await switchSpType(d.type.code, true, d.type.rowVersion, null, v.fromDate);
    busy.value = false;
    if (error) fail(error);
    else ok("انتقال این نوع سند روشن شد؛ ایجنت مالی اقلام را برای تایید آماده می‌کند.");
    return refresh();
  }
  const [, error] = d.kind === "reject" ? await decideSpItem(d.item.id, "reject", d.item.rowVersion, v.reason) : await fixSpItem(d.item.id, "cancel", d.item.rowVersion, v.reason);
  busy.value = false;
  if (error) fail(error);
  else ok(d.kind === "reject" ? "قلم رد شد." : "قلم لغو شد.");
  await refresh();
  await reloadDetail();
}
async function bulkApprove() {
  const rows = list.rows.value.filter((r: any) => selected.value.includes(r.id)).map((r: any) => ({ id: r.id, rowVersion: r.rowVersion }));
  if (!rows.length) return;
  busy.value = true;
  const [d, error] = await bulkApproveSpItems(rows);
  busy.value = false;
  if (error) return fail(error);
  bulkResult.value = d;
  (d.refused ? fail : ok)(`از ${num(d.requested)} قلم، ${num(d.approved)} قلم تایید شد و ${num(d.refused)} قلم رد شد.`);
  await refresh();
}
async function switchOff(t: any) {
  busy.value = true;
  const [, error] = await switchSpType(t.code, false, t.rowVersion);
  busy.value = false;
  if (error) fail(error);
  else ok("انتقال این نوع سند خاموش شد؛ چیزی تازه به صف نمی‌آید.");
  await refresh();
}

const todayIso = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
const dialogFields = computed(() => {
  const d = dialog.value;
  if (!d) return [];
  // switching a kind on needs the first day: only documents finalised from that day are queued (the default is today)
  if (d.kind === "enable") return [{ key: "fromDate", label: "تاریخ شروع انتقال (اسناد نهایی‌شده از این روز به بعد)", type: "date" as const, required: true, value: d.type?.fromDate ? String(d.type.fromDate).slice(0, 10) : todayIso() }];
  return [{ key: "reason", label: d.kind === "reject" ? "علت رد" : "علت لغو", type: "textarea" as const, required: true, maxlength: 1000 }];
});
const dialogTitle = computed(() => {
  const d = dialog.value;
  if (!d) return "";
  if (d.kind === "enable") return "روشن کردن انتقال: " + d.type.title;
  return (d.kind === "reject" ? "رد قلم " : "لغو قلم ") + d.item.sourceNumber;
});

onMounted(async () => {
  await refresh();
  const id = Number(route.query.item);
  if (Number.isFinite(id) && id > 0) await openItem(id);
});
watch(
  () => route.query.item,
  (v) => {
    const id = Number(v);
    if (Number.isFinite(id) && id > 0) openItem(id);
  }
);
function closeDetail() {
  detail.value = null;
  if (route.query.item) router.replace({ query: { ...route.query, item: undefined } });
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">انتقال به سپیدار</h1>
        <button class="btn btn-sm btn-outline-secondary" :disabled="busy" @click="refresh">تازه‌سازی</button>
      </div>

      <div v-if="senderNone" class="alert alert-warning py-2 small" role="alert">اتصال به سپیدار هنوز تنظیم نشده است؛ اقلام تأییدشده در صف می‌مانند.</div>
      <div v-else-if="senderNotReady" class="alert alert-warning py-2 small" role="alert">فرستندهٔ «{{ summary.senderName }}» هنوز آماده نیست؛ اقلام تأییدشده در صف می‌مانند.</div>

      <div v-if="summary" class="fn-tiles mb-3">
        <div class="fn-tile"><div class="t">منتظر تایید</div><div class="v">{{ num(statusCount(2)) }}</div></div>
        <div class="fn-tile"><div class="t">تاییدشده، آماده ارسال</div><div class="v">{{ num(statusCount(3)) }}</div></div>
        <div :class="statusCount(6) ? 'bad' : 'ok'" class="fn-tile"><div class="t">ناموفق</div><div class="v">{{ num(statusCount(6)) }}</div></div>
        <div class="fn-tile"><div class="t">ارسال‌شده</div><div class="v">{{ num(statusCount(5)) }}</div></div>
        <div class="fn-tile"><div class="t">آخرین ارسال موفق</div><div class="v small">{{ when(summary.lastSuccessfulSendAt) }}</div></div>
        <div class="fn-tile"><div class="t">فرستنده</div><div class="v small inv-ltr">{{ summary.senderName }}</div></div>
      </div>

      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'queue' }" href="#" @click.prevent="tab = 'queue'">صف انتقال</a></li>
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'types' }" href="#" @click.prevent="tab = 'types'">انواع قابل انتقال</a></li>
      </ul>

      <div v-if="tab === 'queue'">
        <div class="inv-filters mb-3">
          <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in spStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <select v-model="filters.TypeCode" class="form-select form-select-sm"><option value="">همه انواع</option><option v-for="t in types" :key="t.code" :value="t.code">{{ t.title }}</option></select>
          <input v-model="filters.Search" class="form-control form-control-sm" placeholder="شمارهٔ سند یا عنوان" style="max-width: 14rem" @keyup.enter="list.search" />
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
          <button v-if="isDecider" :disabled="busy || !selected.length" class="btn btn-sm btn-success" @click="bulkApprove">تایید گروهی ({{ num(selected.length) }})</button>
        </div>

        <div v-if="bulkResult" class="alert alert-info py-2 small">
          از {{ num(bulkResult.requested) }} قلم، {{ num(bulkResult.approved) }} قلم تایید شد و {{ num(bulkResult.refused) }} قلم رد شد.
          <ul v-if="bulkResult.refused" class="mb-0"><li v-for="r in bulkResult.rows.filter((x: any) => !x.approved)" :key="r.id">قلم {{ num(r.id) }}: {{ r.message }}</li></ul>
          <button class="btn btn-sm btn-link p-0" @click="bulkResult = null">بستن</button>
        </div>

        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead>
              <tr>
                <th style="width: 2rem"><input v-if="isDecider" type="checkbox" :checked="allChecked" :disabled="!allSelectable.length" @change="toggleAll" /></th>
                <th>سند</th><th>نوع</th><th>وضعیت</th><th>آماده‌سازی</th><th>تلاش</th><th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">قلمی در صف نیست.</td></tr>
              <tr v-for="i in list.rows.value" :key="i.id" :class="{ 'table-active': detail && detail.id === i.id }">
                <td><input v-if="selectable(i)" type="checkbox" :checked="selected.includes(i.id)" @change="toggle(i.id)" /></td>
                <td>{{ i.title }}<div class="inv-muted small inv-ltr">{{ i.sourceNumber }}</div></td>
                <td>{{ i.transferTypeTitle }}<span v-if="i.isCorrection" class="inv-badge warn ms-1">اصلاحیه</span></td>
                <td><span :class="spStatusBadge(i.status)" class="inv-badge">{{ i.statusTitle }}</span><div v-if="i.lastError" class="small text-danger">{{ i.lastError }}</div></td>
                <td class="small">{{ when(i.preparedAt) }}</td>
                <td>{{ num(i.attemptCount) }}</td>
                <td class="text-nowrap">
                  <button class="btn btn-sm btn-link p-0 me-2" @click="openItem(i.id)">جزئیات</button>
                  <template v-if="isDecider && i.status === 2">
                    <button :disabled="busy || !i.canDecide" :title="i.canDecide ? '' : i.cannotDecideReason" class="btn btn-sm btn-success me-1" @click="approve(i)">تایید</button>
                    <button :disabled="busy || !i.canDecide" :title="i.canDecide ? '' : i.cannotDecideReason" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'reject', item: i }">رد</button>
                  </template>
                  <template v-if="isDecider && (i.status === 6 || i.status === 3)">
                    <button v-if="i.status === 6" :disabled="busy || !i.canFix" :title="i.canFix ? '' : i.cannotFixReason" class="btn btn-sm btn-outline-primary me-1" @click="retry(i)">تلاش دوباره</button>
                    <button :disabled="busy || !i.canFix" :title="i.canFix ? '' : i.cannotFixReason" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'cancel', item: i }">لغو</button>
                  </template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" :page-size="25" @go="list.go" />
        <p class="inv-muted small mb-0">تاییدکننده نباید ایجادکننده یا ویرایشگر سند منبع باشد و ایجنت هوش مصنوعی هرگز تایید نمی‌کند. سندی که پس از ارسال تغییر کند، با قلم «اصلاحیه» دوباره برای تایید می‌آید.</p>
      </div>

      <div v-else>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>نوع سند</th><th>توضیح</th><th>تایید انسان</th><th>وضعیت</th><th>شروع انتقال</th><th>آخرین تغییر</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!types.length"><td colspan="7" class="text-center inv-muted">نوعی تعریف نشده است.</td></tr>
              <tr v-for="t in types" :key="t.code">
                <td>{{ t.title }}<div class="inv-muted small inv-ltr">{{ t.code }}</div></td>
                <td class="small">{{ t.note }}<div v-if="t.overlapNote" class="small text-warning">هم‌پوشانی با: {{ t.overlapNote }}؛ تا پایان بازهٔ بستهٔ ماهانه روشن نمی‌شود.</div></td>
                <td>{{ t.requiresApproval ? "همیشه" : "—" }}</td>
                <td><span :class="t.isEnabled ? 'ok' : ''" class="inv-badge">{{ t.isEnabled ? "روشن" : "خاموش" }}</span></td>
                <td class="small">{{ t.fromDate ? faDigits(jalali(t.fromDate)) : "—" }}</td>
                <td class="small">{{ when(t.changedAt) }}<div v-if="t.changedByName" class="inv-muted">{{ t.changedByName }}</div></td>
                <td class="text-nowrap">
                  <template v-if="isSwitcher">
                    <button v-if="!t.isEnabled" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="dialog = { kind: 'enable', type: t }">روشن کردن</button>
                    <button v-else :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="switchOff(t)">خاموش کردن</button>
                  </template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="inv-muted small mb-0">فهرست نهایی اطلاعاتی که به سپیدار می‌رود را مالک شرکت تعیین می‌کند؛ تا آن زمان همهٔ انواع خاموش‌اند و برای نوع خاموش هیچ قلمی ساخته نمی‌شود. تغییر هر نوع در سابقهٔ حسابرسی ثبت می‌شود.</p>
      </div>
    </div>

    <PromptModal
      v-if="dialog"
      :fields="dialogFields"
      :title="dialogTitle"
      :hint="dialog.kind === 'enable' ? 'با روشن شدن، ایجنت مالی برای اسناد نهایی این نوع قلم انتقال می‌سازد و هر قلم منتظر تایید مدیر مالی می‌ماند. ادامه می‌دهید؟' : undefined"
      :confirm-text="dialog.kind === 'enable' ? 'روشن شود' : undefined"
      @cancel="dialog = null"
      @confirm="runDialog"
    />

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="closeDetail">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ detail.title }}</h2>
        <p class="small mb-1">
          <span :class="spStatusBadge(detail.status)" class="inv-badge me-1">{{ detail.statusTitle }}</span>
          <span v-if="detail.isCorrection" class="inv-badge warn me-1">اصلاحیه</span>
          {{ detail.transferTypeTitle }} | سند <span class="inv-ltr">{{ detail.sourceNumber }}</span> | آماده‌سازی: {{ when(detail.preparedAt) }}
        </p>
        <p v-if="detail.isCorrection" class="small text-warning mb-1">این سند پیش‌تر به سپیدار ارسال شده و پس از آن تغییر کرده است؛ این قلم اصلاحیهٔ قلم #{{ num(detail.correctsItemId) }} است.</p>
        <p v-if="detail.approvedByName" class="small mb-1">تاییدکننده: {{ detail.approvedByName }} ({{ when(detail.approvedAt) }})</p>
        <p v-if="detail.rejectedByName" class="small mb-1">ردکننده: {{ detail.rejectedByName }} - {{ detail.rejectReason }}</p>
        <p v-if="detail.cancelledByName" class="small mb-1">لغوکننده: {{ detail.cancelledByName }} - {{ detail.cancelReason }}</p>
        <p v-if="detail.externalReference" class="small mb-1">مرجع سپیدار: <span class="inv-ltr">{{ detail.externalReference }}</span> ({{ when(detail.sentAt) }})</p>
        <p v-if="detail.lastError" class="small text-danger mb-1">{{ detail.lastError }}</p>
        <p v-if="detail.nextAttemptAt" class="small mb-1">تلاش بعدی: {{ when(detail.nextAttemptAt) }}</p>
        <div v-if="!detail.canDecide && detail.status === 2" class="small inv-muted mb-1">{{ detail.cannotDecideReason }}</div>

        <h3 class="h6 mt-3">دادهٔ ارسالی (نسخهٔ طرح {{ num(detail.schemaVersion) }})</h3>
        <pre class="small inv-ltr border rounded p-2" style="max-height: 18rem; overflow: auto; white-space: pre-wrap; text-align: left">{{ detail.payloadPretty }}</pre>
        <p class="small inv-muted inv-ltr mb-2">{{ detail.payloadHash }}</p>

        <h3 class="h6">تاریخچهٔ تلاش‌ها</h3>
        <p v-if="!detail.attempts?.length" class="small inv-muted">تا کنون تلاشی برای ارسال انجام نشده است.</p>
        <table v-else class="table table-sm">
          <thead><tr><th>#</th><th>شروع</th><th>پایان</th><th>نتیجه</th><th>فرستنده</th><th>خطا یا مرجع</th></tr></thead>
          <tbody>
            <tr v-for="a in detail.attempts" :key="a.attemptNumber">
              <td>{{ num(a.attemptNumber) }}</td><td class="small">{{ when(a.startedAt) }}</td><td class="small">{{ when(a.finishedAt) }}</td><td>{{ spAttemptOutcomes[a.outcome] }}</td><td class="inv-ltr">{{ a.senderName }}</td><td class="small">{{ a.error || a.externalReference }}</td>
            </tr>
          </tbody>
        </table>

        <div class="d-flex gap-2 justify-content-end mt-3 flex-wrap">
          <template v-if="isDecider && detail.status === 2">
            <button :disabled="busy || !detail.canDecide" :title="detail.canDecide ? '' : detail.cannotDecideReason" class="btn btn-sm btn-success" @click="approve(detail)">تایید</button>
            <button :disabled="busy || !detail.canDecide" :title="detail.canDecide ? '' : detail.cannotDecideReason" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'reject', item: detail }">رد</button>
          </template>
          <template v-if="isDecider && (detail.status === 6 || detail.status === 3)">
            <button v-if="detail.status === 6" :disabled="busy || !detail.canFix" :title="detail.canFix ? '' : detail.cannotFixReason" class="btn btn-sm btn-outline-primary" @click="retry(detail)">تلاش دوباره</button>
            <button :disabled="busy || !detail.canFix" :title="detail.canFix ? '' : detail.cannotFixReason" class="btn btn-sm btn-outline-danger" @click="dialog = { kind: 'cancel', item: detail }">لغو</button>
          </template>
          <button class="btn btn-sm btn-outline-secondary" @click="closeDetail">بستن</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
