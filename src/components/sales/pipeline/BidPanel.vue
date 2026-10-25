<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { decideSlBid, getSlBidDecision, saveSlBidDecision, submitSlBidDecision } from "@/api/SalesAPIs";
import { fail, ok } from "@/composables/inventory";
import { badgeOfBid, bidStatuses, persianOf, sanctionChecks } from "@/composables/sales";
import { SalesRoles, hasRole } from "@/composables/roles";
import { blankToNull, numOrNull } from "./pipelineKit";

// Bid / no-bid gate of one inquiry. Sales fills the checklist and submits it; the CEO decides (no-bid needs a reason). The capacity item is read by the server from the production load.
const props = defineProps({ inquiryId: { type: Number, required: true }, inquiryStatus: { type: Number, default: 0 } });
const emit = defineEmits(["changed"]);

const d = ref<any>(null);
const f = ref<any>(blank());
const busy = ref(false);
const reason = ref("");
const loaded = ref(false);
const canWrite = hasRole(SalesRoles.writers);
const canDecide = hasRole(SalesRoles.bidDeciders);
const editable = computed(() => canWrite && (!d.value || d.value.status === 1));

function blank() {
  return { technicalCapabilityOk: null as boolean | null, technicalNote: "", capacityNote: "", commercialRiskReviewed: false, commercialRiskNote: "", paymentTermsReviewed: false, paymentTermsNote: "",
    guaranteesReviewed: false, guaranteesNote: "", sanctionCheck: 0, sanctionNote: "", expectedMarginPercent: "" as any };
}
function fill(x: any) {
  d.value = x;
  f.value = { technicalCapabilityOk: x.technicalCapabilityOk ?? null, technicalNote: x.technicalNote || "", capacityNote: x.capacityNote || "", commercialRiskReviewed: !!x.commercialRiskReviewed, commercialRiskNote: x.commercialRiskNote || "",
    paymentTermsReviewed: !!x.paymentTermsReviewed, paymentTermsNote: x.paymentTermsNote || "", guaranteesReviewed: !!x.guaranteesReviewed, guaranteesNote: x.guaranteesNote || "",
    sanctionCheck: x.sanctionCheck ?? 0, sanctionNote: x.sanctionNote || "", expectedMarginPercent: x.expectedMarginPercent ?? "" };
}
async function load() {
  const [x, error] = await getSlBidDecision(props.inquiryId);
  loaded.value = true;
  if (error || !x) {
    d.value = null; // no checklist yet: the API answers "not found" until sales saves the first one
    f.value = blank();
    return;
  }
  fill(x);
}
async function save(silent = false) {
  const v = f.value;
  const margin = numOrNull(v.expectedMarginPercent);
  busy.value = true;
  const [x, error] = await saveSlBidDecision({
    inquiryId: props.inquiryId, technicalCapabilityOk: v.technicalCapabilityOk, technicalNote: blankToNull(v.technicalNote), capacityNote: blankToNull(v.capacityNote), commercialRiskReviewed: v.commercialRiskReviewed,
    commercialRiskNote: blankToNull(v.commercialRiskNote), paymentTermsReviewed: v.paymentTermsReviewed, paymentTermsNote: blankToNull(v.paymentTermsNote), guaranteesReviewed: v.guaranteesReviewed,
    guaranteesNote: blankToNull(v.guaranteesNote), sanctionCheck: v.sanctionCheck, sanctionNote: blankToNull(v.sanctionNote), expectedMarginPercent: margin, rowVersion: d.value?.rowVersion ?? null,
  });
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  fill(x);
  if (!silent) ok("چک‌لیست ذخیره شد.");
  return true;
}
async function submit() {
  if (!(await save(true))) return;
  busy.value = true;
  const [x, error] = await submitSlBidDecision({ inquiryId: props.inquiryId, rowVersion: d.value?.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok("چک‌لیست برای مدیرعامل ارسال شد.");
  fill(x);
  emit("changed");
}
async function decide(decision: "bid" | "nobid") {
  if (decision === "nobid" && !reason.value.trim()) return fail({ message: "برای عدم شرکت، دلیل را بنویسید." });
  busy.value = true;
  const [x, error] = await decideSlBid({ inquiryId: props.inquiryId, decision, reason: reason.value, rowVersion: d.value?.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  ok(decision === "bid" ? "شرکت در مناقصه تایید شد." : "عدم شرکت ثبت شد.");
  fill(x);
  emit("changed");
}
onMounted(load);
</script>

<template>
  <div class="border rounded p-3 mt-3">
    <div class="d-flex align-items-center gap-2 mb-2">
      <h2 class="inv-title mb-0">تصمیم شرکت در مناقصه</h2>
      <span v-if="d" :class="badgeOfBid(d.status)" class="inv-badge">{{ bidStatuses[d.status] }}</span>
    </div>
    <p v-if="loaded && !d && !canWrite" class="inv-muted">هنوز چک‌لیست تصمیم‌گیری برای این استعلام ثبت نشده است.</p>
    <template v-if="d || canWrite">
      <div class="row g-2">
        <div class="col-md-6">
          <label class="form-label small mb-0">توانمندی فنی</label>
          <select v-model="f.technicalCapabilityOk" :disabled="!editable" class="form-select form-select-sm"><option :value="null">بررسی نشده</option><option :value="true">شرکت توانایی فنی دارد</option><option :value="false">توانایی فنی ندارد</option></select>
          <input v-model="f.technicalNote" :disabled="!editable" class="form-control form-control-sm mt-1" placeholder="توضیح فنی" />
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">ظرفیت تولید</label>
          <div class="form-control form-control-sm bg-light" style="height: auto">
            <template v-if="d && d.capacityOverloadWeeks !== null && d.capacityOverloadWeeks !== undefined">
              <span :class="d.capacityOverloadWeeks > 0 ? 'text-danger' : 'text-success'">{{ d.capacityOverloadWeeks }} هفته بار بیش از ظرفیت</span>
              <span class="text-muted"> تا {{ persianOf(d.capacityCheckedUntilText, d.capacityCheckedUntil) }}</span>
            </template>
            <span v-else class="text-muted">با تاریخ تحویل درخواستی استعلام و پس از ذخیره از بار تولید خوانده می‌شود.</span>
          </div>
          <input v-model="f.capacityNote" :disabled="!editable" class="form-control form-control-sm mt-1" placeholder="توضیح ظرفیت" />
        </div>
        <div class="col-md-6">
          <label class="form-check"><input v-model="f.commercialRiskReviewed" :disabled="!editable" class="form-check-input" type="checkbox" /> <span class="form-check-label">ریسک تجاری بررسی شد</span></label>
          <input v-model="f.commercialRiskNote" :disabled="!editable" class="form-control form-control-sm" placeholder="ریسک تجاری" />
        </div>
        <div class="col-md-6">
          <label class="form-check"><input v-model="f.paymentTermsReviewed" :disabled="!editable" class="form-check-input" type="checkbox" /> <span class="form-check-label">شرایط پرداخت بررسی شد</span></label>
          <input v-model="f.paymentTermsNote" :disabled="!editable" class="form-control form-control-sm" placeholder="شرایط پرداخت" />
        </div>
        <div class="col-md-6">
          <label class="form-check"><input v-model="f.guaranteesReviewed" :disabled="!editable" class="form-check-input" type="checkbox" /> <span class="form-check-label">تضمین‌ها بررسی شد</span></label>
          <input v-model="f.guaranteesNote" :disabled="!editable" class="form-control form-control-sm" placeholder="تضمین‌ها" />
        </div>
        <div class="col-md-6">
          <label class="form-label small mb-0">تحریم و صادرات</label>
          <select v-model="f.sanctionCheck" :disabled="!editable" class="form-select form-select-sm"><option v-for="(t, k) in sanctionChecks" :key="k" :value="Number(k)">{{ t }}</option></select>
          <input v-model="f.sanctionNote" :disabled="!editable" class="form-control form-control-sm mt-1" placeholder="توضیح تحریم و صادرات" />
        </div>
        <div class="col-md-3"><label class="form-label small mb-0">حاشیه سود مورد انتظار (درصد)</label><input v-model="f.expectedMarginPercent" :disabled="!editable" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
      </div>
      <div v-if="editable" class="d-flex gap-2 mt-3">
        <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="save()">ذخیره چک‌لیست</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="submit">ارسال برای تصمیم مدیرعامل</button>
      </div>
    </template>

    <div v-if="d && d.status === 2 && canDecide" class="border-top mt-3 pt-3">
      <label class="form-label small mb-0">دلیل (برای عدم شرکت الزامی است)</label>
      <textarea v-model="reason" class="form-control form-control-sm mb-2" rows="2" />
      <div class="d-flex gap-2">
        <button :disabled="busy" class="btn btn-sm btn-success" @click="decide('bid')">شرکت در مناقصه</button>
        <button :disabled="busy" class="btn btn-sm btn-danger" @click="decide('nobid')">عدم شرکت</button>
      </div>
    </div>
    <p v-else-if="d && d.status === 2" class="small text-muted mt-2">منتظر تصمیم مدیرعامل. ارسال‌شده در {{ persianOf(d.submittedAtText, d.submittedAt, true) }}.</p>

    <div v-if="d && (d.status === 3 || d.status === 4)" class="border-top mt-3 pt-2">
      <div>تصمیم: <b>{{ bidStatuses[d.status] }}</b> - {{ d.decidedByName || "" }} - {{ persianOf(d.decidedAtText, d.decidedAt, true) }}</div>
      <div v-if="d.decisionReason">دلیل: {{ d.decisionReason }}</div>
      <div v-if="d.tenderId">
        مناقصه پیش‌نویس‌شده توسط سیستم:
        <router-link :to="{ name: 'TenderUpdate', params: { id: d.tenderId } }">مشاهده مناقصه</router-link>
      </div>
    </div>
  </div>
</template>
