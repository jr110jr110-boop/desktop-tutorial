<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import EstimateCreateDialog from "@/components/sales/pipeline/EstimateCreateDialog.vue";
import { inquiryLabel, inquirySearch } from "@/components/sales/pipeline/pipelineKit";
import { getSlEstimates } from "@/api/SalesAPIs";
import { usePagedList } from "@/composables/fn";
import { badgeOfEstimate, estimateStatuses, persianOf, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// Cost estimates (SlEstimates): list and creation from equipment templates.
const router = useRouter();
const inquiry = ref<any>(null);
const filters = ref({ Status: null as number | null, InquiryId: null as number | null, Contains: "" });
const list = usePagedList(getSlEstimates, filters);
const creating = ref(false);
const canWrite = hasRole(SalesRoles.writers);
const searchInquiries = inquirySearch();

function pick(i: any) {
  inquiry.value = i;
  filters.value.InquiryId = i ? i.id : null;
}
function created(d: any) {
  creating.value = false;
  router.push({ name: "SlEstimate", params: { id: d.id } });
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">برآورد هزینه</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="creating = true">برآورد جدید</button>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره برآورد" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in estimateStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <LookupBox :model-value="inquiry" :fetch="searchInquiries" :label="inquiryLabel" placeholder="استعلام" @pick="pick" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>استعلام</th><th>بازنگری</th><th>بهای تمام‌شده</th><th>قیمت</th><th>ورودی‌های ناقص</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">برآوردی پیدا نشد.</td></tr>
            <tr v-for="r in list.rows.value" :key="r.id">
              <td class="inv-ltr"><router-link :to="{ name: 'SlEstimate', params: { id: r.id } }">{{ r.number }}</router-link></td>
              <td class="inv-ltr"><router-link :to="{ name: 'SlInquiry', params: { id: r.inquiryId } }">{{ r.inquiryNumber }}</router-link></td>
              <td>{{ r.revision }}</td><td>{{ rialText(r.totalCost) }}</td><td>{{ rialText(r.price) }}</td>
              <td class="small text-warning">{{ r.missingInputs || "" }}</td>
              <td><span :class="badgeOfEstimate(r.status)" class="inv-badge">{{ estimateStatuses[r.status] }}</span><span v-if="r.finalizedAtText" class="small text-muted ms-1">{{ persianOf(r.finalizedAtText, null) }}</span></td>
              <td><router-link class="btn btn-sm btn-outline-primary" :to="{ name: 'SlEstimate', params: { id: r.id } }">باز کردن</router-link></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
    <EstimateCreateDialog v-if="creating" @close="creating = false" @created="created" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
