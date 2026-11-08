<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { getDesignDossier } from "@/api/DesignControlAPIs";
import { fail, jalali, printPage } from "@/composables/inventory";
import { inputKinds, inputStatusClass, inputStatuses, outcomeClass, planStatusClass, planStatuses, reviewOutcomes, reviewStatusClass, reviewStatuses, reviewTypes, revisionText } from "@/composables/designControl";

// Package A1: the design dossier of a product: plans, live inputs, reviews and, for every BOM revision, whether the verification and the validation gates are open and what is missing.
const route = useRoute();
const dossier = ref<any>(null);

async function load() {
  const [data, error] = await getDesignDossier(Number(route.params.productId));
  if (error) return fail(error);
  dossier.value = data;
}
onMounted(load);
</script>

<template>
  <div v-if="dossier" class="inv-page ks-report">
    <KsReportHeader title="پرونده طراحی محصول" :subtitle="dossier.productCode + ' ' + dossier.productTitle" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">پرونده طراحی {{ dossier.productCode }} — {{ dossier.productTitle }}</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <router-link :to="{ name: 'DesignInputs', query: { productId: dossier.productId } }" class="btn btn-sm btn-outline-secondary">ورودی‌های طراحی</router-link>
          <router-link :to="{ name: 'ProductBankProduct', params: { id: dossier.productId } }" class="btn btn-sm btn-outline-secondary">بانک محصول</router-link>
        </div>
      </div>
      <p class="small">
        تایید طراحی پیش از انتشار: <b>{{ dossier.verificationRequired ? "الزامی" : "غیرفعال (تنظیمات)" }}</b> · صحه‌گذاری در ممیزی پیکربندی: <b>{{ dossier.validationRequired ? "الزامی" : "غیرفعال (تنظیمات)" }}</b>
        · یافته باز: <b :class="{ 'text-danger': dossier.openFindings }">{{ dossier.openFindings }}</b> · ورودی در تعارض: <b :class="{ 'text-danger': dossier.conflictCount }">{{ dossier.conflictCount }}</b>
      </p>

      <h2 class="h6">دروازه‌های انتشار BOM</h2>
      <table class="table table-sm">
        <thead><tr><th>BOM</th><th>بازنگری</th><th>وضعیت BOM</th><th>تایید طراحی (Verification)</th><th>صحه‌گذاری (Validation)</th></tr></thead>
        <tbody>
          <tr v-if="!dossier.boms.length"><td colspan="5" class="text-center inv-muted">BOM ثبت نشده است.</td></tr>
          <tr v-for="b in dossier.boms" :key="b.bomHeaderId">
            <td class="inv-ltr"><router-link :to="{ name: 'ProductBankBomEdit', params: { id: b.bomHeaderId } }">{{ b.code }}</router-link></td><td class="inv-ltr">{{ b.revisionText }}</td><td>{{ b.status }}</td>
            <td>
              <span class="inv-badge" :class="b.verification.passed ? 'ok' : 'bad'">{{ b.verification.passed ? "باز" : "بسته" }}</span>
              <ul v-if="!b.verification.passed" class="small mb-0"><li v-for="(m, i) in b.verification.missing" :key="i">{{ m }}</li></ul>
            </td>
            <td>
              <span class="inv-badge" :class="b.validation.passed ? 'ok' : 'warn'">{{ b.validation.passed ? "پذیرفته" : "ندارد" }}</span>
              <ul v-if="!b.validation.passed" class="small mb-0"><li v-for="(m, i) in b.validation.missing" :key="i">{{ m }}</li></ul>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 class="h6">برنامه‌های طراحی</h2>
      <table class="table table-sm">
        <thead><tr><th>شماره</th><th>بازنگری</th><th>نسخه</th><th>عنوان</th><th>وضعیت</th><th>تایید در</th></tr></thead>
        <tbody>
          <tr v-if="!dossier.plans.length"><td colspan="6" class="text-center inv-muted">برنامه طراحی ثبت نشده است.</td></tr>
          <tr v-for="p in dossier.plans" :key="p.id">
            <td class="inv-ltr"><router-link :to="{ name: 'DesignPlan', params: { id: p.id } }">{{ p.number }}</router-link></td><td class="inv-ltr">{{ revisionText(p.productRevision) }}</td><td>{{ p.version }}</td><td>{{ p.title }}</td>
            <td><span class="inv-badge" :class="planStatusClass[p.status]">{{ planStatuses[p.status] }}</span></td><td>{{ jalali(p.approvedAt) }}</td>
          </tr>
        </tbody>
      </table>

      <h2 class="h6">ورودی‌های طراحی معتبر</h2>
      <table class="table table-sm">
        <thead><tr><th>کد</th><th>بازنگری</th><th>نوع</th><th>الزام</th><th>منبع</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!dossier.inputs.length"><td colspan="6" class="text-center inv-muted">ورودی ثبت نشده است.</td></tr>
          <tr v-for="i in dossier.inputs" :key="i.id"><td class="inv-ltr">{{ i.code }}</td><td class="inv-ltr">{{ revisionText(i.productRevision) }}</td><td>{{ inputKinds[i.kind] }}</td><td>{{ i.statement }}</td><td>{{ i.source }}</td><td><span class="inv-badge" :class="inputStatusClass[i.status]">{{ inputStatuses[i.status] }}</span></td></tr>
        </tbody>
      </table>

      <h2 class="h6">بازبینی‌ها</h2>
      <table class="table table-sm">
        <thead><tr><th>شماره</th><th>نوع</th><th>بازنگری</th><th>عنوان</th><th>وضعیت</th><th>نتیجه</th><th>یافته باز</th><th>بسته شده در</th></tr></thead>
        <tbody>
          <tr v-if="!dossier.reviews.length"><td colspan="8" class="text-center inv-muted">بازبینی ثبت نشده است.</td></tr>
          <tr v-for="r in dossier.reviews" :key="r.id">
            <td class="inv-ltr"><router-link :to="{ name: 'DesignReview', params: { id: r.id } }">{{ r.number }}</router-link></td><td>{{ reviewTypes[r.type] }}</td><td class="inv-ltr">{{ revisionText(r.productRevision) }}</td><td>{{ r.title }}</td>
            <td><span class="inv-badge" :class="reviewStatusClass[r.status]">{{ reviewStatuses[r.status] }}</span></td>
            <td><span v-if="r.status === 2" class="inv-badge" :class="outcomeClass[r.outcome]">{{ reviewOutcomes[r.outcome] }}</span></td><td>{{ r.openFindings }}</td><td>{{ jalali(r.closedAt) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../../inventory/inventory.scss"></style>
