<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import BidPanel from "@/components/sales/pipeline/BidPanel.vue";
import InquiryForm from "@/components/sales/pipeline/InquiryForm.vue";
import { attachmentList, openDoc } from "@/components/sales/pipeline/pipelineKit";
import { getSlEstimates, getSlInquiry, getSlProposals, inquiryAction } from "@/api/SalesAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfEstimate, badgeOfInquiry, badgeOfProposal, estimateStatuses, inquiryStatuses, persianOf, productTypes, proposalStatuses, qtyText, rialText } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";

// One inquiry (SlInquiry) and its bid / no-bid gate (SlBidDecision uses the same inquiry id).
const route = useRoute();
const id = computed(() => Number(route.params.id));
const d = ref<any>(null);
const editing = ref(false);
const busy = ref(false);
const cancelReason = ref("");
const cancelling = ref(false);
const estimates = ref<any[]>([]);
const proposals = ref<any[]>([]);
const canWrite = hasRole(SalesRoles.writers);
const canMoney = hasRole(SalesRoles.commercialReaders);

async function load() {
  const [x, error] = await getSlInquiry(id.value);
  if (error) return fail(error);
  d.value = x;
  if (canMoney) {
    const [e] = await getSlEstimates({ InquiryId: id.value, PageSize: 50 });
    estimates.value = rowsOf(camelize(e));
    const [p] = await getSlProposals({ InquiryId: id.value, PageSize: 50 });
    proposals.value = rowsOf(camelize(p));
  }
}
async function act(action: "review" | "cancel") {
  if (action === "cancel" && !cancelReason.value.trim()) return fail({ message: "دلیل لغو را بنویسید." });
  busy.value = true;
  const [x, error] = await inquiryAction({ id: id.value, action, reason: cancelReason.value, rowVersion: d.value.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok(action === "review" ? "بررسی استعلام شروع شد." : "استعلام لغو شد.");
  cancelling.value = false;
  d.value = x;
}
watch(id, load);
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="mb-2"><router-link :to="{ name: 'SlInquiries' }" class="btn btn-sm btn-outline-secondary">بازگشت به فهرست</router-link></div>
      <template v-if="d">
        <div class="inv-toolbar">
          <div>
            <h1 class="inv-title"><span class="inv-ltr">{{ d.number }}</span> - {{ d.title }}</h1>
            <span :class="badgeOfInquiry(d.status)" class="inv-badge">{{ d.statusText || inquiryStatuses[d.status] }}</span>
          </div>
          <div v-if="canWrite" class="d-flex gap-2">
            <button v-if="d.status === 1" :disabled="busy" class="btn btn-sm btn-primary" @click="act('review')">شروع بررسی</button>
            <button v-if="[1, 2, 4].includes(d.status)" class="btn btn-sm btn-outline-primary" @click="editing = true">ویرایش</button>
            <button v-if="[1, 2, 3, 4].includes(d.status)" class="btn btn-sm btn-outline-danger" @click="cancelling = !cancelling">لغو</button>
          </div>
        </div>
        <div v-if="cancelling" class="border rounded p-2 mb-3">
          <label class="form-label small mb-0">دلیل لغو</label>
          <textarea v-model="cancelReason" class="form-control form-control-sm mb-2" rows="2" />
          <button :disabled="busy" class="btn btn-sm btn-danger" @click="act('cancel')">تایید لغو</button>
        </div>
        <div class="row g-2 mb-2">
          <div class="col-md-4"><span class="text-muted small">مشتری</span><div>{{ d.customerName }}</div></div>
          <div class="col-md-4"><span class="text-muted small">تاریخ دریافت</span><div>{{ persianOf(d.receivedDateText, d.receivedDate) }}</div></div>
          <div class="col-md-4"><span class="text-muted small">کانال</span><div>{{ d.channel || "-" }}</div></div>
          <div class="col-md-4"><span class="text-muted small">نوع محصول و تعداد</span><div>{{ d.productTypeText || productTypes[d.productType] }} - {{ qtyText(d.quantity) }}</div></div>
          <div class="col-md-4"><span class="text-muted small">تحویل درخواستی</span><div>{{ persianOf(d.requiredDeliveryDateText, d.requiredDeliveryDate) || "-" }}</div></div>
          <div class="col-md-4"><span class="text-muted small">مهلت ارسال پیشنهاد</span><div>{{ persianOf(d.proposalDueDateText, d.proposalDueDate) || "-" }}</div></div>
          <div class="col-md-4"><span class="text-muted small">شماره ارجاع مشتری</span><div class="inv-ltr">{{ d.customerReference || "-" }}</div></div>
          <div v-if="d.tenderId" class="col-md-4"><span class="text-muted small">مناقصه</span><div><router-link :to="{ name: 'TenderUpdate', params: { id: d.tenderId } }">مشاهده مناقصه</router-link></div></div>
          <div v-if="d.cancelReason" class="col-12"><span class="text-muted small">دلیل لغو</span><div>{{ d.cancelReason }}</div></div>
          <div v-if="d.note" class="col-12"><span class="text-muted small">یادداشت</span><div>{{ d.note }}</div></div>
          <div v-if="attachmentList(d.attachmentIds).length" class="col-12">
            <span class="text-muted small">پیوست‌ها</span>
            <div class="d-flex gap-2 flex-wrap"><a v-for="(a, i) in attachmentList(d.attachmentIds)" :key="a" href="#" @click.prevent="openDoc(a, 'attachment-' + (i + 1))">پیوست {{ i + 1 }}</a></div>
          </div>
        </div>

        <BidPanel :inquiry-id="id" :inquiry-status="d.status" @changed="load" />

        <div v-if="canMoney" class="row g-3 mt-1">
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">برآوردها</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>شماره</th><th>بازنگری</th><th>بهای تمام‌شده</th><th>قیمت</th><th>وضعیت</th></tr></thead>
              <tbody>
                <tr v-if="!estimates.length"><td colspan="5" class="text-center inv-muted">برآوردی ثبت نشده است.</td></tr>
                <tr v-for="e in estimates" :key="e.id">
                  <td class="inv-ltr"><router-link :to="{ name: 'SlEstimate', params: { id: e.id } }">{{ e.number }}</router-link></td><td>{{ e.revision }}</td><td>{{ rialText(e.totalCost) }}</td><td>{{ rialText(e.price) }}</td>
                  <td><span :class="badgeOfEstimate(e.status)" class="inv-badge">{{ estimateStatuses[e.status] }}</span></td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="col-lg-6">
            <h2 class="inv-title mb-2">پیشنهادهای قیمت</h2>
            <table class="table table-sm table-hover">
              <thead><tr><th>شماره</th><th>بازنگری</th><th>قیمت</th><th>وضعیت</th></tr></thead>
              <tbody>
                <tr v-if="!proposals.length"><td colspan="4" class="text-center inv-muted">پیشنهادی ثبت نشده است.</td></tr>
                <tr v-for="p in proposals" :key="p.id">
                  <td class="inv-ltr"><router-link :to="{ name: 'SlProposal', params: { id: p.id } }">{{ p.number }}</router-link></td><td>{{ p.revision }}</td><td>{{ rialText(p.price) }}</td>
                  <td><span :class="badgeOfProposal(p.status)" class="inv-badge">{{ proposalStatuses[p.status] }}</span></td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>
      <p v-else class="inv-muted">در حال بارگذاری...</p>
    </div>
    <InquiryForm v-if="editing && d" :row="d" @close="editing = false" @saved="(x: any) => { editing = false; d = x; }" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
