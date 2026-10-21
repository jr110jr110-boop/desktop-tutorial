<script setup lang="ts">
import { onMounted, ref } from "vue";
import PromptModal from "@/components/fn/PromptModal.vue";
import QPager from "@/components/quality/QPager.vue";
import { downloadOutboxPackage, generateOutbox, getOutboxPackage, getOutboxPackages, getOutboxSummary, outboxAction } from "@/api/FnAPIs";
import { jalaliNow, monthNames, outboxKinds, outboxStatuses, parseAmount, seasonNames, usePagedList } from "@/composables/fn";
import { fail, jalali, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Outbox of the accounting package: tax and insurance data is prepared here, approved by another person and handed to the Sepidar connector (the only external link).
const filters = ref({ Kind: null as number | null, Status: null as number | null, Year: null as number | null, Month: null as number | null });
const list = usePagedList(getOutboxPackages, filters);
const summary = ref<any>(null);
const detail = ref<any>(null);
const form = ref<any>(null);
const dialog = ref<null | { pkg: any; action: string; title: string; fields: any[] }>(null);
const busy = ref(false);
const now = jalaliNow();
const isPayroll = (kind: number) => kind === 3 || kind === 4;
const canPrepare = (kind: number) => (isPayroll(kind) ? hasRole(FnRoles.payrollUsers) : hasRole(FnRoles.accountants));
const canApprove = (kind: number) => (isPayroll(kind) ? hasRole(FnRoles.payrollUsers) : hasRole(FnRoles.managers));
const canGenerateAny = hasRole(FnRoles.outboxPrepare) || hasRole(FnRoles.payrollUsers);
const badge = (s: number) => (s === 4 ? "ok" : s === 5 ? "bad" : s === 3 ? "info" : s === 6 ? "" : "warn");
const periodText = (p: any) => (p.season ? `${seasonNames[p.season]} ${p.year}` : p.month ? `${monthNames[p.month - 1]} ${p.year}` : String(p.year));

async function loadSummary() {
  const [d] = await getOutboxSummary();
  summary.value = d;
}
function refresh() {
  list.load();
  loadSummary();
}

async function generate() {
  const f = form.value;
  const dto: any = { kind: Number(f.kind), year: parseAmount(f.year) || 0, note: f.note || null };
  if (f.kind === 2) dto.season = Number(f.season);
  else dto.month = Number(f.month);
  busy.value = true;
  const [, error] = await generateOutbox(dto);
  busy.value = false;
  if (error) return fail(error);
  ok("بسته آماده شد؛ پس از بررسی، شخص دیگری آن را تایید کند.");
  form.value = null;
  refresh();
}

function ask(pkg: any, action: string) {
  if (action === "approve" || action === "send" || action === "mark-accepted") return run(pkg, action, {});
  const note = { key: "note", label: "دلیل رد *", type: "textarea" as const, required: true };
  const ref1 = { key: "reference", label: "شماره پیگیری / مرجع ارسال *", required: true };
  dialog.value = action === "mark-sent" ? { pkg, action, title: "ثبت ارسال دستی", fields: [ref1] } : { pkg, action, title: "ثبت رد بسته", fields: [note] };
}
async function run(pkg: any, action: string, v: any) {
  dialog.value = null;
  busy.value = true;
  const [, error] = await outboxAction(pkg.id, action, v.note, v.reference);
  busy.value = false;
  if (error) {
    fail(error);
    return refresh();
  }
  ok("انجام شد.");
  refresh();
}
async function exportFile(pkg: any) {
  const message = await downloadOutboxPackage(pkg.id);
  if (message) return fail(message);
  refresh();
}
async function open(pkg: any) {
  const [d, error] = await getOutboxPackage(pkg.id);
  if (error) return fail(error);
  detail.value = d;
}
onMounted(refresh);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">صندوق خروجی سپیدار (مالیات و بیمه)</h1>
        <button v-if="canGenerateAny" class="btn btn-sm btn-primary" @click="form = { kind: 1, year: String(now.year), month: now.month, season: Math.ceil(now.month / 3), note: '' }">ساخت بسته جدید</button>
      </div>
      <div v-if="summary" class="fn-tiles mb-3">
        <div class="fn-tile"><div class="t">در انتظار تایید</div><div class="v">{{ summary.pending }}</div></div>
        <div class="fn-tile"><div class="t">آماده ارسال</div><div class="v">{{ summary.readyToSend }}</div></div>
        <div :class="summary.failed ? 'bad' : 'ok'" class="fn-tile"><div class="t">ناموفق یا ردشده</div><div class="v">{{ summary.failed }}</div></div>
        <div class="fn-tile"><div class="t">آخرین ارسال</div><div class="v">{{ summary.lastSentAt ? jalali(summary.lastSentAt, true) : "-" }}</div></div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in outboxKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in outboxStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <input v-model.number="filters.Year" class="form-control form-control-sm fn-num" placeholder="سال" style="max-width: 7rem" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>نوع</th><th>دوره</th><th>نسخه</th><th>تعداد ردیف</th><th>تهیه‌کننده</th><th>تاییدکننده</th><th>مرجع</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="10" class="text-center inv-muted">بسته‌ای نیست.</td></tr>
            <tr v-for="p in list.rows.value" :key="p.id">
              <td class="inv-ltr">{{ p.number }}</td><td>{{ outboxKinds[p.kind] }}</td><td>{{ periodText(p) }}</td><td>{{ p.version }}</td><td>{{ p.rowCount }}</td><td>{{ p.creatorName }}</td><td>{{ p.approvedByName }}</td><td class="inv-ltr">{{ p.externalReference }}</td>
              <td><span :class="badge(p.status)" class="inv-badge">{{ outboxStatuses[p.status] }}</span><div v-if="p.error" class="small text-danger">{{ p.error }}</div></td>
              <td class="text-nowrap">
                <button class="btn btn-sm btn-link p-0 me-2" @click="open(p)">جزئیات</button>
                <button v-if="p.status >= 1 && p.status <= 5" class="btn btn-sm btn-link p-0 me-2" @click="exportFile(p)">خروجی اکسل</button>
                <template v-if="p.status === 0 && canApprove(p.kind)"><button :disabled="busy" class="btn btn-sm btn-success me-1" @click="ask(p, 'approve')">تایید</button></template>
                <template v-if="(p.status === 1 || p.status === 2) && canPrepare(p.kind)">
                  <button :disabled="busy" class="btn btn-sm btn-outline-primary me-1" @click="ask(p, 'send')">ارسال به سپیدار</button>
                  <button :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="ask(p, 'mark-sent')">ثبت ارسال دستی</button>
                </template>
                <template v-if="p.status === 3 && canPrepare(p.kind)">
                  <button :disabled="busy" class="btn btn-sm btn-success me-1" @click="ask(p, 'mark-accepted')">پذیرفته شد</button>
                  <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="ask(p, 'mark-rejected')">رد شد</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      <p class="inv-muted small mb-0">بسته‌ای که خروجی گرفته شده تغییر نمی‌کند؛ ساخت دوباره، نسخه جدید می‌سازد و نسخه قبلی را جایگزین‌شده می‌کند. تایید باید توسط شخصی غیر از تهیه‌کننده باشد.</p>
    </div>

    <PromptModal v-if="dialog" :fields="dialog.fields" :title="dialog.title + ' - ' + dialog.pkg.number" @cancel="dialog = null" @confirm="(v: any) => run(dialog!.pkg, dialog!.action, v)" />

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">ساخت بسته جدید</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in outboxKinds" :key="k" :value="Number(k)" :disabled="!canPrepare(Number(k))">{{ t }}</option></select></div>
          <div class="col-4"><label class="form-label small mb-0">سال</label><input v-model="form.year" class="form-control form-control-sm fn-num" /></div>
          <div v-if="form.kind === 2" class="col-8"><label class="form-label small mb-0">فصل</label><select v-model="form.season" class="form-select form-select-sm"><option v-for="(t, k) in seasonNames" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div v-else class="col-8"><label class="form-label small mb-0">ماه</label><select v-model="form.month" class="form-select form-select-sm"><option v-for="(m, i) in monthNames" :key="i" :value="i + 1">{{ m }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="generate">ساخت بسته</button></div>
      </div>
    </div>

    <div v-if="detail" class="inv-modal-backdrop screen-only" @click.self="detail = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ detail.number }} - {{ outboxKinds[detail.kind] }}</h2>
        <p class="small">دوره: {{ jalali(detail.fromDate) }} تا {{ jalali(detail.toDate) }} | نسخه {{ detail.version }} | وضعیت: {{ outboxStatuses[detail.status] }}</p>
        <p class="small">تهیه‌کننده: {{ detail.creatorName }} | تاییدکننده: {{ detail.approvedByName }} <span v-if="detail.approvedAt">({{ jalali(detail.approvedAt, true) }})</span></p>
        <p v-if="detail.sentAt" class="small">ارسال: {{ jalali(detail.sentAt, true) }} | مرجع: <span class="inv-ltr">{{ detail.externalReference }}</span></p>
        <p v-if="detail.note" class="small">توضیح: {{ detail.note }}</p>
        <table class="table table-sm"><thead><tr><th>جدول</th><th>تعداد ردیف</th></tr></thead><tbody><tr v-for="t in detail.tables" :key="t.name"><td class="inv-ltr">{{ t.name }}</td><td>{{ t.rows }}</td></tr></tbody></table>
        <p class="small inv-muted inv-ltr">{{ detail.contentHash }}</p>
        <div class="text-end"><button class="btn btn-sm btn-outline-secondary" @click="detail = null">بستن</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
