<script setup lang="ts">
import { computed, ref } from "vue";
import CompleteOutsideDialog from "./CompleteOutsideDialog.vue";
import QcDecisionDialog from "./QcDecisionDialog.vue";
import ReasonDialog from "./ReasonDialog.vue";
import ReworkDialog from "./ReworkDialog.vue";
import { operationAction } from "@/api/ProductionAPIs";
import { fail, ok } from "@/composables/inventory";
import { badgeOfOperation, hoursText, inspectionRequirements, operationStatuses, persianOf, qtyText } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";

// Operations of the order with the actions the services accept: skip (shop supervisor, reason), complete-outside and rework (supervisors), QC decision (QC), print of the job card.
const props = defineProps<{ order: any }>();
const emit = defineEmits<{ (e: "changed"): void; (e: "print", operation: any): void }>();
const skipping = ref<any>(null);
const outside = ref<any>(null);
const deciding = ref<any>(null);
const reworking = ref<any>(null);
const busy = ref(false);
const running = computed(() => props.order.status === 1 || props.order.status === 2);
const isSupervisor = hasRole(ProductionRoles.supervisor);
const isSupervisors = hasRole(ProductionRoles.supervisors);
const isInspector = hasRole(ProductionRoles.inspectors);

// a rejected step already has an open rework operation: no second rework button
const hasOpenRework = (o: any) => (props.order.operations || []).some((x: any) => x.isRework && x.reworkOfOperationId === o.id && x.status !== 6 && x.status !== 7);

async function skip(reason: string) {
  busy.value = true;
  const [, error] = await operationAction({ id: skipping.value.id, action: "skip", reason });
  busy.value = false;
  if (error) return fail(error);
  ok("مرحله رد شد.");
  skipping.value = null;
  emit("changed");
}
function done() {
  outside.value = deciding.value = reworking.value = null;
  emit("changed");
}
</script>

<template>
  <div class="inv-card">
    <div class="inv-toolbar"><h2 class="inv-title">مراحل ساخت</h2></div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>ردیف</th><th>مرحله</th><th>مرکز کار</th><th>ساعت برنامه</th><th>شروع برنامه</th><th>پایان برنامه</th><th>وضعیت</th><th>سالم</th><th>ضایعات</th><th>دوباره‌کاری</th><th>ساعت ثبت‌شده</th><th>بازرسی و ارجاع‌ها</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!order.operations?.length"><td colspan="13" class="text-center inv-muted">مرحله‌ای برای این دستور تعریف نشده است.</td></tr>
          <tr v-for="o in order.operations" :key="o.id">
            <td>{{ o.sequence }}</td>
            <td>{{ o.title }}
              <span v-if="o.isRework" class="inv-badge warn ms-1">دوباره‌کاری</span><span v-if="o.isOutside" class="inv-badge info ms-1">بیرونی</span>
              <div v-if="o.skipReason" class="inv-muted">دلیل رد: {{ o.skipReason }}</div>
            </td>
            <td class="inv-ltr">{{ o.workCenterCode || "-" }}</td>
            <td>{{ hoursText((o.setupHoursPlanned || 0) + (o.runHoursPlanned || 0)) }}</td>
            <td>{{ persianOf(o.plannedStartText, o.plannedStart) || "-" }}</td><td>{{ persianOf(o.plannedFinishText, o.plannedFinish) || "-" }}</td>
            <td><span :class="badgeOfOperation(o.status)" class="inv-badge">{{ operationStatuses[o.status] }}</span></td>
            <td>{{ qtyText(o.goodQty) }}</td><td>{{ qtyText(o.scrapQty) }}</td><td>{{ qtyText(o.reworkQty) }}</td><td>{{ hoursText(o.hoursBooked) }}</td>
            <td class="small">
              <div v-if="o.inspectionRequirement">{{ inspectionRequirements[o.inspectionRequirement] }}<span v-if="o.qcAccepted" class="inv-badge ok ms-1">پذیرفته</span></div>
              <div v-if="o.qcRecordId">بازرسی: <router-link :to="{ name: 'QualityRecordEdit', params: { id: o.qcRecordId } }" class="inv-ltr">{{ o.qcRecordNumber || "مشاهده" }}</router-link></div>
              <div v-if="o.ncrId">عدم انطباق: <router-link :to="{ name: 'InventoryNcrEdit', params: { id: o.ncrId } }" class="inv-ltr">{{ o.ncrNumber || "مشاهده" }}</router-link></div>
              <div v-if="o.purchaseRequestId">درخواست خرید: <router-link :to="{ name: 'ProcurementRequestEdit', params: { id: o.purchaseRequestId } }" class="inv-ltr">{{ o.purchaseRequestNumber || "پیش‌نویس" }}</router-link></div>
              <div v-if="o.subcontractIssueId">حواله پیمانکار: <router-link :to="{ name: 'InventoryDocumentEdit', params: { id: o.subcontractIssueId } }" class="inv-ltr">{{ o.subcontractIssueNumber || "پیش‌نویس" }}</router-link></div>
            </td>
            <td class="text-nowrap">
              <button class="btn btn-sm btn-outline-secondary me-1 mb-1" @click="emit('print', o)">برگه کار</button>
              <template v-if="running">
                <button v-if="isSupervisor && (o.status === 0 || o.status === 1)" class="btn btn-sm btn-outline-danger me-1 mb-1" @click="skipping = o">رد کردن</button>
                <button v-if="isSupervisors && o.isOutside && (o.status === 1 || o.status === 2)" class="btn btn-sm btn-outline-primary me-1 mb-1" @click="outside = o">پایان کار بیرونی</button>
                <button v-if="isInspector && o.status === 5" class="btn btn-sm btn-primary me-1 mb-1" @click="deciding = o">تصمیم بازرسی</button>
                <button v-if="isSupervisors && o.status === 6 && !o.isRework && !hasOpenRework(o)" class="btn btn-sm btn-outline-warning me-1 mb-1" @click="reworking = o">دوباره‌کاری</button>
              </template>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <ReasonDialog v-if="skipping" :busy="busy" danger title="رد کردن مرحله" :hint="`مرحله ${skipping.sequence} - ${skipping.title} اجرا نمی‌شود و مرحله بعد آزاد می‌شود.`" label="دلیل رد کردن" confirm-text="رد کردن مرحله" @close="skipping = null" @confirm="skip" />
    <CompleteOutsideDialog v-if="outside" :operation="outside" :work-order-id="order.workOrderId" @close="outside = null" @done="done" />
    <QcDecisionDialog v-if="deciding" :operation="deciding" :quantity="order.quantity" :work-order-id="order.workOrderId" @close="deciding = null" @done="done" />
    <ReworkDialog v-if="reworking" :operation="reworking" :work-order-code="order.workOrderCode" :item-id="order.itemId" @close="reworking = null" @done="done" />
  </div>
</template>
