<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import LookupBox from "@/components/fn/LookupBox.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import QPager from "@/components/quality/QPager.vue";
import ProposalEditor from "@/components/sales/pipeline/ProposalEditor.vue";
import WinLossTab from "@/components/sales/pipeline/WinLossTab.vue";
import { customerLabel, searchCustomers } from "@/components/sales/pipeline/pipelineKit";
import { getSlProposals } from "@/api/SalesAPIs";
import { usePagedList } from "@/composables/fn";
import { badgeOfProposal, persianOf, productTypes, proposalStatuses, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// Proposals (SlProposals / SlProposal): list, editor with approval / submit / revise / cancel / result, and the win / loss statistics.
const route = useRoute();
const router = useRouter();
const detailId = computed(() => (route.params.id ? Number(route.params.id) : null));
const tab = ref("list");
const tabs = [{ key: "list", title: "پیشنهادها" }, { key: "stats", title: "آمار برد و باخت" }];
const creating = ref(false);
const customer = ref<any>(null);
const filters = ref({ Status: null as number | null, CustomerId: null as number | null, Contains: "" });
const list = usePagedList(getSlProposals, filters);
const canWrite = hasRole(SalesRoles.writers);

function pick(c: any) {
  customer.value = c;
  filters.value.CustomerId = c ? c.id : null;
}
function changed(created: any) {
  list.load();
  if (created?.id) {
    creating.value = false;
    router.push({ name: "SlProposal", params: { id: created.id } });
  }
}
watch(detailId, (id) => {
  if (!id) list.load();
});
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <template v-if="detailId || creating">
        <div class="mb-2"><router-link :to="{ name: 'SlProposals' }" class="btn btn-sm btn-outline-secondary" @click="creating = false">بازگشت به فهرست</router-link></div>
        <ProposalEditor :id="detailId" :key="detailId || 'new'" @changed="changed" />
      </template>
      <template v-else>
        <div class="inv-toolbar">
          <h1 class="inv-title">پیشنهاد قیمت</h1>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="creating = true">پیشنهاد جدید</button>
        </div>
        <PcTabs v-model="tab" :tabs="tabs" />
        <template v-if="tab === 'list'">
          <div class="inv-filters mb-3">
            <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره پیشنهاد" @keyup.enter="list.search" />
            <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in proposalStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
            <LookupBox :model-value="customer" :fetch="searchCustomers" :label="customerLabel" placeholder="مشتری" @pick="pick" />
            <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
          </div>
          <div class="table-responsive">
            <table class="table table-sm table-hover">
              <thead><tr><th>شماره</th><th>بازنگری</th><th>استعلام</th><th>مشتری</th><th>محصول</th><th>قیمت (ریال)</th><th>ارسال</th><th>وضعیت</th><th></th></tr></thead>
              <tbody>
                <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">پیشنهادی پیدا نشد.</td></tr>
                <tr v-for="r in list.rows.value" :key="r.id">
                  <td class="inv-ltr"><router-link :to="{ name: 'SlProposal', params: { id: r.id } }">{{ r.number }}</router-link></td><td>{{ r.revision }}</td>
                  <td class="inv-ltr">{{ r.inquiryNumber }}</td><td>{{ r.customerName }}</td><td>{{ productTypes[r.productType] || "-" }}</td><td class="inv-ltr">{{ rialText(r.price) }}</td>
                  <td>{{ persianOf(r.submittedAtText, null) || "-" }}</td>
                  <td><span :class="badgeOfProposal(r.status)" class="inv-badge">{{ r.statusText || proposalStatuses[r.status] }}</span><span v-if="r.belowCost" class="inv-badge bad ms-1">زیر بهای تمام‌شده</span></td>
                  <td><router-link class="btn btn-sm btn-outline-primary" :to="{ name: 'SlProposal', params: { id: r.id } }">باز کردن</router-link></td>
                </tr>
              </tbody>
            </table>
          </div>
          <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
        </template>
        <WinLossTab v-else />
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
