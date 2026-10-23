<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { generateVoucherTemplates, getVoucherTemplates, saveVoucherTemplate } from "@/api/FnAPIs";
import { fail, ok } from "@/composables/inventory";
import { money, parseAmount, yesNo } from "@/composables/fn";
import { accountLabel, searchAccounts, searchDetails } from "@/composables/fnLookups";
import { usePeriods } from "@/composables/fnF3";
import { FnRoles, hasRole } from "@/composables/roles";
import { useRouter } from "vue-router";

// Recurring voucher templates (rent, depreciation-like monthly entries): the accountant prepares them, the system drafts them for a period.
const router = useRouter();
const { periods, load: loadPeriods, label } = usePeriods();
const list = ref<any[]>([]);
const form = ref<any>(null);
const periodId = ref<number | null>(null);
const result = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(FnRoles.accountants);
const canGenerate = hasRole(FnRoles.systemJobs);

const formDebit = computed(() => (form.value ? form.value.lines.reduce((s: number, l: any) => s + (parseAmount(l.debit) || 0), 0) : 0));
const formCredit = computed(() => (form.value ? form.value.lines.reduce((s: number, l: any) => s + (parseAmount(l.credit) || 0), 0) : 0));

async function load() {
  const [d, error] = await getVoucherTemplates();
  if (error) return fail(error);
  list.value = d || [];
}

const blankLine = () => ({ account: null as any, detail1: null as any, detail2: null as any, debit: "", credit: "", description: "" });
function edit(t: any) {
  form.value = {
    id: t?.id || null, title: t?.title || "", description: t?.description || "", dayOfMonth: t?.dayOfMonth || 1, isActive: t ? t.isActive : true,
    lines: t ? t.lines.map((l: any) => ({ account: l.accountCode ? { id: 0, code: l.accountCode, title: "" } : null, detail1: l.detail1Id ? { id: l.detail1Id, code: "", title: "تفصیلی " + l.detail1Id } : null, detail2: l.detail2Id ? { id: l.detail2Id, code: "", title: "تفصیلی " + l.detail2Id } : null, debit: l.debit ? String(l.debit) : "", credit: l.credit ? String(l.credit) : "", description: l.description || "" })) : [blankLine(), blankLine()],
  };
}

async function save() {
  const f = form.value;
  if (!String(f.title).trim()) return fail("عنوان الگو را وارد کنید.");
  const lines = f.lines.filter((l: any) => l.account || parseAmount(l.debit) || parseAmount(l.credit)).map((l: any, i: number) => ({
    rowNumber: i + 1, accountCode: l.account?.code, detail1Id: l.detail1?.id || null, detail2Id: l.detail2?.id || null, debit: parseAmount(l.debit) || 0, credit: parseAmount(l.credit) || 0, description: l.description || null,
  }));
  if (lines.length < 2) return fail("الگو حداقل دو ردیف لازم دارد.");
  if (lines.some((l: any) => !l.accountCode)) return fail("حساب هر ردیف را انتخاب کنید.");
  if (formDebit.value !== formCredit.value) return fail("جمع بدهکار و بستانکار الگو برابر نیست.");
  busy.value = true;
  const [, error] = await saveVoucherTemplate({ id: f.id, title: f.title, description: f.description || null, dayOfMonth: Number(f.dayOfMonth) || 1, isActive: f.isActive, lines });
  busy.value = false;
  if (error) return fail(error);
  ok("الگوی سند ذخیره شد.");
  form.value = null;
  load();
}

async function generate() {
  if (!periodId.value) return fail("دوره را انتخاب کنید.");
  busy.value = true;
  const [d, error] = await generateVoucherTemplates(periodId.value);
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok(`${d.drafted} پیش‌نویس ساخته شد.`);
}

onMounted(() => {
  load();
  loadPeriods();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">الگوهای سند تکراری</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="edit(null)">الگوی جدید</button>
      </div>
      <div v-if="canGenerate" class="inv-filters mb-3">
        <select v-model="periodId" class="form-select form-select-sm"><option :value="null">دوره برای ساخت پیش‌نویس...</option><option v-for="p in periods" :key="p.id" :value="p.id">{{ label(p) }}</option></select>
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="generate">ساخت پیش‌نویس از الگوهای فعال</button>
      </div>
      <div v-if="result" class="alert alert-info py-2">
        ساخته‌شده: {{ result.drafted }} - ردشده: {{ result.skipped }}
        <span v-for="id in result.voucherIds" :key="id" class="me-2"><a href="#" @click.prevent="router.push({ name: 'FnVoucherEdit', params: { id } })">سند {{ id }}</a></span>
        <ul class="mb-0"><li v-for="(m, i) in result.messages" :key="i">{{ m }}</li></ul>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>عنوان</th><th>روز ماه</th><th>تعداد ردیف</th><th>جمع بدهکار (ریال)</th><th>فعال</th><th v-if="canWrite"></th></tr></thead>
        <tbody>
          <tr v-if="!list.length"><td colspan="6" class="text-center inv-muted">الگویی نیست.</td></tr>
          <tr v-for="t in list" :key="t.id">
            <td>{{ t.title }}<div class="inv-muted">{{ t.description }}</div></td><td>{{ t.dayOfMonth }}</td><td>{{ t.lines.length }}</td>
            <td class="fn-num">{{ money(t.lines.reduce((s: number, l: any) => s + (l.debit || 0), 0)) }}</td><td>{{ yesNo(t.isActive) }}</td>
            <td v-if="canWrite"><button class="btn btn-sm btn-outline-secondary" @click="edit(t)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش الگو" : "الگوی سند جدید" }}</h2>
        <div class="row g-2 mb-2">
          <div class="col-md-5"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">شرح سند</label><input v-model="form.description" class="form-control form-control-sm" /></div>
          <div class="col-md-1"><label class="form-label small mb-0">روز ماه</label><input v-model.number="form.dayOfMonth" class="form-control form-control-sm" max="31" min="1" type="number" /></div>
          <div class="col-md-2 d-flex align-items-end"><label class="form-check small"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> فعال</label></div>
        </div>
        <div class="table-responsive">
          <table class="table table-sm fn-lines">
            <thead><tr><th>#</th><th style="min-width: 220px">حساب *</th><th style="min-width: 160px">تفصیلی اول</th><th style="min-width: 160px">تفصیلی دوم</th><th>شرح</th><th>بدهکار</th><th>بستانکار</th><th></th></tr></thead>
            <tbody>
              <tr v-for="(l, i) in form.lines" :key="i">
                <td>{{ i + 1 }}</td>
                <td><LookupBox v-model="l.account" :fetch="searchAccounts" :label="accountLabel" placeholder="کد یا نام حساب" /></td>
                <td><LookupBox v-model="l.detail1" :fetch="searchDetails" :label="accountLabel" placeholder="تفصیلی" /></td>
                <td><LookupBox v-model="l.detail2" :fetch="searchDetails" :label="accountLabel" placeholder="تفصیلی" /></td>
                <td><input v-model="l.description" class="form-control form-control-sm" /></td>
                <td><input v-model="l.debit" class="form-control form-control-sm fn-num" /></td>
                <td><input v-model="l.credit" class="form-control form-control-sm fn-num" /></td>
                <td><button class="btn btn-sm btn-link text-danger p-0" @click="form.lines.length > 2 && form.lines.splice(i, 1)">حذف</button></td>
              </tr>
            </tbody>
            <tfoot><tr class="fn-total"><td colspan="5">جمع</td><td class="fn-num">{{ money(formDebit) }}</td><td class="fn-num">{{ money(formCredit) }}</td><td></td></tr></tfoot>
          </table>
        </div>
        <div class="d-flex gap-2 justify-content-between mt-2">
          <button class="btn btn-sm btn-outline-secondary" @click="form.lines.push(blankLine())">+ ردیف</button>
          <div class="d-flex gap-2"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
