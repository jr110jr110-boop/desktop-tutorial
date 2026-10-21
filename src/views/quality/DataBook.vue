<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import { getDataBook } from "@/api/QualityAPIs";
import { downloadFile } from "@/api/InventoryAPIs";
import { fail, printPage } from "@/composables/inventory";

// Final data book index of a work order, computed from the records. "company" = every entry, "client" = only what the client receives.
const route = useRoute();
const workOrderId = Number(route.params.workOrderId);
const variant = ref<"company" | "client">("company");
const book = ref<any>(null);
const kindNames: Record<string, string> = {
  Itp: "برنامه بازرسی", ItpActivity: "فعالیت ITP", TestReport: "گزارش آزمون", QcRecord: "سابقه بازرسی", Ncr: "عدم انطباق", ReleaseNote: "برگه ترخیص",
  MaterialCertificate: "گواهی مواد", WeldLog: "گزارش جوشکاری", Wps: "WPS", Pqr: "PQR", Qualification: "گواهی صلاحیت", Equipment: "گواهی کالیبراسیون",
};
const total = computed(() => (book.value?.sections || []).reduce((n: number, s: any) => n + s.entries.length, 0));

async function load() {
  const [data, error] = await getDataBook(workOrderId, variant.value);
  if (error) return fail(error);
  book.value = data;
}

const route2 = (e: any) => {
  if (!e.sourceId) return null;
  switch (e.sourceKind) {
    case "Itp": return { name: "QualityItpEdit", params: { id: e.sourceId } };
    case "TestReport": return { name: "QualityReportEdit", params: { id: e.sourceId } };
    case "QcRecord": return { name: "QualityRecordEdit", params: { id: e.sourceId } };
    case "Ncr": return { name: "InventoryNcrEdit", params: { id: e.sourceId } };
    case "ReleaseNote": return { name: "QualityReleaseNoteEdit", params: { id: e.sourceId } };
    default: return null;
  }
};

onMounted(load);
</script>

<template>
  <div v-if="book" class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">دیتابوک نهایی — دستور ساخت <span class="inv-ltr">{{ book.workOrderCode }}</span></h1>
          <span :class="book.missingCount ? 'bad' : 'ok'" class="inv-badge">{{ book.missingCount ? book.missingCount + " مدرک الزامی ناقص" : "کامل" }}</span>
          <span class="inv-muted ms-2">{{ total }} ردیف — فهرست از رکوردها محاسبه می‌شود</span>
        </div>
        <div class="d-flex flex-wrap gap-2 d-print-none">
          <div class="btn-group btn-group-sm">
            <button :class="variant === 'company' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="(variant = 'company'), load()">نسخه شرکت</button>
            <button :class="variant === 'client' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="(variant = 'client'), load()">نسخه کارفرما</button>
          </div>
          <router-link :to="{ name: 'QualityReleaseNotes', query: { workOrderId } }" class="btn btn-sm btn-outline-secondary">برگه‌های ترخیص</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
        </div>
      </div>
      <div v-for="s in book.sections" :key="s.no" class="mb-3">
        <h2 class="inv-title mb-1">{{ s.no }}. {{ s.name }}</h2>
        <table class="table table-sm">
          <thead><tr><th style="width: 160px">نوع</th><th>شماره</th><th>عنوان</th><th>وضعیت</th><th>الزامی</th><th>فایل</th></tr></thead>
          <tbody>
            <tr v-if="!s.entries.length"><td colspan="6" class="inv-muted text-center">موردی نیست.</td></tr>
            <tr v-for="(e, i) in s.entries" :key="i" :class="{ 'table-danger': e.missing }">
              <td>{{ kindNames[e.sourceKind] || e.sourceKind }}</td>
              <td class="inv-ltr"><router-link v-if="route2(e)" :to="route2(e)">{{ e.number }}</router-link><span v-else>{{ e.number }}</span></td>
              <td>{{ e.title }}</td><td>{{ e.status }}</td>
              <td>{{ e.mandatory ? "بله" : "" }} <span v-if="e.missing" class="inv-badge bad">ناقص</span></td>
              <td><button v-if="e.documentId" class="btn btn-sm btn-link p-0" @click="downloadFile(e.documentId, e.number)">دریافت</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
