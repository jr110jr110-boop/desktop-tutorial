<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import { getEnDeviations, getEnDossier, postEnDossierAction, postEnSupplyLine } from "@/api/EngineeringAPIs";
import { completeness, downloadDoc, enPaths, EnRoles, faDigits, faNum, runEn, uploadDoc, useEnDialog } from "@/composables/engineering";
import { fail } from "@/composables/inventory";

// One dossier of a platform (product and standard; the standard is in ?code=). Lines are grouped by discipline. Supplying a file or marking a line «کاربرد ندارد» is a request:
// for a REQUIRED line the mark opens a deviation (the head of engineering and the CEO decide) and the line itself does not change.
const route = useRoute();
const productId = computed(() => Number(route.params.productId));
const code = computed(() => (typeof route.query.code === "string" ? route.query.code : null));
const x = ref<any>(null);
const deviations = ref<Record<number, any>>({});
const loading = ref(true);
const message = ref("");

async function load() {
  loading.value = true;
  const [d, e] = await getEnDossier(productId.value, code.value);
  loading.value = false;
  if (e || !d) {
    message.value = e?.message || "پرونده پیدا نشد.";
    return fail(e || message.value);
  }
  message.value = "";
  x.value = d;
  const [dv] = await getEnDeviations({ ProductId: d.productId, PageSize: 500 });
  const map: Record<number, any> = {};
  (dv?.data || []).filter((v: any) => v.targetType === "EnPlatformDocument").forEach((v: any) => (map[v.targetId] = v));
  deviations.value = map;
}
onMounted(load);
watch([productId, code], load);
const d = useEnDialog(load);

const groups = computed(() => {
  const m = new Map<string, any[]>();
  (x.value?.lines || []).forEach((l: any) => m.set(l.discipline || "—", [...(m.get(l.discipline || "—") || []), l]));
  return [...m.entries()];
});
const act = (action: string, note?: string) => postEnDossierAction({ productId: productId.value, referenceCode: x.value.referenceCode, action, note, rowVersion: x.value.rowVersion });
function supply(l: any) {
  d.ask({
    title: l.state === 1 ? "جایگزینی فایل: " + l.title : "ثبت فایل: " + l.title,
    fields: [{ key: "file", label: "فایل مدرک", type: "file", required: true }, { key: "rev", label: "ویرایش مدرک", required: true, max: 20 }],
    run: async (v) => {
      const [id, e] = await uploadDoc(v.file);
      if (e) return [null, e];
      return postEnSupplyLine({ lineId: l.id, documentId: id, revision: v.rev, notApplicable: false, rowVersion: l.rowVersion });
    },
    okText: "فایل ثبت شد.",
  });
}
function notApplicable(l: any) {
  d.ask({
    title: "کاربرد ندارد: " + l.title,
    hint: l.required ? "این مدرک در استاندارد الزامی است؛ این کار یک درخواست انحراف از استاندارد می‌سازد و بند تا تصمیم مدیر مهندسی و مدیرعامل تغییر نمی‌کند." : "این مدرک الزامی نیست؛ بند بلافاصله «کاربرد ندارد» ثبت می‌شود.",
    fields: [{ key: "why", label: "دلیل", type: "textarea", required: true, min: 15, max: 2000 }],
    run: (v) => postEnSupplyLine({ lineId: l.id, notApplicable: true, notApplicableReason: v.why, rowVersion: l.rowVersion }),
    okText: l.required ? "درخواست انحراف ثبت شد؛ بند تغییر نکرد." : "بند «کاربرد ندارد» ثبت شد.",
  });
}
const reason = (title: string, action: string, label: string, okText: string) =>
  d.ask({ title, fields: [{ key: "n", label, type: "textarea", required: true, min: 15, max: 2000 }], run: (v) => act(action, v.n), okText });
function standardDialog() {
  d.ask({
    title: "ثبت استاندارد از دیتاشیت اصلی",
    fields: [{ key: "c", label: "کد استاندارد", required: true, max: 40 }, { key: "b", label: "مبنا (دیتاشیت و بند)", required: true, min: 5, max: 500 }],
    run: (v) => postEnDossierAction({ productId: productId.value, action: "native-standard", referenceCode: v.c, standardBasis: v.b }),
    okText: "استاندارد از دیتاشیت اصلی ثبت شد.",
  });
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">پرونده مدارک پلتفرم <span v-if="x">{{ x.productCode }}</span></h1>
        <router-link :to="enPaths.platforms" class="btn btn-sm btn-outline-secondary">فهرست پلتفرم‌ها</router-link>
      </div>
      <p v-if="loading && !x" class="inv-muted">در حال خواندن…</p>
      <div v-if="message" class="en-banner red" role="alert">{{ message }}</div>
      <template v-if="x">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <span class="fw-bold">{{ x.productTitle }}</span>
          <EnChip :status="x.status" :text="x.statusText" group="dossier" />
          <span class="inv-ltr">{{ x.referenceCode }}</span>
          <span v-if="x.stale" class="inv-badge warn">فهرست مرجع تغییر کرده؛ ارزیابی دوباره لازم است</span>
        </div>
        <p v-if="x.standardBasis" class="small">مبنای استاندارد: {{ x.standardBasis }}</p>
        <p v-if="x.otherDossiers?.length" class="small">پرونده‌های دیگر این محصول:
          <router-link v-for="o in x.otherDossiers" :key="o.id" :to="enPaths.platform(x.productId, o.referenceCode)" class="ms-2">{{ o.referenceCode }} ({{ o.statusText }})</router-link>
        </p>
        <div class="d-flex flex-wrap align-items-center gap-3 mb-3">
          <span>الزامی: {{ faNum(x.requiredCount, 0) }} — موجود: {{ faNum(x.availableCount, 0) }} — کم: {{ faNum(x.missingCount, 0) }}</span>
          <div v-if="completeness(x.requiredCount, x.availableCount) !== null" :class="['en-bar', x.missingCount ? 'partial' : '']" style="width: 14rem"><span :style="{ width: completeness(x.requiredCount, x.availableCount) + '%' }"></span></div>
        </div>
        <div v-if="x.tenderStageMissingCount" class="en-banner amber">{{ faNum(x.tenderStageMissingCount, 0) }} مدرک مرحله مناقصه هنوز موجود نیست.</div>
        <div v-if="x.status === 2" class="en-banner amber" data-test="en-dossier-wait">کامل‌شدن این پرونده منتظر تایید مدیر مهندسی است؛ درخواست منقضی نمی‌شود.</div>
        <div v-if="x.note" class="small mb-2">یادداشت: {{ x.note }}</div>

        <div class="mb-3">
          <EnAction :can="x.canRecordStandard" :reason="x.recordStandardBlockedReason" :roles="EnRoles.writers" label="ثبت استاندارد از دیتاشیت اصلی" variant="outline-primary" @go="standardDialog" />
          <EnAction :can="x.canAssess" :reason="x.assessBlockedReason" :roles="EnRoles.writers" :confirm="`پروندهٔ مدارک ${x.productCode} (استاندارد ${x.referenceCode}) دوباره با فهرست مرجع جاری ارزیابی شود؟ اگر فهرست یا مدارک تغییر کرده باشد، اعلام کامل‌شدن و تایید مدیر مهندسی برداشته می‌شود و پرونده به ناقص برمی‌گردد.`" label="ارزیابی دوباره" variant="outline-primary" @go="runEn(() => act('assess'), 'ارزیابی انجام شد.', load)" />
          <EnAction :can="x.canComplete" :reason="x.completeBlockedReason" :roles="EnRoles.writers" :confirm="`پروندهٔ مدارک ${x.productCode} (استاندارد ${x.referenceCode}) کامل اعلام شود؟ پس از آن منتظر تایید مدیر مهندسی می‌ماند.`" label="اعلام کامل‌شدن" variant="outline-primary" @go="runEn(() => act('complete'), 'کامل‌شدن اعلام شد.', load)" />
          <EnAction :can="x.canConfirm" :reason="x.confirmBlockedReason" :roles="EnRoles.manager" :confirm="`کامل‌بودن پروندهٔ مدارک ${x.productCode} (استاندارد ${x.referenceCode}) تایید شود؟ تحویل مدارک به PMO فقط با پروندهٔ تاییدشده بدون قفل می‌شود.`" label="تایید کامل بودن" variant="success" @go="runEn(() => act('confirm'), 'پرونده تایید شد.', load)" />
          <EnAction :can="x.canReturn" :reason="x.returnBlockedReason" :roles="EnRoles.manager" label="برگشت به ناقص" variant="outline-danger" @go="reason('برگشت به ناقص', 'return', 'دلیل برگشت', 'پرونده به ناقص برگشت.')" />
        </div>

        <div v-for="[disc, lines] in groups" :key="disc" class="mb-3">
          <h2 class="h6">{{ disc }}</h2>
          <div class="table-responsive">
            <table class="table table-sm">
              <thead><tr><th>ردیف</th><th>نوع</th><th>عنوان</th><th>الزامی</th><th>مرحله مناقصه</th><th>وضعیت</th><th>فایل / ویرایش</th><th></th></tr></thead>
              <tbody>
                <tr v-for="l in lines" :key="l.id">
                  <td>{{ faNum(l.seq, 0) }}</td><td>{{ l.documentType }}</td><td>{{ l.title }}</td>
                  <td>{{ l.required ? "بله" : "خیر" }}</td><td>{{ l.tenderStage ? "بله" : "خیر" }}</td>
                  <td>
                    <EnChip :status="l.state" :text="l.stateText" group="line" />
                    <div v-if="l.state === 2 && l.notApplicableReason" class="small inv-muted">{{ l.notApplicableReason }}</div>
                    <div v-if="deviations[l.id]" class="small" data-test="en-line-deviation">
                      انحراف <router-link :to="enPaths.deviation(deviations[l.id].id)">{{ faDigits(deviations[l.id].number) }}</router-link>:
                      <EnChip :status="deviations[l.id].status" :text="deviations[l.id].statusText" group="deviation" />
                    </div>
                  </td>
                  <td>
                    <template v-if="l.systemProven">اثبات‌شده توسط سیستم</template>
                    <template v-else-if="l.documentId"><a href="#" @click.prevent="downloadDoc(l.documentId, l.title)">دریافت فایل</a><span v-if="l.revision" class="small inv-muted"> — ویرایش مدرک {{ faDigits(String(l.revision)) }}</span></template>
                    <span v-else class="inv-muted">—</span>
                    <div v-if="l.suppliedByName" class="small inv-muted">{{ l.suppliedByName }} {{ faDigits(l.suppliedAtText || "") }}</div>
                  </td>
                  <td>
                    <template v-if="!l.systemProven">
                      <EnAction :can="x.canSupply" :reason="x.supplyBlockedReason" :roles="EnRoles.writers" :label="l.state === 1 ? 'جایگزینی فایل' : 'ثبت فایل'" variant="outline-primary" @go="supply(l)" />
                      <EnAction v-if="l.state !== 2" :can="x.canSupply" :reason="x.supplyBlockedReason" :roles="EnRoles.writers" label="کاربرد ندارد" variant="outline-secondary" @go="notApplicable(l)" />
                    </template>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>
    </div>
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
