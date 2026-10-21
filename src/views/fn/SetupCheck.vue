<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getSetupCheck } from "@/api/FnAPIs";
import { fail } from "@/composables/inventory";

// What is missing before the ledger, the fixed assets and the payroll can be used (fiscal year, chart, account roles, tax and payroll rates).
const check = ref<any>(null);
const loading = ref(true);

async function load() {
  loading.value = true;
  const [data, error] = await getSetupCheck();
  loading.value = false;
  if (error) return fail(error);
  check.value = data;
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">بررسی آمادگی حسابداری</h1>
        <button :disabled="loading" class="btn btn-sm btn-outline-secondary" @click="load">بررسی دوباره</button>
      </div>
      <template v-if="check">
        <div :class="check.ok ? 'alert-success' : 'alert-danger'" class="alert py-2">
          {{ check.ok ? "دفتر کل آماده است؛ کمبودی پیدا نشد." : "پیش از استفاده از دفتر کل، کمبودهای زیر را برطرف کنید." }}
        </div>
        <table v-if="check.problems && check.problems.length" class="table table-sm table-bordered">
          <thead><tr><th style="width: 220px">کد</th><th>مشکل</th></tr></thead>
          <tbody>
            <tr v-for="p in check.problems" :key="p.code"><td class="inv-ltr">{{ p.code }}</td><td>{{ p.message }}</td></tr>
          </tbody>
        </table>

        <div v-if="check.toVerify && check.toVerify.length" class="mt-3">
          <div class="fn-section">نرخ‌هایی که مالی باید یک‌بار تایید کند</div>
          <ul class="mb-0"><li v-for="(v, i) in check.toVerify" :key="i">{{ v }}</li></ul>
        </div>

        <div v-if="check.modules && check.modules.length" class="mt-3">
          <div class="fn-section">ماژول‌های اختیاری (دارایی‌ها و حقوق و دستمزد)</div>
          <table class="table table-sm table-bordered">
            <thead><tr><th>ماژول</th><th>وضعیت</th><th>توضیح</th><th>موارد کم‌شده</th></tr></thead>
            <tbody>
              <tr v-for="m in check.modules" :key="m.module">
                <td>{{ m.module === "Assets" ? "دارایی‌های ثابت" : m.module === "Payroll" ? "حقوق و دستمزد" : m.module }}</td>
                <td><span :class="m.ok ? 'ok' : 'bad'" class="inv-badge">{{ m.ok ? "آماده" : "ناقص" }}</span></td>
                <td>{{ m.message }}</td>
                <td><span v-for="k in m.missing" :key="k" class="inv-badge warn me-1 inv-ltr">{{ k }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
