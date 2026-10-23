<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";
import ExcelRows, { type ImportField } from "@/components/fn/ExcelRows.vue";
import { massImportVouchers } from "@/api/FnAPIs";
import { fail, ok } from "@/composables/inventory";

// Mass voucher import: one file with many vouchers (rows of the same group make one voucher). Always run the check first; nothing is saved by it.
const router = useRouter();
const fields: ImportField[] = [
  { key: "group", label: "گروه (شماره سند در فایل) *", words: ["گروه", "شماره سند", "group"], required: true },
  { key: "date", label: "تاریخ *", words: ["تاریخ", "date"], required: true, type: "date" },
  { key: "description", label: "شرح سند", words: ["شرح سند", "description"] },
  { key: "accountCode", label: "کد حساب *", words: ["کد حساب", "حساب", "account", "accountcode"], required: true },
  { key: "detail1Code", label: "کد تفصیلی اول", words: ["تفصیلی اول", "کد تفصیلی", "detail1", "detail1code"] },
  { key: "detail2Code", label: "کد تفصیلی دوم", words: ["تفصیلی دوم", "detail2", "detail2code"] },
  { key: "debit", label: "بدهکار", words: ["بدهکار", "debit"], type: "number" },
  { key: "credit", label: "بستانکار", words: ["بستانکار", "credit"], type: "number" },
  { key: "lineDescription", label: "شرح ردیف", words: ["شرح ردیف", "lineDescription", "شرح"] },
];
const rows = ref<any[]>([]);
const report = ref<any>(null);
const busy = ref(false);

function build() {
  return rows.value.map((r) => ({
    group: r.group, date: r.date, description: r.description, accountCode: r.accountCode, detail1Code: r.detail1Code, detail2Code: r.detail2Code,
    debit: r.debit || 0, credit: r.credit || 0, lineDescription: r.lineDescription,
  }));
}

async function run(dryRun: boolean) {
  if (!rows.value.length) return fail("ابتدا فایل را انتخاب کنید.");
  if (rows.value.some((r) => !r.date)) return fail("تاریخ برخی ردیف‌ها خوانده نشد؛ تاریخ شمسی بنویسید.");
  busy.value = true;
  const [d, error] = await massImportVouchers(dryRun, build());
  busy.value = false;
  if (error) return fail(error);
  report.value = d;
  if (dryRun) ok(d.errors.length ? "بررسی انجام شد؛ خطاها را رفع کنید." : "بررسی بدون خطا بود؛ می‌توانید ثبت کنید.");
  else ok(`${d.vouchers} سند پیش‌نویس ثبت شد.`);
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar"><h1 class="inv-title">ورود گروهی اسناد</h1></div>
      <ExcelRows :fields="fields" hint="هر گروه یک سند پیش‌نویس می‌شود و باید متوازن باشد. اسناد پس از ثبت مثل سایر پیش‌نویس‌ها باید کنترل و قطعی شوند." @rows="(r: any[]) => { rows = r; report = null; }" />
      <div class="d-flex gap-2 mb-3">
        <button :disabled="busy || !rows.length" class="btn btn-sm btn-outline-primary" @click="run(true)">بررسی (بدون ثبت)</button>
        <button :disabled="busy || !rows.length || !report || report.errors.length > 0 || !report.dryRun" class="btn btn-sm btn-primary" @click="run(false)">ثبت اسناد</button>
      </div>
      <div v-if="report">
        <p>
          <span :class="report.errors.length ? 'bad' : 'ok'" class="inv-badge">{{ report.dryRun ? "نتیجه بررسی" : "ثبت شد" }}</span>
          اسناد: <b>{{ report.vouchers }}</b> - ردیف‌ها: <b>{{ report.lines }}</b>
        </p>
        <ul v-if="report.errors.length" class="text-danger"><li v-for="(e, i) in report.errors" :key="i">{{ e }}</li></ul>
        <p v-if="report.voucherIds.length">
          <a v-for="id in report.voucherIds" :key="id" class="me-2" href="#" @click.prevent="router.push({ name: 'FnVoucherEdit', params: { id } })">سند {{ id }}</a>
        </p>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
