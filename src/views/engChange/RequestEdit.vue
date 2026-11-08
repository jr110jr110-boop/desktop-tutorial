<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import EcrPrint from "@/components/engChange/print/EcrPrint.vue";
import { getRequest, requestAction, saveOrder, saveRequest } from "@/api/EngChangeAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { ecClasses, ecUrgencies, statusClass, targetKinds } from "@/composables/engChange";

// One engineering change request: reason, class, urgency and the affected products, parts, BOM / OPC / drawing / routing revisions.
const route = useRoute();
const router = useRouter();
const isNew = computed(() => route.params.id === "new");
const busy = ref(false);
const req = ref<any>(null);
const form = ref<any>({ id: null, title: "", reason: "", description: "", classification: 1, urgency: 1, targets: [{ kind: 2, partNumber: "", note: "" }] });

async function load() {
  if (isNew.value) return;
  const [data, error] = await getRequest(Number(route.params.id));
  if (error) return fail(error);
  req.value = data;
  form.value = { id: data.id, title: data.title, reason: data.reason, description: data.description || "", classification: data.classification, urgency: data.urgency, targets: data.targets.map((t: any) => ({ ...t })) };
}
const editable = computed(() => isNew.value || !!req.value?.canEdit);
function addTarget() { form.value.targets.push({ kind: 2, partNumber: "", note: "" }); }
function removeTarget(i: number) { form.value.targets.splice(i, 1); }

async function save() {
  busy.value = true;
  const [data, error] = await saveRequest(form.value);
  busy.value = false;
  if (error) return fail(error);
  ok("درخواست ذخیره شد.");
  if (isNew.value) router.replace({ name: "EngChangeRequestEdit", params: { id: data.id } });
  else { req.value = data; load(); }
}
async function act(action: string, ask: string | null) {
  let note: string | null = null;
  if (ask) {
    note = window.prompt(ask);
    if (!note || !note.trim()) return;
  }
  busy.value = true;
  const [data, error] = await requestAction(req.value.id, action, note);
  busy.value = false;
  if (error) return fail(error);
  ok("انجام شد.");
  req.value = data;
  load();
}
async function makeOrder() {
  busy.value = true;
  const [data, error] = await saveOrder({ requestId: req.value.id, title: req.value.title });
  busy.value = false;
  if (error) return fail(error);
  router.push({ name: "EngChangeOrderEdit", params: { id: data.id } });
}
load();
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="req ? 'درخواست تغییر مهندسی ' + req.number : 'درخواست تغییر مهندسی جدید'" />
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">{{ req ? "درخواست تغییر " + req.number : "درخواست تغییر جدید" }}
          <span v-if="req" class="inv-badge ms-2" :class="statusClass[req.status]">{{ req.statusText }}</span></h1>
        <div class="d-flex gap-2 d-print-none">
          <router-link :to="{ name: 'EngChangeRequests' }" class="btn btn-sm btn-outline-secondary">بازگشت به فهرست</router-link>
          <button v-if="req" class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
        </div>
      </div>

      <div class="row g-2">
        <div class="col-md-8"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" :disabled="!editable" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">کلاس *</label>
          <select v-model="form.classification" class="form-select form-select-sm" :disabled="!editable"><option v-for="(t, k) in ecClasses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">فوریت *</label>
          <select v-model="form.urgency" class="form-select form-select-sm" :disabled="!editable"><option v-for="(t, k) in ecUrgencies" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-12"><label class="form-label small mb-0">دلیل تغییر * (دست‌کم ۱۰ نویسه)</label><textarea v-model="form.reason" rows="2" class="form-control form-control-sm" :disabled="!editable"></textarea></div>
        <div class="col-12"><label class="form-label small mb-0">شرح تغییر پیشنهادی</label><textarea v-model="form.description" rows="2" class="form-control form-control-sm" :disabled="!editable"></textarea></div>
      </div>

      <h2 class="h6 mt-3">موارد متاثر</h2>
      <table class="table table-sm">
        <thead><tr><th>نوع</th><th>شماره قطعه / شناسه</th><th>توضیح</th><th v-if="editable"></th></tr></thead>
        <tbody>
          <tr v-for="(t, i) in form.targets" :key="i">
            <td style="width: 190px">
              <select v-model="t.kind" class="form-select form-select-sm" :disabled="!editable"><option v-for="(n, k) in targetKinds" :key="k" :value="Number(k)">{{ n }}</option></select>
            </td>
            <td>
              <input v-if="t.kind === 2" v-model="t.partNumber" class="form-control form-control-sm inv-ltr" placeholder="مثلا 16.11.00.000.2" :disabled="!editable" />
              <input v-else-if="t.kind === 1" v-model.number="t.productId" class="form-control form-control-sm inv-ltr" placeholder="شناسه محصول بانک محصول" :disabled="!editable" />
              <input v-else-if="t.kind === 3" v-model.number="t.bomHeaderId" class="form-control form-control-sm inv-ltr" placeholder="شناسه BOM" :disabled="!editable" />
              <input v-else-if="t.kind === 4" v-model.number="t.opcRoutingId" class="form-control form-control-sm inv-ltr" placeholder="شناسه چارت OPC" :disabled="!editable" />
              <input v-else-if="t.kind === 5" v-model.number="t.deliverableId" class="form-control form-control-sm inv-ltr" placeholder="شناسه سند (نقشه)" :disabled="!editable" />
              <input v-else v-model.number="t.routingRevisionId" class="form-control form-control-sm inv-ltr" placeholder="شناسه بازنگری مسیر ساخت" :disabled="!editable" />
              <div v-if="t.reference" class="small inv-muted">{{ t.reference }}</div>
            </td>
            <td><input v-model="t.note" class="form-control form-control-sm" :disabled="!editable" /></td>
            <td v-if="editable"><button class="btn btn-sm btn-outline-danger py-0" @click="removeTarget(i)">حذف</button></td>
          </tr>
        </tbody>
      </table>
      <div class="d-flex gap-2 flex-wrap">
        <button v-if="editable" class="btn btn-sm btn-outline-secondary" @click="addTarget">+ مورد</button>
        <button v-if="editable" class="btn btn-sm btn-primary" :disabled="busy" @click="save">ذخیره</button>
        <template v-if="req">
          <button v-if="req.canSubmit" class="btn btn-sm btn-success" :disabled="busy" @click="act('submit', null)">ارسال برای تصمیم مهندسی</button>
          <button v-if="req.canDecide" class="btn btn-sm btn-success" :disabled="busy" @click="act('approve', null)">تایید درخواست</button>
          <button v-if="req.canDecide" class="btn btn-sm btn-danger" :disabled="busy" @click="act('reject', 'دلیل رد')">رد</button>
          <button v-if="req.canCancel" class="btn btn-sm btn-outline-danger" :disabled="busy" @click="act('cancel', 'دلیل لغو')">لغو</button>
          <button v-if="req.canCreateOrder" class="btn btn-sm btn-primary" :disabled="busy" @click="makeOrder">ساخت دستور تغییر (ECO)</button>
          <router-link v-if="req.orderId" :to="{ name: 'EngChangeOrderEdit', params: { id: req.orderId } }" class="btn btn-sm btn-outline-primary">دستور {{ req.orderNumber }}</router-link>
        </template>
      </div>
      <p v-if="req && req.decidedByName" class="small inv-muted mt-2 mb-0">تصمیم: {{ req.decidedByName }} — {{ jalali(req.decidedAt, true) }} — {{ req.decisionNote }}</p>
    </div>
    <EcrPrint v-if="req" :req="req" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
