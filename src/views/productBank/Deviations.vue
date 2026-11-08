<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ReadinessPanel from "@/components/opsData/ReadinessPanel.vue";
import { generateWbs, applyChange, approveChangeEngineering, approveChangePlanning, cancelChange, getBaseline, getProducts, instantiateProduct, rejectChange, saveChange, submitChange } from "@/api/ProductBankAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, changeKinds, changeStatuses, makeOrBuyShort, num, revisionText } from "@/composables/productBank";

// A project's copy of the product bank (BOM, routings, WBS) and its deviations. A deviation is approved by engineering and planning, two different people,
// neither the one who asked. After the PC baseline of the contract is current, it is applied only with an approved change request of project control.
const route = useRoute();
const workOrderId = ref<number | null>(route.query.workOrderId ? Number(route.query.workOrderId) : null);
const baseline = ref<any>(null);
const products = ref<any[]>([]);
const productId = ref<number | null>(null);
const form = ref<any>(null);
const busy = ref(false);
const loaded = ref(false);
const crIds = ref<Record<number, number | null>>({});
const canRequest = hasRole(PbRoles.requesters);
const tab = ref<"bom" | "changes" | "wbs" | "opc">("bom");
const lines = computed(() => (baseline.value?.lines || []) as any[]);
const parts = computed(() => lines.value.filter((l) => !l.isHeading && !l.isRemoved));

async function load() {
  if (!workOrderId.value) return;
  const [p] = await getProducts();
  products.value = p || [];
  const [data, error] = await getBaseline(workOrderId.value);
  loaded.value = true;
  baseline.value = error ? null : data;
}
const takeCr = ref<number | null>(null);
async function genWbs() {
  busy.value = true;
  const [data, error] = await generateWbs(workOrderId.value!, takeCr.value);
  busy.value = false;
  if (error) return fail(error);
  baseline.value = data;
  if (data.wbsNote) fail(data.wbsNote);
  else ok("WBS کنترل پروژه ساخته شد.");
}
async function take() {
  busy.value = true;
  const [data, error] = await instantiateProduct(workOrderId.value!, productId.value!, takeCr.value);
  busy.value = false;
  if (error) return fail(error);
  ok("محصول از بانک گرفته شد و به‌عنوان مبنای پروژه کپی شد.");
  baseline.value = data;
}
function newChange(kind: number, line?: any) {
  form.value = { baselineId: baseline.value.id, kind, targetLineId: line?.id ?? null, partNumber: "", partName: line?.partName ?? "", newQty: kind === 4 ? line?.qty : 1, unit: line?.unit ?? "عدد", material: "", makeOrBuy: 1, reason: "", target: line };
}
async function run(fn: () => Promise<any>, message: string) {
  busy.value = true;
  const [, error] = await fn();
  busy.value = false;
  if (error) return fail(error);
  ok(message);
  load();
}
async function sendForm() {
  const { target, ...dto } = form.value;
  busy.value = true;
  const [saved, error] = await saveChange(dto);
  busy.value = false;
  if (error) return fail(error);
  form.value = null;
  ok("انحراف ثبت شد (پیش‌نویس).");
  await submitChange(saved.id);
  load();
}
const reject = (c: any) => {
  const note = window.prompt("دلیل رد");
  if (note && note.trim()) run(() => rejectChange(c.id, note), "انحراف رد شد.");
};
const lineOf = (id: number | null) => lines.value.find((l) => l.id === id);
if (workOrderId.value) load();
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="مبنای BOM پروژه و انحراف‌ها" :subtitle="baseline ? baseline.productCode : ''" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">مبنای BOM پروژه و انحراف‌ها</h1>
        <div class="d-flex gap-2 d-print-none"><button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button></div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model.number="workOrderId" type="number" class="form-control form-control-sm" style="max-width: 200px" placeholder="شناسهٔ دستور کار (Work Order)" @keyup.enter="load" />
        <button class="btn btn-sm btn-outline-secondary" @click="load">نمایش</button>
      </div>

      <div v-if="loaded && !baseline" class="border rounded p-3">
        <p>برای این دستور کار هنوز محصولی از بانک گرفته نشده است.</p>
        <div v-if="canRequest" class="d-flex gap-2 align-items-center">
          <select v-model="productId" class="form-select form-select-sm" style="max-width: 320px" @focus="products.length || load()">
            <option :value="null">محصول را انتخاب کنید</option>
            <option v-for="p in products" :key="p.id" :value="p.id" :disabled="p.releasedBomId === null">{{ p.code }} — {{ p.title }}{{ p.releasedBomId === null ? " (BOM منتشرشده ندارد)" : "" }}</option>
          </select>
          <input v-model.number="takeCr" type="number" class="form-control form-control-sm" style="max-width: 200px" placeholder="درخواست تغییر تاییدشده (اگر برنامهٔ قرارداد قفل است)" />
          <button :disabled="busy || !productId" class="btn btn-sm btn-primary" @click="take">گرفتن از بانک و کپی به‌عنوان مبنا</button>
        </div>
      </div>

      <ReadinessPanel v-if="loaded && !baseline && productId && workOrderId" :product-id="productId" :work-order-id="workOrderId" />

      <div v-if="baseline">
        <p class="small">
          محصول <span class="inv-ltr fw-bold">{{ baseline.productCode }}</span> · منبع: <span class="inv-ltr">{{ baseline.sourceBomCode }}</span> نسخه {{ revisionText(baseline.sourceBomRevision) }} · قالب WBS نسخه
          {{ baseline.wbsTemplateVersion || "—" }} · تعداد در دستور کار {{ baseline.count }} · کپی شده در {{ jalali(baseline.copiedAt) }}
        </p>
        <div v-if="baseline.pcBaselineCurrent" class="alert alert-warning py-1 small">
          خط‌مبنای کنترل پروژهٔ این قرارداد فعال است؛ انحراف تاییدشده فقط با «درخواست تغییر» تاییدشدهٔ هیئت (کنترل پروژه) اعمال می‌شود.
        </div>

        <ul class="nav nav-tabs mb-3 d-print-none">
          <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'bom' }" href="#" @click.prevent="tab = 'bom'">BOM پروژه</a></li>
          <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'changes' }" href="#" @click.prevent="tab = 'changes'">انحراف‌ها ({{ baseline.changes.length }})</a></li>
          <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'opc' }" href="#" @click.prevent="tab = 'opc'">چارت‌ها ({{ baseline.routings.length }})</a></li>
          <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'wbs' }" href="#" @click.prevent="tab = 'wbs'">WBS کنترل پروژه ({{ baseline.pcWbs.length }})</a></li>
        </ul>

        <div v-if="tab === 'bom'">
          <div v-if="canRequest" class="mb-2 d-print-none"><button class="btn btn-sm btn-outline-primary" @click="newChange(1)">+ افزودن قطعه (انحراف)</button></div>
          <table class="table table-sm table-hover">
            <thead><tr><th>#</th><th>شماره قطعه</th><th>نام</th><th>تعداد برای یک دستگاه</th><th>مجموع پروژه</th><th>جنس</th><th>ساخت/خرید</th><th class="d-print-none"></th></tr></thead>
            <tbody>
              <tr v-for="l in lines" :key="l.id" :class="{ 'table-light fw-bold': l.isHeading, 'text-decoration-line-through inv-muted': l.isRemoved }">
                <template v-if="l.isHeading"><td></td><td colspan="7">{{ l.partName }}</td></template>
                <template v-else>
                  <td>{{ l.itemNumber }}</td><td class="inv-ltr">{{ l.partNumber }}</td><td>{{ l.partName }}</td><td>{{ num(l.qty) }}</td>
                  <td>{{ num(l.totalQty) }}<span v-if="l.changedByChangeId" class="inv-badge warn ms-1">انحراف</span></td>
                  <td class="inv-ltr">{{ l.material }}</td><td>{{ makeOrBuyShort[l.makeOrBuy] }}</td>
                  <td class="d-print-none text-nowrap">
                    <template v-if="canRequest && !l.isRemoved">
                      <button class="btn btn-sm btn-outline-secondary py-0" @click="newChange(4, l)">تعداد</button>
                      <button class="btn btn-sm btn-outline-secondary py-0" @click="newChange(3, l)">جایگزینی</button>
                      <button class="btn btn-sm btn-outline-danger py-0" @click="newChange(2, l)">حذف</button>
                    </template>
                  </td>
                </template>
              </tr>
            </tbody>
          </table>
        </div>

        <table v-if="tab === 'changes'" class="table table-sm">
          <thead><tr><th>شماره</th><th>نوع</th><th>قطعه</th><th>مقدار جدید</th><th>دلیل</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!baseline.changes.length"><td colspan="7" class="text-center inv-muted">انحرافی ثبت نشده است.</td></tr>
            <tr v-for="c in baseline.changes" :key="c.id">
              <td class="inv-ltr">{{ c.number }}</td>
              <td>{{ changeKinds[c.kind] }}</td>
              <td class="inv-ltr">{{ c.targetPartNumber || lineOf(c.targetLineId)?.partNumber }}<span v-if="c.partNumber"> ← {{ c.partNumber }}</span></td>
              <td>{{ num(c.newQty) }}</td>
              <td>{{ c.reason }}<div v-if="c.rejectReason" class="text-danger small">رد: {{ c.rejectReason }}</div></td>
              <td>
                <span class="inv-badge" :class="c.status === 4 || c.status === 2 ? 'ok' : c.status === 3 || c.status === 5 ? 'bad' : 'warn'">{{ changeStatuses[c.status] }}</span>
                <div class="small inv-muted">مهندسی {{ c.engineeringApprovedByUserId ? "✓" : "—" }} · برنامه‌ریزی {{ c.planningApprovedByUserId ? "✓" : "—" }}</div>
              </td>
              <td class="d-print-none text-nowrap">
                <button v-if="c.canApproveEngineering" :disabled="busy" class="btn btn-sm btn-success py-0" @click="run(() => approveChangeEngineering(c.id), 'تایید مهندسی ثبت شد.')">تایید مهندسی</button>
                <button v-if="c.canApprovePlanning" :disabled="busy" class="btn btn-sm btn-success py-0" @click="run(() => approveChangePlanning(c.id), 'تایید برنامه‌ریزی ثبت شد.')">تایید برنامه‌ریزی</button>
                <button v-if="c.status === 1 && (c.canApproveEngineering || c.canApprovePlanning)" :disabled="busy" class="btn btn-sm btn-outline-danger py-0" @click="reject(c)">رد</button>
                <template v-if="c.status === 2">
                  <input v-if="c.needsChangeRequest" v-model.number="crIds[c.id]" type="number" class="form-control form-control-sm d-inline-block" style="width: 130px" placeholder="شناسهٔ درخواست تغییر" />
                  <button :disabled="busy" class="btn btn-sm btn-primary py-0" @click="run(() => applyChange(c.id, crIds[c.id]), 'انحراف روی مبنای پروژه اعمال شد.')">اعمال</button>
                </template>
                <button v-if="c.status === 0 || c.status === 1" :disabled="busy" class="btn btn-sm btn-outline-secondary py-0" @click="run(() => cancelChange(c.id), 'انحراف لغو شد.')">لغو</button>
              </td>
            </tr>
          </tbody>
        </table>

        <table v-if="tab === 'opc'" class="table table-sm">
          <thead><tr><th>قطعه</th><th>عنوان</th><th>نسخه</th><th>مرحله‌ها</th><th></th></tr></thead>
          <tbody>
            <tr v-for="r in baseline.routings" :key="r.id"><td class="inv-ltr">{{ r.partNumber }}</td><td>{{ r.title }}</td><td class="inv-ltr">{{ revisionText(r.revision) }}</td><td>{{ r.stepCount }}</td>
              <td><router-link :to="{ name: 'ProductBankRoutingEdit', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td></tr>
          </tbody>
        </table>

        <div v-if="tab === 'wbs'">
          <p class="small inv-muted">WBS کنترل پروژه تنها WBS پروژه است؛ از قالب تاییدشدهٔ محصول و پس از پذیرش بازبینی قرارداد ساخته می‌شود ({{ baseline.pcDependencies }} وابستگی).</p>
          <p v-if="baseline.wbsNote" class="alert alert-warning py-1 small">{{ baseline.wbsNote }}</p>
          <div v-if="canRequest && !baseline.pcProductNodeId" class="mb-2 d-print-none d-flex gap-2 align-items-center">
            <input v-model.number="takeCr" type="number" class="form-control form-control-sm" style="max-width: 200px" placeholder="شمارهٔ درخواست تغییر (در صورت قفل بودن برنامه)" />
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="genWbs">ساخت WBS کنترل پروژه</button>
          </div>
          <table class="table table-sm">
            <thead><tr><th>کد</th><th>عنوان</th><th>E/P/C</th><th>مدت (روز)</th><th>واحد مسئول</th></tr></thead>
            <tbody>
              <tr v-for="n in baseline.pcWbs" :key="n.key" :class="{ 'fw-bold table-light': !n.isActivity }">
                <td class="inv-ltr">{{ n.code }}</td><td>{{ n.title }}</td><td class="inv-ltr">{{ n.phase }}</td><td class="inv-ltr">{{ n.isActivity ? n.durationDays : "" }}</td><td>{{ n.responsibleUnit }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 560px">
        <h2 class="inv-title mb-2">انحراف از BOM بانک — {{ changeKinds[form.kind] }}</h2>
        <p v-if="form.target" class="small">ردیف: <span class="inv-ltr">{{ form.target.partNumber }}</span> {{ form.target.partName }} (تعداد فعلی {{ num(form.target.qty) }})</p>
        <div class="row g-2">
          <template v-if="form.kind === 1 || form.kind === 3">
            <div class="col-6"><label class="form-label small mb-0">شماره قطعهٔ جدید *</label><input v-model="form.partNumber" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-6"><label class="form-label small mb-0">نام</label><input v-model="form.partName" class="form-control form-control-sm" /></div>
            <div class="col-6"><label class="form-label small mb-0">جنس</label><input v-model="form.material" class="form-control form-control-sm inv-ltr" /></div>
          </template>
          <div v-if="form.kind !== 2" class="col-3"><label class="form-label small mb-0">تعداد برای یک دستگاه *</label><input v-model.number="form.newQty" type="number" step="any" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">دلیل *</label><input v-model="form.reason" class="form-control form-control-sm" /></div>
        </div>
        <p class="small inv-muted mt-2">پس از ثبت، انحراف ارسال می‌شود و باید هم مهندسی و هم برنامه‌ریزی (دو نفر متفاوت، غیر از شما) تایید کنند.</p>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="sendForm">ثبت و ارسال</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
