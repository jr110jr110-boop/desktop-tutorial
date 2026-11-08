<script setup lang="ts">
import { ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getHistory, rowsOf } from "@/api/EngChangeAPIs";
import { fail, jalali, printPage } from "@/composables/inventory";

// Change history of one part number or product: every request and order that touched it, with the effectivity.
const route = useRoute();
const part = ref<string>((route.query.partNumber as string) || "");
const rows = ref<any[]>([]);
const searched = ref(false);

async function load() {
  if (!part.value.trim()) return;
  const [data, error] = await getHistory(part.value.trim());
  if (error) return fail(error);
  rows.value = rowsOf(data);
  searched.value = true;
}
if (part.value) load();
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="تاریخچه تغییرات قطعه" :subtitle="part" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">تاریخچه تغییرات قطعه یا محصول</h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'EngChangeOrders' }" class="btn btn-sm btn-outline-secondary">دستورهای تغییر</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="part" class="form-control form-control-sm inv-ltr" placeholder="شماره قطعه یا محصول" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">نمایش</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>نوع</th><th>شماره</th><th>عنوان</th><th>وضعیت</th><th>محتوا</th><th>نقطه اجرا</th><th>تاریخ</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td class="text-center inv-muted" colspan="7">{{ searched ? "برای این قطعه تغییری ثبت نشده است." : "شماره قطعه را بنویسید." }}</td></tr>
          <tr v-for="r in rows" :key="r.kind + r.id">
            <td>{{ r.kind }}</td>
            <td class="inv-ltr"><router-link :to="r.kind === 'ECO' ? { name: 'EngChangeOrderEdit', params: { id: r.id } } : { name: 'EngChangeRequestEdit', params: { id: r.id } }">{{ r.number }}</router-link></td>
            <td>{{ r.title }}</td>
            <td>{{ r.statusText }}</td>
            <td>{{ r.detail }}</td>
            <td>{{ r.effectivity }}</td>
            <td>{{ jalali(r.at) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
