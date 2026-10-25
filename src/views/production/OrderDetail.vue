<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import ComponentsEditor from "@/components/production/orders/ComponentsEditor.vue";
import JobCardPrint from "@/components/production/orders/JobCardPrint.vue";
import OperationsTable from "@/components/production/orders/OperationsTable.vue";
import ReasonDialog from "@/components/production/orders/ReasonDialog.vue";
import ReleasePanel from "@/components/production/orders/ReleasePanel.vue";
import { getProductionOrder, orderAction } from "@/api/ProductionAPIs";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { badgeOfOrder, orderStatuses, persianOf, productionActionTitles, qtyText } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";

// One production order (MfOrder): header, release gate (draft), components, operations with their actions, order actions, links to the documents the order made, job card print.
const route = useRoute();
const order = ref<any>(null);
const loading = ref(false);
const dialog = ref<null | "cancel" | "kit-override">(null);
const printing = ref<any>(null);
const busy = ref(false);
const id = computed(() => Number(route.params.id));
const isPlanner = hasRole(ProductionRoles.planners);
const isSupervisors = hasRole(ProductionRoles.supervisors);

const status = computed(() => order.value?.status);
// a released order that already has work started cannot be cancelled
const anyStarted = computed(() => (order.value?.operations || []).some((o: any) => o.startedAtText));
const canCancel = computed(() => (status.value === 0 && isPlanner) || (status.value === 1 && isSupervisors && !anyStarted.value));
const canClose = computed(() => status.value === 3 && isSupervisors);
const canKitOverride = computed(() => (status.value === 1 || status.value === 2) && isSupervisors && !order.value?.kitIssued && !order.value?.kitOverrideReason);
const canRefreshRouting = computed(() => status.value === 0 && isPlanner);
const canRefreshReadiness = computed(() => (status.value === 1 || status.value === 2) && isSupervisors);
const purchaseRequests = computed(() => uniqueBy((order.value?.operations || []).filter((o: any) => o.purchaseRequestId), "purchaseRequestId"));
const subcontractIssues = computed(() => uniqueBy((order.value?.operations || []).filter((o: any) => o.subcontractIssueId), "subcontractIssueId"));

function uniqueBy(rows: any[], key: string) {
  const seen = new Set();
  return rows.filter((r) => !seen.has(r[key]) && seen.add(r[key]));
}

async function load() {
  loading.value = true;
  const [d, error] = await getProductionOrder(id.value);
  loading.value = false;
  if (error) return fail(error);
  order.value = camelize(d);
}
function replace(next: any) {
  if (next && next.id) order.value = next;
  else load();
}

async function run(action: "close" | "kit-override" | "refresh-readiness" | "refresh-routing" | "cancel", reason: string | null = null) {
  if (action === "close" && !confirm("دستور تولید بسته شود؟")) return;
  busy.value = true;
  const [d, error] = await orderAction({ id: id.value, action, reason });
  busy.value = false;
  if (error) return fail(error);
  ok(productionActionTitles[action] + " انجام شد.");
  dialog.value = null;
  replace(camelize(d));
}

watch(id, load);
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div v-if="!order" class="inv-card"><p class="inv-muted mb-0">{{ loading ? "در حال بارگذاری..." : "دستور تولید پیدا نشد." }}</p></div>
    <template v-else>
      <div class="inv-card screen-only">
        <div class="inv-toolbar">
          <h1 class="inv-title">دستور تولید <span class="inv-ltr">{{ order.number }}</span>
            <span :class="badgeOfOrder(order.status)" class="inv-badge ms-2">{{ orderStatuses[order.status] }}</span>
            <span v-if="order.isLate" class="inv-badge bad ms-1">{{ order.daysLate }} روز دیرکرد</span>
          </h1>
          <div class="d-flex flex-wrap gap-2">
            <button v-if="canRefreshRouting" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="run('refresh-routing')">به‌روزرسانی مسیر ساخت</button>
            <button v-if="canRefreshReadiness" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="run('refresh-readiness')">بررسی دوباره آمادگی مراحل</button>
            <button v-if="canKitOverride" :disabled="busy" class="btn btn-sm btn-outline-warning" @click="dialog = 'kit-override'">شروع بدون کیت کامل</button>
            <button v-if="canClose" :disabled="busy" class="btn btn-sm btn-success" @click="run('close')">بستن دستور</button>
            <button v-if="canCancel" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="dialog = 'cancel'">لغو دستور</button>
            <router-link :to="{ name: 'MfOrders' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          </div>
        </div>
        <div class="row g-2 small">
          <div class="col-md-4"><b>کالا:</b> <span class="inv-ltr">{{ order.itemCode }}</span> {{ order.itemTitle }}</div>
          <div class="col-md-2"><b>تعداد:</b> {{ qtyText(order.quantity) }} {{ order.unit }}</div>
          <div class="col-md-3"><b>دستور کار:</b> <span class="inv-ltr">{{ order.workOrderCode }}</span></div>
          <div class="col-md-3"><b>اولویت:</b> {{ order.priority ?? "-" }}<span v-if="order.totalFloatDays !== null && order.totalFloatDays !== undefined"> (شناوری {{ order.totalFloatDays }} روز)</span></div>
          <div class="col-md-3"><b>تاریخ نیاز:</b> {{ persianOf(order.needDateText, order.needDate) || "-" }}</div>
          <div class="col-md-3"><b>شروع برنامه:</b> {{ persianOf(order.plannedStartText, order.plannedStart) || "-" }}</div>
          <div class="col-md-3"><b>پایان برنامه:</b> {{ persianOf(order.plannedFinishText, order.plannedFinish) || "-" }}</div>
          <div class="col-md-3"><b>پیشرفت مراحل:</b> {{ order.operationsDone }} از {{ order.operationCount }}</div>
          <div v-if="order.actualStartText" class="col-md-3"><b>شروع واقعی:</b> {{ order.actualStartText }}</div>
          <div v-if="order.actualFinishText" class="col-md-3"><b>پایان واقعی:</b> {{ order.actualFinishText }}</div>
          <div v-if="order.releasedAtText" class="col-md-3"><b>صدور:</b> {{ order.releasedByName }} - {{ order.releasedAtText }}</div>
          <div v-if="order.scheduleNote" class="col-12"><b>یادداشت زمان‌بندی:</b> {{ order.scheduleNote }}<span v-if="order.scheduledAtText" class="inv-muted"> ({{ order.scheduledAtText }})</span></div>
          <div v-if="order.releaseOverride" class="col-12"><span class="inv-badge warn">صدور با کمبود</span> {{ order.releaseOverrideReason }} <span v-if="order.shortageText" class="inv-muted">- {{ order.shortageText }}</span></div>
          <div v-if="order.kitOverrideReason" class="col-12"><span class="inv-badge warn">شروع بدون کیت کامل</span> {{ order.kitOverrideReason }}</div>
          <div v-if="order.cancelReason" class="col-12"><span class="inv-badge bad">لغو</span> {{ order.cancelReason }}</div>
          <div v-if="order.note" class="col-12"><b>توضیح:</b> {{ order.note }}</div>
        </div>
        <hr />
        <div class="row g-2 small">
          <div class="col-md-3">
            <b>درخواست کیت مواد:</b>
            <template v-if="order.kitRequestId"><router-link :to="{ name: 'InventoryMaterialRequestEdit', params: { id: order.kitRequestId } }" class="inv-ltr">{{ order.kitNumber || "پیش‌نویس" }}</router-link> <span :class="order.kitIssued ? 'ok' : order.kitShortage ? 'warn' : 'info'" class="inv-badge">{{ order.kitIssued ? "تحویل شده" : order.kitShortage ? "کمبود" : "در انتظار تحویل" }}</span></template>
            <span v-else class="inv-muted">ساخته نشده</span>
          </div>
          <div class="col-md-3">
            <b>رسید تولید:</b>
            <router-link v-if="order.productionReceiptId" :to="{ name: 'InventoryDocumentEdit', params: { id: order.productionReceiptId } }" class="inv-ltr">{{ order.productionReceiptNumber || "پیش‌نویس" }}</router-link>
            <span v-else class="inv-muted">ساخته نشده</span>
          </div>
          <div class="col-md-3">
            <b>درخواست خرید (کار بیرونی):</b>
            <span v-if="!purchaseRequests.length" class="inv-muted">ندارد</span>
            <router-link v-for="o in purchaseRequests" :key="o.purchaseRequestId" :to="{ name: 'ProcurementRequestEdit', params: { id: o.purchaseRequestId } }" class="inv-ltr me-2">{{ o.purchaseRequestNumber || "پیش‌نویس" }}</router-link>
          </div>
          <div class="col-md-3">
            <b>حواله ارسال به پیمانکار:</b>
            <span v-if="!subcontractIssues.length" class="inv-muted">ندارد</span>
            <router-link v-for="o in subcontractIssues" :key="o.subcontractIssueId" :to="{ name: 'InventoryDocumentEdit', params: { id: o.subcontractIssueId } }" class="inv-ltr me-2">{{ o.subcontractIssueNumber || "پیش‌نویس" }}</router-link>
          </div>
        </div>
      </div>

      <ReleasePanel v-if="order.status === 0" class="screen-only" :order="order" @changed="replace" />
      <ComponentsEditor class="screen-only" :order="order" :can-edit="order.status === 0 && isPlanner" @changed="replace" />
      <OperationsTable class="screen-only" :order="order" @changed="load" @print="printing = $event" />
    </template>

    <ReasonDialog v-if="dialog === 'cancel'" :busy="busy" danger title="لغو دستور تولید" hint="دستور پیش‌نویس یا صادرشده‌ای که کاری روی آن شروع نشده لغو می‌شود." label="دلیل لغو" confirm-text="لغو دستور" @close="dialog = null" @confirm="(r) => run('cancel', r)" />
    <ReasonDialog v-if="dialog === 'kit-override'" :busy="busy" title="شروع بدون کیت کامل" hint="با این کار مراحل بدون انتظار برای تحویل کامل کیت آزاد می‌شوند؛ دلیل ثبت می‌شود." label="دلیل" confirm-text="ثبت" @close="dialog = null" @confirm="(r) => run('kit-override', r)" />
    <JobCardPrint v-if="order && printing" :order="order" :operation="printing" @close="printing = null" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
