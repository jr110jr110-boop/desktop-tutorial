<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnGapView from "@/components/engineering/EnGapView.vue";
import EnInfoList from "@/components/engineering/EnInfoList.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import EnWaiverCard from "@/components/engineering/EnWaiverCard.vue";
import { getEnGap, getEnHandover, getEnHandovers, postEnHandoverAction, requestEnWaiver } from "@/api/EngineeringAPIs";
import { completeness, downloadDoc, enPaths, EnRoles, faDigits, faNum, handoverStatuses, officeHoursUntil, runEn, stamp, unitName, unitsText, useEnDialog } from "@/composables/engineering";
import { fail } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";

// A handover of the engineering documents to the PMO. LOCK-B: while the required documents are missing the submit is refused and the server's reason is written in the red banner.
// An emergency permit (reason of at least 50 characters, a period of 1..72 hours counted from the release to execution) is decided by the CEO. The PMO accepts, returns or releases; the units acknowledge their notice.
const route = useRoute();
const id = computed(() => Number(route.params.id));
const h = ref<any>(null);
const rounds = ref<any[]>([]);
const gap = ref<any>(null);
const loading = ref(true);
const message = ref("");
const sideError = ref("");
const canInfo = hasRole(EnRoles.infoReaders);

async function load() {
  loading.value = true;
  const [d, e] = await getEnHandover(id.value);
  loading.value = false;
  if (e || !d) {
    message.value = e?.message || "تحویل پیدا نشد.";
    return fail(e || message.value);
  }
  message.value = "";
  h.value = d;
  sideError.value = "";
  const [r, re] = await getEnHandovers({ CaseId: d.caseId, PageSize: 50 });
  rounds.value = (r?.data || []).sort((a: any, b: any) => a.round - b.round);
  if (re) sideError.value = "فهرست دورهای تحویل خوانده نشد: " + (re.message || "");
  gap.value = null;
  if (d.standardGap?.id) {
    const [g, ge] = await getEnGap(d.standardGap.id);
    gap.value = g;
    if (ge) sideError.value = (sideError.value ? sideError.value + " " : "") + "گزارش تفاوت دو استاندارد خوانده نشد: " + (ge.message || "");
  }
}
onMounted(load);
watch(id, load);
const d = useEnDialog(load);
const act = (action: string, extra: any = {}) => postEnHandoverAction({ id: id.value, action, rowVersion: h.value.rowVersion, ...extra });
const reason = (title: string, action: string, okText: string) =>
  d.ask({ title, fields: [{ key: "n", label: "دلیل", type: "textarea", required: true, min: 15, max: 2000 }], run: (v) => act(action, { note: v.n }), okText });

const lines = computed(() => [...(h.value?.lines || [])].sort((a: any, b: any) => Number(b.forMyUnit) - Number(a.forMyUnit) || a.seq - b.seq));
const pct = computed(() => (h.value ? completeness(h.value.requiredCount, h.value.requiredCount - h.value.missingRequiredCount) : null));
const isLink = (p: string) => typeof p === "string" && p.startsWith("/");

function waiverDialog() {
  d.ask({
    title: "درخواست مجوز اضطرار",
    hint: "این درخواست فقط یک‌بار و پس از پذیرش PMO ثبت می‌شود؛ مدیرعامل تصمیم می‌گیرد و فقط می‌تواند ساعت‌ها را کمتر کند. بدهی مدارک هنگام ابلاغ به اجرا ثبت می‌شود. ساعت اداری: ۸ تا ۱۹، شنبه تا پنجشنبه؛ این مهلت فقط برای انسان است.",
    fields: [
      { key: "reason", label: "دلیل اضطرار", type: "textarea", required: true, min: 50, max: 2000 },
      { key: "hours", label: "مهلت تأمین مدرک (ساعت اداری)", type: "number", required: true, lo: 1, hi: 72, value: 72, hint: "مهلت تأمین مدرک از لحظهٔ ابلاغ به اجرا شمرده می‌شود؛ حداکثر ۷۲ ساعت اداری" },
    ],
    run: (v) => requestEnWaiver({ handoverId: id.value, reason: v.reason, debtHours: v.hours }),
    okText: "درخواست مجوز اضطرار برای مدیرعامل ثبت شد.",
  });
}
const editScope = (x: any) =>
  d.ask({
    title: "دامنه کار واحد " + unitName(x.unit ?? x.unitText),
    hint: "یادداشت دامنه واحد را PMO تا زمان پذیرش ویرایش می‌کند.",
    fields: [{ key: "s", label: "یادداشت دامنه", type: "textarea", max: 2000, value: x.scopeNote || "" }],
    run: (v) => postEnHandoverAction({ id: id.value, distributionId: x.id, action: "edit-scope", scopeNote: v.s, rowVersion: x.rowVersion }),
    okText: "یادداشت دامنه ذخیره شد.",
  });
const ack = (x: any) => runEn(() => postEnHandoverAction({ id: id.value, distributionId: x.id, action: "acknowledge", rowVersion: x.rowVersion }), "دریافت واحد تایید شد.", load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">تحویل مدارک <span v-if="h">{{ faDigits(h.number) }}</span></h1>
        <router-link :to="enPaths.handovers" class="btn btn-sm btn-outline-secondary">فهرست تحویل‌ها</router-link>
      </div>
      <p v-if="loading && !h" class="inv-muted">در حال خواندن…</p>
      <div v-if="message" class="en-banner red" role="alert">{{ message }}</div>
      <div v-if="sideError" class="en-banner amber" role="status">{{ sideError }}</div>

      <template v-if="h">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <EnChip :status="h.status" :text="h.statusText || handoverStatuses[h.status]" group="handover" />
          <span class="inv-badge info">دور {{ faNum(h.round, 0) }}</span>
          <span v-if="h.preparedByAgent" class="inv-badge info">آماده‌شده توسط ایجنت</span>
          <router-link :to="enPaths.case(h.caseId)">پرونده {{ faDigits(h.caseNumber) }}</router-link>
          <span class="small inv-muted">قرارداد {{ faDigits(h.contractCode || "") }} — {{ h.classificationText }}</span>
        </div>

        <div v-if="h.incompleteNotice" class="en-banner amber" data-test="en-incomplete-notice">{{ h.incompleteNotice }}</div>
        <div v-if="h.lockMessage && h.status === 3" class="en-banner red" data-test="en-lock-release">{{ h.lockMessage }}</div>
        <div v-if="h.status === 1" class="en-banner amber" data-test="en-handover-wait">منتظر پذیرش PMO است؛ این درخواست منقضی نمی‌شود و تا تصمیم گرفته شود هشدار می‌ماند.</div>
        <div v-if="h.previousReturnNumber" class="en-banner amber" data-test="en-previous-return">دور قبل ({{ faDigits(h.previousReturnNumber) }}) را PMO برگرداند: {{ h.previousReturnReason }}<span v-if="h.previousReturnByName || h.previousReturnAtText" class="small inv-muted"> — {{ h.previousReturnByName }}<span v-if="h.previousReturnAtText"> ({{ h.previousReturnAtText }})</span></span></div>
        <div v-if="h.returnReason" class="en-banner amber">برگشت‌خورده توسط {{ h.returnedByName }}: {{ h.returnReason }}</div>
        <div v-if="h.voidReason" class="en-banner red">ابطال‌شده: {{ h.voidReason }}</div>

        <div class="en-box">
          <div class="d-flex flex-wrap align-items-center gap-3">
            <span>{{ h.complete ? "مدارک الزامی کامل است" : `${faNum(h.missingRequiredCount, 0)} مدرک الزامی از ${faNum(h.requiredCount, 0)} کم است` }}</span>
            <div v-if="pct !== null" :class="['en-bar', h.complete ? '' : 'partial']" style="width: 14rem"><span :style="{ width: pct + '%' }"></span></div>
            <span class="small">پرونده پلتفرم: {{ h.platformDossierConfirmed ? "تاییدشده" : "تایید نشده" }}</span>
          </div>
        </div>

        <EnGapView v-if="gap" :gap="gap" link @changed="load" />

        <h2 class="h6 mt-3">مدارک</h2>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>ردیف</th><th>نوع</th><th>رشته</th><th>عنوان</th><th>الزامی</th><th>وضعیت</th><th>فایل / پیوند</th><th>واحدها</th></tr></thead>
            <tbody>
              <tr v-for="l in lines" :key="l.id" :class="l.forMyUnit ? 'table-info' : ''">
                <td>{{ faNum(l.seq, 0) }}</td><td>{{ l.documentType }}</td><td>{{ l.discipline }}</td><td>{{ l.title }}<span v-if="l.forMyUnit" class="inv-badge info ms-1">برای واحد شما</span></td>
                <td>{{ l.required ? "بله" : "خیر" }}</td>
                <td><EnChip :status="l.state" :text="l.stateText" group="line" /></td>
                <td>
                  <router-link v-if="l.linkPath && isLink(l.linkPath)" :to="l.linkPath">باز کردن در سیستم</router-link>
                  <a v-else-if="l.documentId" href="#" @click.prevent="downloadDoc(l.documentId, l.title)">دریافت فایل</a><span v-if="l.revision" class="small inv-muted"> — ویرایش مدرک {{ faDigits(String(l.revision)) }}</span>
                  <span v-if="!l.linkPath && !l.documentId" class="inv-muted">—</span>
                </td>
                <td class="small">{{ unitsText(l.units) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="mb-3">
          <EnAction :can="h.canSubmit" :reason="h.submitBlockedReason" :roles="EnRoles.writers" :confirm="h.complete ? `تحویل ${faDigits(h.number)} به PMO ارسال شود؟ پس از ارسال تحویل منتظر تصمیم PMO می‌ماند.` : 'این بسته ناقص است و برای بررسی به PMO می‌رود. ادامه می‌دهید؟'" label="ارسال به PMO" variant="primary" @go="runEn(() => act('submit'), 'تحویل به PMO ارسال شد.', load)" />
          <EnAction :can="h.canRequestWaiver" :reason="h.requestWaiverBlockedReason" :roles="EnRoles.manager" label="درخواست مجوز اضطرار" variant="outline-danger" @go="waiverDialog" />
          <EnAction :can="h.canRefresh" :reason="h.refreshBlockedReason" :roles="EnRoles.writers" :confirm="h.status === 3 ? `فقط ردیف‌های کسری تحویل ${faDigits(h.number)} با مدارکی که رسیده پر می‌شود (ردیفی که مدرک داشت تغییر نمی‌کند) و بسته دوباره برای پذیرش به PMO می‌رود؛ درخواست مجوز اضطرار بازِ این تحویل باطل می‌شود. ادامه می‌دهید؟` : `مدارک تحویل ${faDigits(h.number)} دوباره از پروندهٔ پلتفرم خوانده شود؟ وضعیت ردیف‌ها با پرونده هم‌خوان می‌شود.`" :label="h.status === 3 ? 'بازخوانی مدارک کسری و ارسال دوباره به PMO' : 'بازخوانی مدارک از پرونده پلتفرم'" variant="outline-secondary" @go="runEn(() => act('refresh'), h.status === 3 ? 'ردیف‌های کسری پر شد و بسته دوباره برای PMO ارسال شد.' : 'مدارک بازخوانی شد.', load)" />
          <EnAction :can="h.canAccept" :reason="h.acceptBlockedReason" :roles="EnRoles.pmoSide" :confirm="`تحویل ${faDigits(h.number)} پذیرفته شود؟ ` + (h.complete ? 'پس از پذیرش ساخت یادداشت ابلاغ واحدها آغاز می‌شود.' : 'این بسته ناقص است: پس از پذیرش، ابلاغ به اجرا قفل می‌ماند تا مدارک کسری برسد (مدیر مهندسی بسته را بازخوانی می‌کند و دوباره برای شما می‌فرستد) یا مدیرعامل مجوز اضطرار بدهد.')" label="پذیرش" variant="success" @go="runEn(() => act('accept'), 'تحویل پذیرفته شد.', load)" />
          <EnAction :can="h.canReturn" :reason="h.returnBlockedReason" :roles="EnRoles.pmoSide" label="برگشت" variant="outline-danger" @go="reason('برگشت تحویل', 'return', 'تحویل برگشت داده شد.')" />
          <EnAction :can="h.canRelease" :reason="h.releaseBlockedReason" :roles="EnRoles.pmoSide" :confirm="`تحویل ${faDigits(h.number)} برای واحدها منتشر شود؟ متن یادداشت ابلاغ هر واحد ثابت می‌شود و این کار برگشت‌پذیر نیست.`" label="انتشار برای واحدها" variant="success" @go="runEn(() => act('release'), 'تحویل منتشر شد.', load)" />
          <EnAction :can="h.canVoid" :reason="h.voidBlockedReason" :roles="EnRoles.writers" label="ابطال" variant="outline-secondary" @go="reason('ابطال تحویل', 'void', 'تحویل باطل شد.')" />
        </div>

        <h2 class="h6">مجوز اضطرار</h2>
        <p v-if="!h.waiver && !h.complete && h.status < 3" class="inv-muted" data-test="en-waiver-later">مجوز اضطرار فقط پس از پایان رفت‌وبرگشت مدارک با PMO (پذیرش PMO) درخواست می‌شود؛ در طول رفت‌وبرگشت از مدیرعامل تاییدی گرفته نمی‌شود.</p>
        <p v-else-if="!h.waiver" class="inv-muted">مجوز اضطراری درخواست نشده است.</p>
        <EnWaiverCard v-else :waiver="h.waiver" @changed="load" />
        <p v-if="h.waiver && h.waiver.status === 2 && h.status === 3 && !h.complete" class="small inv-muted" data-test="en-waiver-rejected-hint">پس از رد مدیرعامل، مدارک کسری را برسانید و بسته را بازخوانی کنید، یا با دلیل تازه دوباره درخواست کنید.</p>

        <h2 class="h6 mt-3">ابلاغ به واحدها</h2>
        <p class="small inv-muted">یادداشت ابلاغ هر واحد را PMO یا ایجنت سمت PMO می‌سازد و ابلاغ می‌کند؛ مهندسی مستقیم به واحدها ابلاغ نمی‌کند.</p>
        <div class="mb-2" data-test="en-dist-actions">
          <EnAction :can="h.canDraftDistribution" :reason="h.draftDistributionBlockedReason" :roles="EnRoles.pmo" :confirm="`برای تحویل ${faDigits(h.number)} یادداشت ابلاغ هر واحد ساخته شود؟ تا پیش از انتشار می‌توانید متن هر واحد را ویرایش کنید.`" label="ساخت یادداشت ابلاغ واحدها" variant="outline-primary" @go="runEn(() => act('draft-distribution'), 'یادداشت ابلاغ واحدها ساخته شد.', load)" />
          <EnAction :can="h.canDistribute" :reason="h.distributeBlockedReason" :roles="EnRoles.pmo" :confirm="`تحویل ${faDigits(h.number)} به واحدها ابلاغ شود؟ برای هر واحد کار دریافت مدارک ساخته می‌شود و این کار دوباره انجام نمی‌شود.`" label="ابلاغ به واحدها" variant="primary" @go="runEn(() => act('distribute'), 'ابلاغ به واحدها انجام شد.', load)" />
        </div>
        <p v-if="!h.distribution?.length" class="inv-muted">ابلاغی آماده نشده است؛ پس از پذیرش تحویل، PMO یا ایجنت سمت PMO یادداشت ابلاغ هر واحد را می‌سازد.</p>
        <div v-else class="table-responsive">
          <table class="table table-sm" data-test="en-distribution">
            <thead><tr><th>واحد</th><th>وضعیت</th><th>مدارک</th><th>دامنه کار</th><th>تایید دریافت</th><th></th></tr></thead>
            <tbody>
              <tr v-for="x in h.distribution" :key="x.id">
                <td>{{ unitName(x.unit ?? x.unitText) }}</td>
                <td><EnChip :status="x.acknowledged ? 1 : x.status" :text="x.acknowledged ? 'دریافت تایید شد' : x.statusText" group="dist" /></td>
                <td>{{ faNum(x.documentCount, 0) }}</td>
                <td class="small" style="max-width: 18rem">{{ x.scopeNote || "—" }}<EnAction :can="x.canEditScope" :reason="x.editScopeBlockedReason" :roles="EnRoles.pmo" label="ویرایش دامنه" variant="outline-secondary" @go="editScope(x)" /></td>
                <td class="small">{{ x.acknowledged ? x.acknowledgedByName + "، " + faDigits(x.acknowledgedAtText || "") : "—" }}</td>
                <td><EnAction v-if="!x.acknowledged" :can="x.canAcknowledge" :reason="x.acknowledgeBlockedReason" :confirm="`دریافت مدارک تحویل ${faDigits(h.number)} برای واحد ${unitName(x.unit ?? x.unitText)} تایید شود؟ این تایید برگشت‌پذیر نیست.`" label="تایید دریافت" variant="primary" @go="ack(x)" /></td>
              </tr>
            </tbody>
          </table>
        </div>

        <h2 class="h6 mt-3">بدهی مدارک</h2>
        <p v-if="!h.debts?.length" class="inv-muted">بدهی ثبت نشده است.</p>
        <ul v-else class="small" data-test="en-debts">
          <li v-for="b in h.debts" :key="b.id" :class="b.overdue && b.status === 0 ? 'text-danger' : ''">
            {{ b.handoverLineId ? b.documentType + " — " + b.title : "تایید کامل بودن پرونده پلتفرم" }} — مهلت {{ stamp(b.dueAt) }} <EnChip :status="b.status" :text="b.statusText" group="debt" />
            <span v-if="b.status === 0 && officeHoursUntil(b.dueAt) !== null"> ({{ (officeHoursUntil(b.dueAt) as number) >= 0 ? faNum(officeHoursUntil(b.dueAt), 0) + " ساعت اداری مانده" : "گذشته از موعد" }})</span>
            <router-link v-if="b.referenceCode && b.productId" class="ms-2" :to="enPaths.platform(b.productId, b.referenceCode)">پرونده استاندارد {{ b.referenceCode }}</router-link>
            <div v-if="b.status === 2 && b.voidReason" class="inv-muted">ابطال: {{ b.voidReason }}</div>
          </li>
        </ul>

        <template v-if="canInfo">
          <h2 class="h6 mt-3">پرسش از PMO</h2>
          <EnInfoList :handover-id="h.id" :case-id="h.caseId" :no-ask="h.status === 5" />
        </template>

        <h2 class="h6 mt-3">دورهای تحویل</h2>
        <ul>
          <li v-for="r in rounds" :key="r.id" :class="r.id === h.id ? 'fw-bold' : ''"><router-link :to="enPaths.handover(r.id)">دور {{ faNum(r.round, 0) }} — {{ faDigits(r.number) }}</router-link> <EnChip :status="r.status" :text="r.statusText" group="handover" /></li>
        </ul>
        <dl class="en-kv small">
          <dt>ارسال</dt><dd>{{ h.submittedByName ? h.submittedByName + "، " + faDigits(h.submittedAtText || "") : "—" }}</dd>
          <dt>پذیرش</dt><dd>{{ h.acceptedByName ? h.acceptedByName + "، " + faDigits(h.acceptedAtText || "") : "—" }}</dd>
          <dt>انتشار</dt><dd>{{ h.releasedByName ? h.releasedByName + "، " + faDigits(h.releasedAtText || "") : "—" }}</dd>
        </dl>
      </template>
    </div>
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
