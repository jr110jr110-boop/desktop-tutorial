<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getFinanceCheck } from "@/api/FinanceAPIs";
import { fail } from "@/composables/inventory";
import { roleTitle } from "@/composables/finance";

// Is everything in place to send the accounting events to Sepidar?
const check = ref<any>(null);

async function load() {
  const [data, error] = await getFinanceCheck();
  if (error) return fail(error);
  check.value = data;
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div v-if="check" class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">آمادگی اتصال به سپیدار</h1>
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-secondary" @click="load">بررسی دوباره</button>
          <router-link :to="{ name: 'FinanceAccounts' }" class="btn btn-sm btn-outline-primary">نگاشت حساب‌ها</router-link>
        </div>
      </div>
      <div class="alert py-2" :class="check.ready ? 'alert-success' : 'alert-warning'">
        {{ check.ready ? "همه چیز آماده است؛ رویدادها را می‌توان خروجی گرفت." : "پیش از خروجی گرفتن، موارد زیر را کامل کنید." }}
      </div>
      <table class="table table-sm table-bordered">
        <tbody>
          <tr>
            <td>اتصال مستقیم به API سپیدار</td>
            <td><span class="inv-badge" :class="check.sepidarApiConfigured ? 'ok' : 'warn'">{{ check.sepidarApiConfigured ? "تنظیم شده" : "تنظیم نشده (فعلاً خروجی فایل CSV)" }}</span></td>
          </tr>
          <tr>
            <td>رویدادهای در انتظار ارسال</td>
            <td>
              {{ check.pendingEvents }}
              <router-link :to="{ name: 'FinanceEvents' }" class="ms-2">مشاهده</router-link>
            </td>
          </tr>
          <tr>
            <td>نقش‌های حساب بدون کد سپیدار</td>
            <td>
              <span v-if="!check.unmappedRoles.length" class="inv-badge ok">ندارد</span>
              <span v-for="r in check.unmappedRoles" :key="r" class="inv-badge warn me-1">{{ roleTitle(r) }}</span>
              <router-link v-if="check.unmappedRoles.length" :to="{ name: 'FinanceAccounts' }" class="ms-2">ثبت کد حساب</router-link>
            </td>
          </tr>
          <tr>
            <td>تامین‌کنندگان بدون کد سپیدار (دارای رویداد)</td>
            <td>
              <span v-if="!check.unmappedProviders.length" class="inv-badge ok">ندارد</span>
              <span v-for="p in check.unmappedProviders" :key="p" class="inv-badge warn me-1">{{ p }}</span>
              <router-link v-if="check.unmappedProviders.length" :to="{ name: 'InventorySepidar' }" class="ms-2">ثبت کدها</router-link>
            </td>
          </tr>
          <tr>
            <td>پروژه‌های بدون مرکز هزینه</td>
            <td><span class="inv-badge" :class="check.unmappedProjects ? 'warn' : 'ok'">{{ check.unmappedProjects }}</span></td>
          </tr>
          <tr>
            <td>کالاهای بدون کد سپیدار</td>
            <td><span class="inv-badge" :class="check.itemsWithoutSepidarCode ? 'warn' : 'ok'">{{ check.itemsWithoutSepidarCode }}</span></td>
          </tr>
          <tr>
            <td>انبارهای بدون کد سپیدار</td>
            <td><span class="inv-badge" :class="check.warehousesWithoutSepidarCode ? 'warn' : 'ok'">{{ check.warehousesWithoutSepidarCode }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
