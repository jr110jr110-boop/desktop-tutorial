<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { getAssetClasses, saveAssetClass } from "@/api/FnAPIs";
import { depreciationMethods, depreciationStarts, parseAmount } from "@/composables/fn";
import { accountLabel, searchAccounts } from "@/composables/fnLookups";
import { fail, ok } from "@/composables/inventory";
import { FnRoles, hasRole } from "@/composables/roles";

// Asset classes: the depreciation rule (method, rate or useful life, start rule, idle rule) and the two ledger accounts of the class.
// Rates are entered by finance and are never built into the program: a class without a rate blocks the depreciation run.
const rows = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);

async function load() {
  const [d, error] = await getAssetClasses();
  if (error) return fail(error);
  rows.value = Array.isArray(d) ? d : d?.data || [];
}

const num = (v: any) => (String(v ?? "").trim() === "" ? null : parseAmount(String(v)));

function edit(c: any | null) {
  form.value = c
    ? { ...c, assetAccount: { code: c.assetAccountCode, title: "" }, accAccount: { code: c.accumulatedAccountCode, title: "" }, ratePercent: c.ratePercent ?? "", usefulLifeYears: c.usefulLifeYears ?? "" }
    : { id: null, code: "", title: "", assetAccount: null, accAccount: null, method: 1, ratePercent: "", usefulLifeYears: "", startRule: 1, idleRatePercent: "", idleAfterMonths: "", salvagePercent: "", source: "", verify: false, isIntangible: false, isActive: true };
}

async function save() {
  const f = form.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail("کد و عنوان رده را وارد کنید.");
  if (!f.assetAccount?.code || !f.accAccount?.code) return fail("حساب دارایی و حساب استهلاک انباشته را انتخاب کنید.");
  busy.value = true;
  const [, error] = await saveAssetClass({
    id: f.id, code: String(f.code).trim().toUpperCase(), title: f.title, assetAccountCode: f.assetAccount.code, accumulatedAccountCode: f.accAccount.code, method: Number(f.method),
    ratePercent: num(f.ratePercent), usefulLifeYears: num(f.usefulLifeYears), startRule: Number(f.startRule), idleRatePercent: num(f.idleRatePercent), idleAfterMonths: num(f.idleAfterMonths), salvagePercent: num(f.salvagePercent),
    source: f.source || null, verify: !!f.verify, isIntangible: !!f.isIntangible, isActive: !!f.isActive,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("رده ذخیره شد.");
  form.value = null;
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">رده‌های دارایی ثابت</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="edit(null)">رده جدید</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>کد</th><th>عنوان</th><th>روش</th><th>نرخ (٪)</th><th>عمر مفید (سال)</th><th>شروع</th><th>حساب دارایی</th><th>حساب انباشته</th><th>نرخ</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="10" class="text-center inv-muted">رده‌ای نیست.</td></tr>
            <tr v-for="c in rows" :key="c.id">
              <td class="inv-ltr">{{ c.code }}</td><td>{{ c.title }}<span v-if="c.isIntangible" class="inv-badge info me-1">نامشهود</span><span v-if="!c.isActive" class="inv-badge bad me-1">غیرفعال</span></td>
              <td>{{ depreciationMethods[c.method] }}</td><td class="fn-num">{{ c.ratePercent ?? "" }}</td><td class="fn-num">{{ c.usefulLifeYears ?? "" }}</td><td>{{ depreciationStarts[c.startRule] }}</td>
              <td class="inv-ltr">{{ c.assetAccountCode }}</td><td class="inv-ltr">{{ c.accumulatedAccountCode }}</td>
              <td><span :class="c.hasRate ? 'ok' : 'bad'" class="inv-badge">{{ c.hasRate ? "تعیین شده" : "تعیین نشده" }}</span><span v-if="c.verify" class="inv-badge warn me-1">بررسی شود</span></td>
              <td><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="edit(c)">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش رده" : "رده جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">کد (انگلیسی و عدد) *</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-5"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">روش استهلاک</label><select v-model="form.method" class="form-select form-select-sm"><option v-for="(t, k) in depreciationMethods" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">حساب دارایی (معین) *</label><LookupBox v-model="form.assetAccount" :fetch="searchAccounts" :label="accountLabel" :fallback-text="form.assetAccount?.code" placeholder="کد یا نام حساب" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">حساب استهلاک انباشته (معین) *</label><LookupBox v-model="form.accAccount" :fetch="searchAccounts" :label="accountLabel" :fallback-text="form.accAccount?.code" placeholder="کد یا نام حساب" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">نرخ استهلاک سالانه (٪)</label><input v-model="form.ratePercent" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">عمر مفید (سال)</label><input v-model="form.usefulLifeYears" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">شروع استهلاک</label><select v-model="form.startRule" class="form-select form-select-sm"><option v-for="(t, k) in depreciationStarts" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-3"><label class="form-label small mb-0">ارزش اسقاط (٪)</label><input v-model="form.salvagePercent" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">نرخ دوره بلااستفاده (٪)</label><input v-model="form.idleRatePercent" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">بلااستفاده پس از (ماه)</label><input v-model="form.idleAfterMonths" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مأخذ نرخ (ماده قانون / بخشنامه)</label><input v-model="form.source" class="form-control form-control-sm" /></div>
          <div class="col-12 d-flex gap-3">
            <label class="form-check small"><input v-model="form.verify" class="form-check-input" type="checkbox" /> نیاز به راستی‌آزمایی</label>
            <label class="form-check small"><input v-model="form.isIntangible" class="form-check-input" type="checkbox" /> دارایی نامشهود</label>
            <label class="form-check small"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label>
          </div>
        </div>
        <p class="inv-muted small mt-2 mb-0">نرخ یا عمر مفید را مالی تعیین می‌کند؛ تا تعیین نشود استهلاک این رده محاسبه نمی‌شود.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
