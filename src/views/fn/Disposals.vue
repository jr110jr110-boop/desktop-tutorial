<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import { disposalAction, getAssets, getBankAccounts, getCashBoxes, getCustomers, getDisposals, saveDisposal } from "@/api/FnAPIs";
import { badgeOfDoc, disposalKinds, docStatuses, money, parseAmount, proceedsMethods, usePagedList } from "@/composables/fn";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Sale or scrapping of an asset: depreciation is caught up to the date, the gain or loss is calculated, the manager (not the preparer) approves and posts.
const filters = ref({ Status: null as number | null });
const list = usePagedList(getDisposals, filters);
const form = ref<any>(null);
const banks = ref<any[]>([]);
const boxes = ref<any[]>([]);
const customers = ref<any[]>([]);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);
const canApprove = hasRole(FnRoles.managers);
const rowsOf = (d: any): any[] => (Array.isArray(d) ? d : d?.data || []);
const searchAssets = async (t: string) => rowsOf((await getAssets({ Contains: t, PageSize: 15 }))[0]).filter((a: any) => [1, 2, 3].includes(a.status));
const assetLabel = (a: any) => `${a.code} - ${a.title}`;

function blank() {
  return { id: null, asset: null, date: today(), kind: 1, proceedsAmount: "", proceedsMethod: 0, cashBoxId: null, bankAccountId: null, customerId: null, note: "" };
}
async function save() {
  const f = form.value;
  if (!f.asset) return fail("دارایی را انتخاب کنید.");
  const proceeds = parseAmount(f.proceedsAmount) || 0;
  busy.value = true;
  const [, error] = await saveDisposal({
    id: f.id, assetId: f.asset.id, date: f.date, kind: Number(f.kind), proceedsAmount: proceeds, proceedsMethod: proceeds > 0 ? Number(f.proceedsMethod) : 0,
    cashBoxId: f.proceedsMethod === 1 ? f.cashBoxId : null, bankAccountId: f.proceedsMethod === 2 ? f.bankAccountId : null, customerId: f.proceedsMethod === 3 ? f.customerId : null, note: f.note || null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("پیش‌نویس واگذاری ثبت شد.");
  form.value = null;
  list.load();
}
function edit(d: any) {
  form.value = { id: d.id, asset: { id: d.assetId, code: d.assetCode, title: d.assetTitle }, date: String(d.date).slice(0, 10), kind: d.kind, proceedsAmount: d.proceedsAmount ? String(d.proceedsAmount) : "", proceedsMethod: d.proceedsMethod, cashBoxId: null, bankAccountId: null, customerId: null, note: d.note || "" };
}
async function act(d: any, action: string) {
  if (action === "cancel" && !confirm("پیش‌نویس باطل شود؟")) return;
  busy.value = true;
  const [, error] = await disposalAction({ id: d.id, action });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "واگذاری تایید و سند ثبت شد." : "باطل شد.");
  list.load();
}
onMounted(async () => {
  banks.value = rowsOf((await getBankAccounts({ PageSize: 200 }))[0]).filter((x: any) => x.isActive);
  boxes.value = rowsOf((await getCashBoxes({ PageSize: 200 }))[0]).filter((x: any) => x.isActive);
  customers.value = rowsOf((await getCustomers({ PageSize: 200 }))[0]).filter((x: any) => x.isActive);
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">فروش و اسقاط دارایی</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = blank()">واگذاری جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in docStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>دارایی</th><th>نوع</th><th>بها</th><th>استهلاک تا تاریخ</th><th>ارزش دفتری</th><th>وجه دریافتی</th><th>سود (زیان)</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="11" class="text-center inv-muted">موردی نیست.</td></tr>
            <tr v-for="d in list.rows.value" :key="d.id">
              <td class="inv-ltr">{{ d.number }}</td><td>{{ jalali(d.date) }}</td><td>{{ d.assetCode }} - {{ d.assetTitle }}</td><td>{{ disposalKinds[d.kind] }}</td>
              <td class="fn-num">{{ money(d.cost) }}</td><td class="fn-num">{{ money(d.accumulatedAtDisposal) }}</td><td class="fn-num">{{ money(d.bookValue) }}</td>
              <td class="fn-num">{{ money(d.proceedsAmount) }} <span class="inv-muted">{{ proceedsMethods[d.proceedsMethod] }}</span></td>
              <td class="fn-num"><span :class="d.gainLoss < 0 ? 'bad' : 'ok'" class="inv-badge">{{ money(d.gainLoss) }}</span></td>
              <td><span :class="badgeOfDoc(d.status)" class="inv-badge">{{ docStatuses[d.status] }}</span></td>
              <td class="text-nowrap">
                <template v-if="d.status === 0">
                  <button v-if="canWrite" class="btn btn-sm btn-outline-secondary me-1" @click="edit(d)">ویرایش</button>
                  <button v-if="canApprove" :disabled="busy" class="btn btn-sm btn-success me-1" @click="act(d, 'approve')">تایید</button>
                  <button v-if="canWrite" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act(d, 'cancel')">ابطال</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش واگذاری" : "واگذاری دارایی" }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">دارایی *</label><LookupBox v-model="form.asset" :disabled="!!form.id" :fetch="searchAssets" :label="assetLabel" placeholder="کد یا عنوان دارایی" /></div>
          <div class="col-6"><label class="form-label small mb-0">تاریخ</label><FnDate v-model="form.date" /></div>
          <div class="col-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in disposalKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">وجه دریافتی (ریال)</label><input v-model="form.proceedsAmount" class="form-control form-control-sm fn-num" /></div>
          <div class="col-6"><label class="form-label small mb-0">روش دریافت</label><select v-model="form.proceedsMethod" class="form-select form-select-sm"><option v-for="(t, k) in proceedsMethods" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div v-if="form.proceedsMethod === 1" class="col-12"><label class="form-label small mb-0">صندوق</label><select v-model="form.cashBoxId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="b in boxes" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
          <div v-if="form.proceedsMethod === 2" class="col-12"><label class="form-label small mb-0">حساب بانکی</label><select v-model="form.bankAccountId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="b in banks" :key="b.id" :value="b.id">{{ b.title }}</option></select></div>
          <div v-if="form.proceedsMethod === 3" class="col-12"><label class="form-label small mb-0">مشتری (بدهکار)</label><select v-model="form.customerId" class="form-select form-select-sm"><option :value="null">انتخاب...</option><option v-for="c in customers" :key="c.id" :value="c.id">{{ c.name }}</option></select></div>
          <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
