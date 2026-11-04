<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import AttachmentPanel from "@/components/common/AttachmentPanel.vue";
import { AttachmentOwner } from "@/composables/attachments";
import { approveRouting, checkRouting, deleteRoutingDraft, getProducts, getRouting, getStages, newRoutingRevision, publishRouting, returnRouting, saveRouting, submitRouting } from "@/api/ProductBankAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, evidenceKinds, makeOrBuy, revisionText, routingStatuses, stageKinds, stageSymbols } from "@/composables/productBank";

// OPC routing editor and viewer: a vertical chart with the OPC symbols (circle operation, square quality control, arrow transport, triangle storage), one column per part.
// Steps at a stockable stage are linked to the stage item (part number + stage). An approved chart is read-only; a new revision copies it.
const route = useRoute();
const router = useRouter();
const routing = ref<any>(null);
const isNew = computed(() => route.params.id === "new");
const header = ref<any>({ productId: null, partNumber: "", title: "", projectNumber: "", platform: "", documentDate: "", changeReason: "" });
const steps = ref<any[]>([]);
const stages = ref<any[]>([]);
const products = ref<any[]>([]);
const note = ref("");
const busy = ref(false);
const editable = computed(() => isNew.value || (routing.value?.canEdit ?? false));
const canPublish = computed(() => routing.value?.canPublish && hasRole(PbRoles.publish));
const stageOf = (code: string) => stages.value.find((s) => s.code === code);

const lanes = computed(() => {
  const map = new Map<string, any[]>();
  for (const s of steps.value) {
    const key = s.partNumber || header.value.partNumber;
    if (!map.has(key)) map.set(key, []);
    map.get(key)!.push(s);
  }
  return [...map.entries()].map(([partNumber, list]) => ({ partNumber, partName: list.find((x) => x.partName)?.partName || "", steps: [...list].sort((a, b) => a.seq - b.seq) }));
});

async function load() {
  const [st] = await getStages(false);
  stages.value = st || [];
  const [p] = await getProducts();
  products.value = p || [];
  if (isNew.value) {
    routing.value = null;
    steps.value = [];
    return;
  }
  const [data, error] = await getRouting(Number(route.params.id));
  if (error) return fail(error);
  routing.value = data;
  header.value = { productId: data.productId, partNumber: data.partNumber, title: data.title, projectNumber: data.projectNumber, platform: data.platform, documentDate: data.documentDate?.slice(0, 10) || "", changeReason: data.changeReason || "" };
  steps.value = (data.steps || []).map((s: any) => ({ ...s }));
}
onMounted(load);
watch(() => route.params.id, load);

function addStep(partNumber: string, partName = "") {
  const own = steps.value.filter((s) => (s.partNumber || header.value.partNumber) === partNumber);
  steps.value.push({ seq: Math.max(0, ...own.map((s) => s.seq)) + 1, partNumber, partName, stageCode2: "QC", title: "", resourceClass: "", standardHours: null, isQcPoint: true, requiredEvidence: "", isOutside: false, kind: 2 });
}
function addPart() {
  const pn = window.prompt("شماره قطعهٔ ستون جدید (مثلا 16.11.01.001.2)");
  if (pn && pn.trim()) addStep(pn.trim().toUpperCase());
}
function onStage(s: any) {
  const st = stageOf(s.stageCode2);
  if (st) {
    s.kind = st.kind;
    s.isQcPoint = st.kind === 2 || st.kind === 5;
  }
}
function removeStep(s: any) {
  steps.value.splice(steps.value.indexOf(s), 1);
}
function toggleEvidence(s: any, k: string) {
  const set = new Set(String(s.requiredEvidence || "").split(",").filter(Boolean));
  if (set.has(k)) set.delete(k);
  else set.add(k);
  s.requiredEvidence = [...set].join(",");
}

async function run(fn: () => Promise<any>, message: string, after?: (d: any) => void) {
  busy.value = true;
  const [data, error] = await fn();
  busy.value = false;
  if (error) return fail(error);
  ok(message);
  note.value = "";
  if (after) after(data);
  else load();
}
const save = () =>
  run(
    () =>
      saveRouting({
        id: routing.value?.id ?? null, ...header.value, productId: header.value.productId || null, documentDate: header.value.documentDate || null,
        steps: steps.value.map((s) => ({ seq: s.seq, partNumber: s.partNumber, partName: s.partName, stageCode2: s.stageCode2, kind: s.kind || null, title: s.title, workCenterId: s.workCenterId ?? null, resourceClass: s.resourceClass, standardHours: s.standardHours === "" ? null : s.standardHours, isQcPoint: !!s.isQcPoint, requiredEvidence: s.requiredEvidence, isOutside: !!s.isOutside, makeOrBuy: s.makeOrBuy || null, note: s.note })),
      }),
    "چارت ذخیره شد.",
    (d) => (isNew.value ? router.replace({ name: "ProductBankRoutingEdit", params: { id: d.id } }) : load())
  );
const submit = () => run(() => submitRouting(routing.value.id), "چارت برای بررسی ارسال شد و کالای مراحل انبارشی ساخته شد.");
const sendBack = () => run(() => returnRouting(routing.value.id, note.value), "چارت به پیش‌نویس برگشت.");
const check = () => run(() => checkRouting(routing.value.id), "کنترل ثبت شد.");
const approve = () => run(() => approveRouting(routing.value.id), "چارت تایید شد.");
const revise = () => run(() => newRoutingRevision(routing.value.id, note.value), "نسخهٔ جدید ساخته شد.", (d) => router.push({ name: "ProductBankRoutingEdit", params: { id: d.id } }));
const publish = () => run(() => publishRouting(routing.value.id), "مرحله‌ها به مسیر ساخت (MES) منتقل شد؛ نسخهٔ مسیر ساخت منتظر تایید در بخش تولید است.");
const removeDraft = () => run(() => deleteRoutingDraft(routing.value.id), "پیش‌نویس حذف شد.", () => router.push({ name: "ProductBankRoutings" }));
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="routing ? 'OPC ' + routing.partNumber + ' — نسخه ' + revisionText(routing.revision) : 'چارت OPC جدید'" :subtitle="routing ? routingStatuses[routing.status] : 'پیش‌نویس'" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">
          {{ routing ? routing.title || routing.partNumber : "چارت OPC جدید" }}
          <span v-if="routing" class="inv-badge" :class="routing.status === 2 ? 'ok' : 'warn'">{{ routingStatuses[routing.status] }}</span>
          <span v-if="routing?.importedSigned" class="inv-badge ok">امضای کاغذی</span>
        </h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <router-link :to="{ name: 'ProductBankRoutings' }" class="btn btn-sm btn-outline-secondary">فهرست</router-link>
        </div>
      </div>

      <div class="row g-2 mb-3">
        <div class="col-3"><label class="form-label small mb-0">شماره مجموعه / قطعه *</label><input v-model="header.partNumber" class="form-control form-control-sm inv-ltr" :disabled="!editable || !isNew" /></div>
        <div class="col-3"><label class="form-label small mb-0">عنوان</label><input v-model="header.title" class="form-control form-control-sm" :disabled="!editable" /></div>
        <div class="col-3">
          <label class="form-label small mb-0">محصول</label>
          <select v-model="header.productId" class="form-select form-select-sm" :disabled="!editable"><option :value="null">—</option><option v-for="p in products" :key="p.id" :value="p.id">{{ p.code }} {{ p.title }}</option></select>
        </div>
        <div class="col-3"><label class="form-label small mb-0">شماره پروژه</label><input v-model="header.projectNumber" class="form-control form-control-sm inv-ltr" :disabled="!editable" /></div>
        <div v-if="routing && routing.revision > 0" class="col-12"><label class="form-label small mb-0">دلیل تغییر نسخه</label><input v-model="header.changeReason" class="form-control form-control-sm" :disabled="!editable" /></div>
      </div>
      <p v-if="routing" class="small inv-muted">
        تهیه: کاربر {{ routing.preparedByUserId }} · کنترل: {{ routing.checkedByUserId ? "کاربر " + routing.checkedByUserId + " (" + jalali(routing.checkedAt) + ")" : "—" }} · تایید:
        {{ routing.approvedByUserId ? "کاربر " + routing.approvedByUserId + " (" + jalali(routing.approvedAt) + ")" : "—" }} — سه نفر متفاوت
      </p>

      <div class="d-flex gap-2 flex-wrap mb-3 d-print-none align-items-center">
        <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        <button v-if="routing?.canSubmit" :disabled="busy" class="btn btn-sm btn-success" @click="submit">ارسال برای بررسی</button>
        <button v-if="routing?.canCheck" :disabled="busy" class="btn btn-sm btn-success" @click="check">کنترل کردم</button>
        <button v-if="routing?.canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="approve">تایید چارت</button>
        <button v-if="canPublish" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="publish">انتقال به مسیر ساخت (MES)</button>
        <button v-if="routing && routing.status === 2" :disabled="busy || !note.trim()" class="btn btn-sm btn-outline-primary" @click="revise">نسخهٔ جدید</button>
        <button v-if="routing && routing.status === 1" :disabled="busy || !note.trim()" class="btn btn-sm btn-outline-warning" @click="sendBack">برگشت به پیش‌نویس</button>
        <button v-if="routing?.canEdit && routing.status === 0" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="removeDraft">حذف پیش‌نویس</button>
        <input v-if="routing" v-model="note" class="form-control form-control-sm" style="max-width: 300px" placeholder="یادداشت / دلیل" />
      </div>

      <!-- the chart: one column per part -->
      <div class="d-flex gap-3 flex-wrap align-items-start" style="overflow-x: auto">
        <div v-for="lane in lanes" :key="lane.partNumber" class="border rounded p-2" style="min-width: 260px; max-width: 320px; flex: 1 1 260px">
          <div class="fw-bold small">{{ lane.partName || "—" }}</div>
          <div class="inv-ltr small inv-muted mb-2">{{ lane.partNumber }}</div>
          <div v-for="s in lane.steps" :key="s.seq" class="d-flex gap-2 align-items-start border-top py-1">
            <span class="fs-5" style="width: 24px; text-align: center" :title="stageKinds[s.kind]">{{ stageSymbols[s.kind] }}</span>
            <div class="flex-grow-1">
              <template v-if="!editable">
                <div class="small"><span class="inv-ltr fw-bold">{{ s.seq }}</span> · {{ s.title || stageOf(s.stageCode2)?.titleFa }} <span class="inv-ltr inv-muted">({{ s.stageCode2 }})</span></div>
                <div v-if="s.isQcPoint && s.requiredEvidence" class="small text-primary inv-ltr">{{ s.requiredEvidence.split(",").join(" · ") }}</div>
                <div v-if="s.stageItemCode" class="small inv-muted">کالای مرحله: <span class="inv-ltr">{{ s.stageItemCode }}</span></div>
                <div v-if="s.resourceClass || s.standardHours" class="small inv-muted">{{ s.resourceClass }} {{ s.standardHours ? "· " + s.standardHours + " ساعت" : "" }}</div>
              </template>
              <template v-else>
                <div class="d-flex gap-1">
                  <input v-model.number="s.seq" type="number" class="form-control form-control-sm px-1" style="width: 48px" />
                  <select v-model="s.stageCode2" class="form-select form-select-sm" @change="onStage(s)"><option v-for="st in stages" :key="st.code" :value="st.code">{{ st.code }} {{ st.titleFa }}</option></select>
                  <button class="btn btn-sm py-0 text-danger" @click="removeStep(s)">×</button>
                </div>
                <div class="d-flex gap-1 mt-1">
                  <input v-model="s.resourceClass" class="form-control form-control-sm" placeholder="منبع / مرکز کار" />
                  <input v-model.number="s.standardHours" type="number" step="any" class="form-control form-control-sm" style="width: 70px" placeholder="ساعت ★" />
                </div>
                <div v-if="s.isQcPoint" class="d-flex gap-1 flex-wrap mt-1">
                  <label v-for="k in evidenceKinds" :key="k" class="small inv-ltr"><input type="checkbox" :checked="String(s.requiredEvidence || '').split(',').includes(k)" @change="toggleEvidence(s, k)" /> {{ k }}</label>
                </div>
                <label class="small mt-1"><input v-model="s.isOutside" type="checkbox" /> انجام بیرونی</label>
              </template>
            </div>
          </div>
          <button v-if="editable" class="btn btn-sm btn-outline-secondary mt-2 w-100" @click="addStep(lane.partNumber, lane.partName)">+ مرحله</button>
        </div>
        <button v-if="editable" class="btn btn-sm btn-outline-secondary d-print-none" @click="addPart">+ ستون قطعه</button>
      </div>
      <p v-if="!lanes.length && !editable" class="inv-muted">مرحله‌ای ثبت نشده است.</p>
      <p v-if="editable && !lanes.length" class="inv-muted">ابتدا شماره مجموعه را بنویسید و «+ ستون قطعه» را بزنید.</p>
      <div class="small inv-muted mt-3">○ عملیات · □ کنترل کیفیت · → حمل · △ انبار · ◇ توقف بازرسی. ★ زمان استاندارد در چارت‌های کاغذی نیست و پس از اندازه‌گیری شرکت وارد می‌شود.</div>
    </div>
    <AttachmentPanel v-if="routing?.id" :owner-type="AttachmentOwner.PbOpcRouting" :owner-id="routing.id" title="مدارک چارت فرآیند — به درخواست‌های خرید خودکار پیوست می‌شود" />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
