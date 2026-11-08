<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import { OpsDataRoles, hasRole } from "@/composables/roles";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import Pb6Table from "@/components/common/Pb6Table.vue";
import { decideOpsException, decideReadinessException, getOpsExceptions, getOpsFindings, getOpsRules, getOpsSummary, getReadinessExceptions, getReadinessObligations, getReadinessSummary, requestOpsException, resolveOpsFinding } from "@/api/OpsDataAPIs";
import { fail, ok } from "@/composables/inventory";

// «کیفیت داده»: findings of the real-time gates and the OPS-DATA seat, exceptions, rules and the engineering readiness of the projects.
const route = useRoute();
const tabs = ["findings", "exceptions", "rules", "readiness"];
const queryTab = String(route.query.tab || "");   // the links of the notifications open the right tab (?tab=exceptions)
const tab = ref<"findings" | "exceptions" | "rules" | "readiness">((tabs.includes(queryTab) ? queryTab : "findings") as any);
const canResolve = hasRole(OpsDataRoles.makers);
const canRequest = hasRole(OpsDataRoles.makers);
const canDecideReady = hasRole(OpsDataRoles.readinessDeciders);
const canDecide = hasRole(OpsDataRoles.supervisors);
const justification = ref("");
const rows = ref<any[]>([]);
const summary = ref<any>(null);
const readiness = ref<any>(null);
const obligations = ref<any[]>([]);
const readyExceptions = ref<any[]>([]);
const filter = ref<any>({ unit: "", rule: "", state: "open", from: "", to: "" });
const current = ref<any>(null);
const reason = ref("");
const correctiveId = ref<number | null>(null);
const busy = ref(false);
const units = [{ v: "", t: "همه‌ی واحدها" }, { v: 1, t: "انبار" }, { v: 2, t: "تولید" }, { v: 3, t: "کیفیت" }, { v: 4, t: "مالی" }];
const states = [{ v: "open", t: "باز" }, { v: "resolved", t: "رفع‌شده یا تاییدشده" }, { v: "superseded", t: "منسوخ" }, { v: "all", t: "همه" }];
const severityText: Record<number, string> = { 1: "هشدار", 2: "مسدودکننده" };
const sourceText: Record<number, string> = { 1: "گیت لحظه‌ای", 2: "صندلی OPS-DATA" };

const findingCols = [{ key: "unitTitle", title: "واحد" }, { key: "documentNumber", title: "سند" }, { key: "ruleTitle", title: "قاعده" }, { key: "severityText", title: "شدت" }, { key: "message", title: "پیام" }, { key: "detectedAtText", title: "تاریخ" }, { key: "statusTitle", title: "وضعیت" }, { key: "sourceText", title: "منبع" }];
const exceptionCols = [{ key: "unitTitle", title: "واحد" }, { key: "documentNumber", title: "سند" }, { key: "ruleTitle", title: "قاعده" }, { key: "justification", title: "توجیه" }, { key: "requestedByName", title: "درخواست‌دهنده" }, { key: "requestedAtText", title: "تاریخ" }, { key: "statusTitle", title: "وضعیت" }];
const ruleCols = [{ key: "code", title: "کد" }, { key: "title", title: "عنوان" }, { key: "enabledText", title: "فعال" }, { key: "warn", title: "آستانه‌ی هشدار" }, { key: "block", title: "آستانه‌ی مسدود" }, { key: "minHistory", title: "حداقل سابقه" }];
const readyCols = [{ key: "workOrderCode", title: "پروژه / دستور کار" }, { key: "productCode", title: "محصول" }, { key: "percentText", title: "آمادگی" }, { key: "hardMissing", title: "ناقص الزامی" }, { key: "softMissing", title: "ناقص توصیه‌ای" }, { key: "openObligations", title: "تعهد باز" }, { key: "overdueObligations", title: "تعهد معوق" }];
const obligationCols = [{ key: "workOrderCode", title: "پروژه" }, { key: "title", title: "مورد" }, { key: "ownerTitle", title: "مسئول" }, { key: "missingCount", title: "تعداد" }, { key: "dueDateText", title: "سررسید" }, { key: "overdueText", title: "معوق" }];
const readyExCols = [{ key: "workOrderCode", title: "پروژه" }, { key: "itemCodes", title: "موارد" }, { key: "justification", title: "توجیه" }, { key: "requestedByName", title: "درخواست‌دهنده" }, { key: "pmoByName", title: "تایید دفتر پروژه" }, { key: "ceoByName", title: "تایید مدیرعامل" }, { key: "statusTitle", title: "وضعیت" }];

async function load() {
  current.value = null;
  const [s] = await getOpsSummary(filter.value.unit || null);
  summary.value = s;
  if (tab.value === "findings") {
    const [d, e] = await getOpsFindings({ unit: filter.value.unit, rule: filter.value.rule, state: filter.value.state, from: filter.value.from, to: filter.value.to });
    if (e) return fail(e);
    rows.value = (d?.data ?? d ?? []).map((r: any) => ({ ...r, severityText: severityText[r.severity], sourceText: sourceText[r.source] }));
  } else if (tab.value === "exceptions") {
    const [d, e] = await getOpsExceptions({ unit: filter.value.unit });
    if (e) return fail(e);
    rows.value = d?.data ?? d ?? [];
  } else if (tab.value === "rules") {
    const [d, e] = await getOpsRules();
    if (e) return fail(e);
    rows.value = (d || []).map((r: any) => ({ ...r, enabledText: r.enabled ? "بله" : "خیر" }));
  } else {
    const [d, e] = await getReadinessSummary();
    if (e) return fail(e);
    readiness.value = d;
    rows.value = (d.items || []).map((r: any) => ({ ...r, percentText: r.percent + "٪" }));
    const [o] = await getReadinessObligations(null, true);
    obligations.value = (o || []).map((r: any) => ({ ...r, overdueText: r.overdue ? "بله" : "" }));
    const [x] = await getReadinessExceptions(null);
    readyExceptions.value = x || [];
  }
}
function switchTab(t: any) {
  tab.value = t;
  rows.value = [];
  load();
}
function open(r: any) {
  current.value = r;
  reason.value = "";
  justification.value = "";
  correctiveId.value = null;
}
async function run(fn: () => Promise<any>, msg: string) {
  busy.value = true;
  const [, e] = await fn();
  busy.value = false;
  if (e) return fail(e);
  ok(msg);
  load();
}
const resolve = (kind: string) => run(() => resolveOpsFinding(current.value.id, kind, reason.value, correctiveId.value), kind === "verified" ? "درستی داده تایید شد." : "یافته با سند اصلاحی رفع شد.");
const requestException = () => run(() => requestOpsException(current.value.id, justification.value), "درخواست استثنا ثبت شد؛ سرپرست دیگر واحد باید تصمیم بگیرد.");
const decide = (approve: boolean) => run(() => decideOpsException(current.value.id, approve, reason.value), approve ? "استثنا تایید شد." : "استثنا رد شد.");
const decideReady = (id: number, approve: boolean) => run(() => decideReadinessException(id, approve, reason.value), approve ? "تایید ثبت شد." : "رد ثبت شد.");
watch(() => [filter.value.from, filter.value.to], () => { if (tab.value === "findings") load(); });
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="کیفیت داده" />
    <div class="inv-card">
      <div v-if="summary" class="row text-center mb-3">
        <div class="col"><div class="fs-4">{{ summary.openFindings }}</div><small>یافته‌ی باز</small></div>
        <div class="col"><div class="fs-4">{{ summary.openBlocks }}</div><small>مسدودکننده</small></div>
        <div class="col"><div class="fs-4">{{ summary.pendingExceptions }}</div><small>استثنای در انتظار</small></div>
        <div class="col"><div class="fs-4">{{ summary.exceptionsThisWeek }}</div><small>استثنا در این هفته</small></div>
        <div class="col"><div class="fs-4">{{ summary.resolvedThisWeek }}</div><small>رفع‌شده در این هفته</small></div>
      </div>
      <div class="d-flex flex-wrap gap-1 mb-3">
        <button v-for="t in [['findings', 'یافته‌ها'], ['exceptions', 'استثناها'], ['rules', 'قاعده‌ها'], ['readiness', 'آمادگی مهندسی پروژه‌ها']]" :key="t[0]" class="btn btn-sm" :class="tab === t[0] ? 'btn-primary' : 'btn-outline-secondary'" @click="switchTab(t[0])">{{ t[1] }}</button>
      </div>

      <template v-if="tab === 'findings'">
        <div class="d-flex flex-wrap gap-2 mb-2">
          <select v-model="filter.unit" class="form-select form-select-sm w-auto" @change="load"><option v-for="u in units" :key="u.v" :value="u.v">{{ u.t }}</option></select>
          <input v-model="filter.rule" class="form-control form-control-sm w-auto" placeholder="کد قاعده" @change="load" />
          <select v-model="filter.state" class="form-select form-select-sm w-auto" @change="load"><option v-for="s in states" :key="s.v" :value="s.v">{{ s.t }}</option></select>
          <KsDatePicker v-model="filter.from" element-tag="ops-from" placeholder="از تاریخ" />
          <KsDatePicker v-model="filter.to" element-tag="ops-to" placeholder="تا تاریخ" />
        </div>
        <Pb6Table :columns="findingCols" :rows="rows" @select="open" />
        <div v-if="current" class="border rounded p-2 mt-2">
          <p class="mb-1">{{ current.message }}</p>
          <router-link v-if="current.link" :to="current.link">مشاهده‌ی سند</router-link>
          <template v-if="current.status === 1 && canRequest && current.severity === 2 && !current.exceptionId">
            <input v-model="justification" class="form-control form-control-sm mt-2" placeholder="توجیه درخواست استثنا (دست‌کم چند کلمه)" />
            <button class="btn btn-sm btn-outline-warning mt-1" :disabled="busy || justification.trim().length < 10" @click="requestException">درخواست استثنا</button>
          </template>
          <template v-if="current.status === 1 && canResolve">
            <input v-model="reason" class="form-control form-control-sm mt-2" placeholder="دلیل / توضیح" />
            <input v-model.number="correctiveId" type="number" class="form-control form-control-sm mt-1" placeholder="شناسه‌ی سند اصلاحی (برای «رفع شد با سند اصلاحی»)" />
            <div class="mt-2 d-flex gap-2">
              <button class="btn btn-sm btn-success" :disabled="busy || !correctiveId" @click="resolve('corrected')">رفع شد با سند اصلاحی</button>
              <button class="btn btn-sm btn-outline-primary" :disabled="busy || !reason.trim()" @click="resolve('verified')">تایید درستی داده</button>
            </div>
          </template>
        </div>
      </template>

      <template v-else-if="tab === 'exceptions'">
        <Pb6Table :columns="exceptionCols" :rows="rows" @select="open" />
        <div v-if="current" class="border rounded p-2 mt-2">
          <p class="mb-1">{{ current.message }}</p>
          <template v-if="current.canDecide && canDecide">
            <input v-model="reason" class="form-control form-control-sm" placeholder="دلیل تصمیم" />
            <div class="mt-2 d-flex gap-2">
              <button class="btn btn-sm btn-success" :disabled="busy" @click="decide(true)">تایید استثنا</button>
              <button class="btn btn-sm btn-outline-danger" :disabled="busy || !reason.trim()" @click="decide(false)">رد</button>
            </div>
          </template>
          <p v-else class="small inv-muted mb-0">این استثنا را فقط سرپرست دیگر همان واحد می‌تواند تصمیم‌گیری کند.</p>
        </div>
      </template>

      <Pb6Table v-else-if="tab === 'rules'" :columns="ruleCols" :rows="rows" />

      <template v-else>
        <p v-if="readiness" class="small">میانگین آمادگی: {{ readiness.averagePercent }}٪ · تعهد باز: {{ readiness.openObligations }} · معوق: {{ readiness.overdueObligations }}</p>
        <Pb6Table :columns="readyCols" :rows="rows" />
        <h2 class="h6 mt-3">تعهدهای باز</h2>
        <Pb6Table :columns="obligationCols" :rows="obligations" />
        <h2 class="h6 mt-3">درخواست‌های استثنا</h2>
        <Pb6Table :columns="readyExCols" :rows="readyExceptions" />
        <div v-if="canDecideReady && readyExceptions.some((x: any) => x.status === 'Pending')" class="mt-2">
          <input v-model="reason" class="form-control form-control-sm mb-1" placeholder="دلیل تصمیم" />
          <div v-for="x in readyExceptions.filter((y: any) => y.status === 'Pending')" :key="x.id" class="d-flex gap-2 align-items-center mb-1">
            <span class="small">{{ x.workOrderCode }}</span>
            <button class="btn btn-sm btn-success" :disabled="busy" @click="decideReady(x.id, true)">تایید</button>
            <button class="btn btn-sm btn-outline-danger" :disabled="busy || !reason.trim()" @click="decideReady(x.id, false)">رد</button>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
