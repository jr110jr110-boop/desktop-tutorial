<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import ContractPick from "@/components/fn/ContractPick.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import { getClearances, getRetentionStatus, releaseRetention, saveClearance } from "@/api/FnAPIs";
import { getSuppliers } from "@/api/ProcurementAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { money, parseAmount, yesNo } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Contractors: social-insurance clearance letters (مفاصاحساب بیمه) and the retention (حسن انجام کار) held, released and releasable; a release needs a valid clearance.
const tab = ref("clearance");
const tabs = [{ key: "clearance", title: "مفاصاحساب بیمه" }, { key: "retention", title: "حسن انجام کار پیمانکار" }];
const canWrite = hasRole(FnRoles.accountants);
const busy = ref(false);
const suppliers = ref<any[]>([]);
const providerId = ref<number | null>(null);
const list = ref<any[]>([]);
const form = ref<any>(null);

const supplierName = (id: number) => suppliers.value.find((s) => s.id === id)?.name || id;
async function load() {
  const [d, error] = await getClearances(providerId.value, null);
  if (error) return fail(error);
  list.value = d || [];
}
async function save() {
  const f = form.value;
  if (!f.providerId || !String(f.letterNo).trim() || !f.issueDate || !f.validUntil) return fail("پیمانکار، شماره نامه و تاریخ‌ها را وارد کنید.");
  if (f.validUntil < f.issueDate) return fail("اعتبار مفاصاحساب پیش از تاریخ صدور است.");
  busy.value = true;
  const [, error] = await saveClearance({ id: f.id || null, providerId: f.providerId, contractId: f.contractId || null, letterNo: f.letterNo, issueDate: f.issueDate, validUntil: f.validUntil, documentId: f.documentId || null, isRevoked: f.isRevoked, note: f.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("مفاصاحساب ثبت شد.");
  form.value = null;
  load();
}

// retention
const q = ref<any>({ providerId: null as number | null, contractId: null as number | null, asOf: null as string | null });
const status = ref<any>(null);
const rel = ref<any>({ date: today(), amount: "", note: "" });
const released = ref<any>(null);
async function checkStatus() {
  if (!q.value.providerId) return fail("پیمانکار را انتخاب کنید.");
  const [d, error] = await getRetentionStatus(q.value.providerId, q.value.contractId, q.value.asOf);
  if (error) return fail(error);
  status.value = d;
  rel.value.amount = d.canRelease ? String(d.releasable) : "";
  released.value = null;
}
async function release() {
  const amount = parseAmount(rel.value.amount) || 0;
  if (amount <= 0) return fail("مبلغ آزادسازی را وارد کنید.");
  if (amount > status.value.releasable) return fail("مبلغ از حسن انجام کار قابل آزادسازی بیشتر است.");
  busy.value = true;
  const [d, error] = await releaseRetention({ providerId: q.value.providerId, contractId: q.value.contractId, date: rel.value.date || today(), amount, note: rel.value.note || null });
  busy.value = false;
  if (error) return fail(error);
  ok("آزادسازی ثبت و سند صادر شد.");
  released.value = d;
  checkStatus();
}

onMounted(async () => {
  load();
  const [s] = await getSuppliers();
  suppliers.value = Array.isArray(s) ? s : s?.data || [];
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">پیمانکاران: مفاصاحساب و حسن انجام کار</h1>
        <button v-if="canWrite && tab === 'clearance'" class="btn btn-sm btn-primary" @click="form = { providerId: providerId, contractId: null, letterNo: '', issueDate: today(), validUntil: null, documentId: null, isRevoked: false, note: '' }">مفاصاحساب جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'clearance'">
        <div class="inv-filters mb-3"><select v-model="providerId" class="form-select form-select-sm" @change="load"><option :value="null">همه پیمانکاران</option><option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option></select></div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>پیمانکار</th><th>پروژه</th><th>شماره نامه</th><th>صدور</th><th>اعتبار تا</th><th>سند پیوست</th><th>وضعیت</th><th v-if="canWrite"></th></tr></thead>
          <tbody>
            <tr v-if="!list.length"><td colspan="9" class="text-center inv-muted">مفاصاحسابی ثبت نشده است.</td></tr>
            <tr v-for="c in list" :key="c.id">
              <td class="inv-ltr">{{ c.number }}</td><td>{{ c.providerName }}</td><td>{{ c.contractId }}</td><td class="inv-ltr">{{ c.letterNo }}</td><td>{{ jalali(c.issueDate) }}</td><td>{{ jalali(c.validUntil) }}</td><td>{{ yesNo(c.hasDocument) }}</td>
              <td><span :class="c.isValid ? 'ok' : 'bad'" class="inv-badge">{{ c.isRevoked ? "ابطال‌شده" : c.isValid ? "معتبر" : "منقضی" }}</span></td>
              <td v-if="canWrite"><button class="btn btn-sm btn-outline-secondary" @click="form = { ...c, validUntil: c.validUntil, documentId: c.documentId }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <div class="row g-2 mb-3">
          <div class="col-md-3"><label class="form-label small mb-0">پیمانکار *</label><select v-model="q.providerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">پروژه</label><ContractPick v-model="q.contractId" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="q.asOf" /></div>
          <div class="col-md-3 d-flex align-items-end"><button class="btn btn-sm btn-primary" @click="checkStatus">وضعیت</button></div>
        </div>
        <template v-if="status">
          <table class="table table-sm" style="max-width: 560px">
            <tbody>
              <tr><td>پیمانکار</td><td>{{ status.providerName || supplierName(status.providerId) }}</td></tr>
              <tr><td>نگهداری‌شده (همه سال‌ها)</td><td class="fn-num">{{ money(status.held) }}</td></tr>
              <tr><td>آزادشده</td><td class="fn-num">{{ money(status.released) }}</td></tr>
              <tr class="fn-total"><td>قابل آزادسازی</td><td class="fn-num">{{ money(status.releasable) }}</td></tr>
              <tr><td>مفاصاحساب معتبر</td><td>{{ status.validClearanceId ? "شناسه " + status.validClearanceId : "ندارد" }}</td></tr>
            </tbody>
          </table>
          <p v-if="status.message" :class="status.canRelease ? '' : 'text-danger'">{{ status.message }}</p>
          <div v-if="canWrite && status.canRelease" class="row g-2" style="max-width: 720px">
            <div class="col-md-4"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="rel.date" /></div>
            <div class="col-md-4"><label class="form-label small mb-0">مبلغ آزادسازی (ریال)</label><input v-model="rel.amount" class="form-control form-control-sm fn-num" /></div>
            <div class="col-md-4"><label class="form-label small mb-0">توضیح</label><input v-model="rel.note" class="form-control form-control-sm" /></div>
            <div class="col-12"><button :disabled="busy" class="btn btn-sm btn-success" @click="release">آزادسازی و صدور سند</button></div>
          </div>
          <p v-if="released" class="mt-2">آزادسازی {{ released.number }}: {{ money(released.amount) }} ریال - سند {{ released.voucherId }}</p>
        </template>
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">مفاصاحساب بیمه</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">پیمانکار *</label><select v-model="form.providerId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">پروژه</label><ContractPick v-model="form.contractId" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شماره نامه *</label><input v-model="form.letterNo" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ صدور *</label><FnDate v-model="form.issueDate" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">اعتبار تا *</label><FnDate v-model="form.validUntil" /></div>
          <div class="col-12"><label class="form-label small mb-0">تصویر نامه (از مخزن اسناد)</label><DocAttach v-model="form.documentId" title="مفاصاحساب بیمه" /></div>
          <div class="col-md-6"><label class="form-check small"><input v-model="form.isRevoked" class="form-check-input" type="checkbox" /> ابطال‌شده</label></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
