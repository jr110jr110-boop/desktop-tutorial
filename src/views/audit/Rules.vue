<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getRules, rowsOf } from "@/api/AuditAPIs";
import { fail, printPage } from "@/composables/inventory";
import { severityClass } from "@/composables/audit";

// Rule catalogue (read only). Switching a rule off is a security-relevant change: it goes through the two-person route of the safety profile, never from this page.
const rows = ref<any[]>([]);
const text = ref("");
const shown = computed(() => rows.value.filter((r) => !text.value || (r.code + r.title + r.clause + r.processTitle).toLowerCase().includes(text.value.toLowerCase())));
onMounted(async () => {
  const [d, e] = await getRules();
  if (e) return fail(e);
  rows.value = rowsOf(d);
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="فهرست قواعد ممیزی" subtitle="فقط خواندنی" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">فهرست قواعد ممیزی ({{ rows.length }})</h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'SafetyProfile' }" class="btn btn-sm btn-outline-secondary">درخواست تغییر فعال‌سازی (دو نفره)</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <input v-model="text" class="form-control form-control-sm mb-2 d-print-none" placeholder="جستجو در کد، عنوان، بند یا فرایند" />
      <table class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>عنوان</th><th>استاندارد و بند</th><th>رویه</th><th>روش بازبینی</th><th>فرایند</th><th>شدت</th><th>وضعیت</th><th>آخرین اجرا</th><th>نتیجه</th><th>یافته‌ی باز</th></tr></thead>
        <tbody>
          <tr v-for="r in shown" :key="r.code">
            <td class="inv-ltr">{{ r.code }}</td>
            <td>{{ r.title }}</td>
            <td class="inv-ltr small">{{ r.standard }} {{ r.clause }}</td>
            <td class="small">{{ r.procedure }}</td>
            <td class="small">{{ r.mode }}</td>
            <td>{{ r.processTitle }}</td>
            <td><span class="inv-badge" :class="severityClass[r.severityValue]">{{ r.severity }}</span></td>
            <td><span class="inv-badge" :class="r.enabled ? 'ok' : 'bad'">{{ r.enabled ? "فعال" : "غیرفعال" }}</span></td>
            <td>{{ r.lastRunAtText }}</td>
            <td class="small">{{ r.lastStatus }} ({{ r.lastHits }}، {{ r.lastDurationMs }} ms)</td>
            <td>{{ r.openFindings }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
