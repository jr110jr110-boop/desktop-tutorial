<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import InquiryForm from "@/components/sales/pipeline/InquiryForm.vue";
import { customerLabel, searchCustomers } from "@/components/sales/pipeline/pipelineKit";
import { getSlBidDecisions, getSlInquiries } from "@/api/SalesAPIs";
import { usePagedList } from "@/composables/fn";
import { badgeOfBid, badgeOfInquiry, bidStatuses, inquiryStatuses, persianOf, productTypes, qtyText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// Inquiry register (SlInquiries) and the list of bid / no-bid checklists (SlBidDecisions, same page, second tab).
const route = useRoute();
const router = useRouter();
const tab = ref(route.name === "SlBidDecisions" ? "bids" : "inquiries");
const tabs = [{ key: "inquiries", title: "ثبت استعلام" }, { key: "bids", title: "تصمیم شرکت در مناقصه" }];
const customer = ref<any>(null);
const filters = ref({ Status: null as number | null, CustomerId: null as number | null, ProductType: null as number | null, Contains: "" });
const bidFilters = ref({ Status: 2 as number | null });
const list = usePagedList(getSlInquiries, filters);
const bids = usePagedList(getSlBidDecisions, bidFilters);
const creating = ref(false);
const canWrite = hasRole(SalesRoles.writers);
const rows = computed(() => list.rows.value);
const today = new Date();

function pick(c: any) {
  customer.value = c;
  filters.value.CustomerId = c ? c.id : null;
}
function saved(d: any) {
  creating.value = false;
  if (d?.id) router.push({ name: "SlInquiry", params: { id: d.id } });
  else list.load();
}
const overdue = (r: any) => r.proposalDueDate && new Date(r.proposalDueDate) < today && [1, 2, 4].includes(r.status);
onMounted(() => {
  list.load();
  bids.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">استعلام‌ها</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="creating = true">استعلام جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />
      <template v-if="tab === 'inquiries'">
        <div class="inv-filters mb-3">
          <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا عنوان" @keyup.enter="list.search" />
          <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in inquiryStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <select v-model="filters.ProductType" class="form-select form-select-sm"><option :value="null">همه محصولات</option><option v-for="(t, k) in productTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <LookupBox :model-value="customer" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" @pick="pick" />
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>دریافت</th><th>مشتری</th><th>عنوان</th><th>محصول</th><th>تعداد</th><th>تحویل درخواستی</th><th>مهلت پیشنهاد</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!rows.length"><td colspan="10" class="text-center inv-muted">استعلامی پیدا نشد.</td></tr>
              <tr v-for="r in rows" :key="r.id">
                <td class="inv-ltr"><router-link :to="{ name: 'SlInquiry', params: { id: r.id } }">{{ r.number }}</router-link></td>
                <td>{{ persianOf(r.receivedDateText, r.receivedDate) }}</td><td>{{ r.customerName }}</td><td>{{ r.title }}</td>
                <td>{{ r.productTypeText || productTypes[r.productType] }}</td><td>{{ qtyText(r.quantity) }}</td>
                <td>{{ persianOf(r.requiredDeliveryDateText, r.requiredDeliveryDate) || "-" }}</td>
                <td :class="{ 'text-danger': overdue(r) }">{{ persianOf(r.proposalDueDateText, r.proposalDueDate) || "-" }}</td>
                <td><span :class="badgeOfInquiry(r.status)" class="inv-badge">{{ r.statusText || inquiryStatuses[r.status] }}</span></td>
                <td><router-link class="btn btn-sm btn-outline-primary" :to="{ name: 'SlInquiry', params: { id: r.id } }">مشاهده</router-link></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      </template>
      <template v-else>
        <div class="inv-filters mb-3">
          <select v-model="bidFilters.Status" class="form-select form-select-sm" @change="bids.search"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in bidStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <button class="btn btn-sm btn-secondary" @click="bids.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>استعلام</th><th>مشتری</th><th>عنوان</th><th>هفته‌های پرکار</th><th>حاشیه مورد انتظار</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!bids.rows.value.length"><td colspan="7" class="text-center inv-muted">چک‌لیستی پیدا نشد.</td></tr>
              <tr v-for="b in bids.rows.value" :key="b.id">
                <td class="inv-ltr">{{ b.inquiryNumber }}</td><td>{{ b.customerName }}</td><td>{{ b.inquiryTitle }}</td>
                <td>{{ b.capacityOverloadWeeks ?? "-" }}</td><td>{{ b.expectedMarginPercent ?? "-" }}</td>
                <td><span :class="badgeOfBid(b.status)" class="inv-badge">{{ bidStatuses[b.status] }}</span></td>
                <td><router-link class="btn btn-sm btn-outline-primary" :to="{ name: 'SlBidDecision', params: { id: b.inquiryId } }">باز کردن</router-link></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="bids.page.value" :total="bids.total.value" @go="bids.go" />
      </template>
    </div>
    <InquiryForm v-if="creating" @close="creating = false" @saved="saved" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
