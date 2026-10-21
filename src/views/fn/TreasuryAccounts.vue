<script setup lang="ts">
import { onMounted, ref } from "vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import { getBankAccounts, getCashBoxes, saveBankAccount, saveCashBox } from "@/api/FnAPIs";
import { fail, ok } from "@/composables/inventory";
import { money, usePagedList, yesNo } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Bank accounts and cash boxes (the balance comes from the accounting events of each account).
const tab = ref("banks");
const tabs = [{ key: "banks", title: "حساب‌های بانکی" }, { key: "boxes", title: "صندوق‌ها" }];
const bankFilters = ref({ Contains: "" });
const boxFilters = ref({ Contains: "" });
const banks = usePagedList(getBankAccounts, bankFilters);
const boxes = usePagedList(getCashBoxes, boxFilters);
const form = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.treasury);

const newBank = () => ({ id: null, title: "", bankName: "", branch: "", accountNumber: "", iban: "", currencyCode: "IRR", isActive: true });
const newBox = () => ({ id: null, title: "", custodianTitle: "", isActive: true });

async function save() {
  const f = form.value;
  if (!String(f.title).trim()) return fail("عنوان را وارد کنید.");
  busy.value = true;
  const isBank = tab.value === "banks";
  const [, error] = isBank
    ? await saveBankAccount({ id: f.id, title: f.title, bankName: f.bankName, branch: f.branch, accountNumber: f.accountNumber, iban: f.iban, currencyCode: f.currencyCode || null, isActive: !!f.isActive })
    : await saveCashBox({ id: f.id, title: f.title, custodianTitle: f.custodianTitle, isActive: !!f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  form.value = null;
  (isBank ? banks : boxes).load();
}

onMounted(() => {
  banks.load();
  boxes.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">حساب‌های بانکی و صندوق‌ها</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = tab === 'banks' ? newBank() : newBox()">{{ tab === "banks" ? "حساب بانکی جدید" : "صندوق جدید" }}</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'banks'">
        <div class="inv-filters mb-3">
          <input v-model="bankFilters.Contains" class="form-control form-control-sm" placeholder="عنوان، بانک یا شماره حساب" @keydown.enter="banks.search" />
          <button class="btn btn-sm btn-secondary" @click="banks.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>عنوان</th><th>بانک</th><th>شعبه</th><th>شماره حساب</th><th>شبا</th><th>ارز</th><th>حساب دفتر کل</th><th>مانده (ریال)</th><th>فعال</th><th v-if="canWrite"></th></tr></thead>
            <tbody>
              <tr v-if="!banks.rows.value.length"><td colspan="10" class="text-center inv-muted">حساب بانکی‌ای نیست.</td></tr>
              <tr v-for="b in banks.rows.value" :key="b.id" :class="{ 'opacity-50': !b.isActive }">
                <td>{{ b.title }}</td><td>{{ b.bankName }}</td><td>{{ b.branch }}</td><td class="inv-ltr">{{ b.accountNumber }}</td><td class="inv-ltr">{{ b.iban }}</td>
                <td class="inv-ltr">{{ b.currencyCode }}</td><td class="inv-ltr">{{ b.accountCode }}</td><td class="fn-num">{{ money(b.balance) }}</td><td>{{ yesNo(b.isActive) }}</td>
                <td v-if="canWrite"><button class="btn btn-sm btn-link p-0" @click="form = { ...b }">ویرایش</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="banks.page.value" :total="banks.total.value" @go="banks.go" />
      </template>

      <template v-else>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>عنوان</th><th>عهده‌دار</th><th>مانده (ریال)</th><th>فعال</th><th v-if="canWrite"></th></tr></thead>
            <tbody>
              <tr v-if="!boxes.rows.value.length"><td colspan="5" class="text-center inv-muted">صندوقی نیست.</td></tr>
              <tr v-for="b in boxes.rows.value" :key="b.id" :class="{ 'opacity-50': !b.isActive }">
                <td>{{ b.title }}</td><td>{{ b.custodianTitle }}</td><td class="fn-num">{{ money(b.balance) }}</td><td>{{ yesNo(b.isActive) }}</td>
                <td v-if="canWrite"><button class="btn btn-sm btn-link p-0" @click="form = { ...b }">ویرایش</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="boxes.page.value" :total="boxes.total.value" @go="boxes.go" />
      </template>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ tab === "banks" ? "حساب بانکی" : "صندوق" }}</h2>
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <template v-if="tab === 'banks'">
            <div class="col-6"><label class="form-label small mb-0">نام بانک</label><input v-model="form.bankName" class="form-control form-control-sm" /></div>
            <div class="col-6"><label class="form-label small mb-0">شعبه</label><input v-model="form.branch" class="form-control form-control-sm" /></div>
            <div class="col-6"><label class="form-label small mb-0">شماره حساب</label><input v-model="form.accountNumber" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-6"><label class="form-label small mb-0">کد ارز (ریال = IRR)</label><input v-model="form.currencyCode" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-12"><label class="form-label small mb-0">شماره شبا</label><input v-model="form.iban" class="form-control form-control-sm inv-ltr" /></div>
          </template>
          <div v-else class="col-12"><label class="form-label small mb-0">عهده‌دار صندوق</label><input v-model="form.custodianTitle" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-check small"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
