<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import { getEnPlatforms, getEnPlatformSummary, postEnDossierAction } from "@/api/EngineeringAPIs";
import { completeness, dossierStatuses, enPaths, EnRoles, faDigits, faNum, useEnDialog } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";

// The platform dossiers: one row per product and standard, and the products without any dossier as «ارزیابی‌نشده» with the action to record their own standard.
const route = useRoute();
const filters = ref({ Status: route.query.status !== undefined ? Number(route.query.status) : (null as number | null), Contains: "" });
const list = usePagedList(getEnPlatforms, filters, 25);
const summary = ref<any>(null);
async function reload() {
  await list.load();
  const [s] = await getEnPlatformSummary();
  summary.value = s;
}
onMounted(reload);
const d = useEnDialog(reload);
function nativeStandard(p: any) {
  d.ask({
    title: "ثبت استاندارد بومی " + p.productCode,
    hint: "استاندارد دیتاشیت اصلی این پلتفرم را ثبت کنید؛ پرونده مدارک برای آن ساخته می‌شود.",
    fields: [
      { key: "code", label: "کد استاندارد", required: true, max: 40 },
      { key: "basis", label: "مبنای استاندارد (شماره دیتاشیت و بند)", required: true, min: 5, max: 500 },
    ],
    run: (v) => postEnDossierAction({ productId: p.productId, action: "native-standard", referenceCode: v.code, standardBasis: v.basis }),
    okText: "استاندارد بومی ثبت شد.",
  });
}
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">پرونده مدارک پلتفرم‌ها</h1></div>
      <div v-if="summary" class="en-tiles">
        <div class="en-tile"><div class="t">محصول</div><div class="v">{{ faNum(summary.products, 0) }}</div></div>
        <div :class="['en-tile', summary.notAssessed ? 'warn' : '']"><div class="t">ارزیابی‌نشده</div><div class="v">{{ faNum(summary.notAssessed, 0) }}</div></div>
        <div :class="['en-tile', summary.incomplete ? 'bad' : '']"><div class="t">ناقص</div><div class="v">{{ faNum(summary.incomplete, 0) }}</div></div>
        <div class="en-tile"><div class="t">منتظر تایید مدیر مهندسی</div><div class="v">{{ faNum(summary.completing, 0) }}</div></div>
        <div class="en-tile ok"><div class="t">کامل</div><div class="v">{{ faNum(summary.complete, 0) }}</div></div>
        <div :class="['en-tile', summary.stale ? 'warn' : '']"><div class="t">نیازمند ارزیابی دوباره</div><div class="v">{{ faNum(summary.stale, 0) }}</div></div>
      </div>
      <div v-if="summary && !summary.hasApprovedReference" class="en-banner amber">هنوز فهرست مدارک مرجع تاییدشده‌ای نیست؛ ارزیابی پلتفرم‌ها پس از تایید آن ممکن است. <router-link :to="enPaths.references">فهرست‌های مرجع</router-link></div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in dossierStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان محصول" style="max-width: 14rem" @keyup.enter="list.search" />
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!list.rows.value.length" class="inv-muted" data-test="en-empty">محصولی با این شرایط نیست.</p>
      <div v-else class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>محصول</th><th>استاندارد</th><th>وضعیت</th><th>الزامی</th><th>موجود</th><th>کم</th><th>پیشرفت</th><th></th></tr></thead>
          <tbody>
            <tr v-for="p in list.rows.value" :key="p.productId + '/' + (p.referenceCode || '')">
              <td><router-link :to="enPaths.platform(p.productId, p.referenceCode)">{{ p.productCode }}</router-link> <span class="small inv-muted">{{ p.productTitle }}</span></td>
              <td class="inv-ltr">{{ p.referenceCode || "—" }}</td>
              <td><EnChip :status="p.status" :text="p.statusText" group="dossier" /> <span v-if="p.stale" class="inv-badge warn">قدیمی</span></td>
              <td>{{ p.dossierId ? faNum(p.requiredCount, 0) : "—" }}</td>
              <td>{{ p.dossierId ? faNum(p.availableCount, 0) : "—" }}</td>
              <td>{{ p.dossierId ? faNum(p.missingCount, 0) : "—" }}</td>
              <td style="min-width: 8rem">
                <div v-if="p.dossierId && completeness(p.requiredCount, p.availableCount) !== null" :class="['en-bar', p.missingCount ? 'partial' : '']"><span :style="{ width: completeness(p.requiredCount, p.availableCount) + '%' }"></span></div>
                <span v-else class="inv-muted small">—</span>
              </td>
              <td>
                <EnAction v-if="p.nativeStandardMissing && !p.dossierId" :can="true" :roles="EnRoles.writers" label="ثبت استاندارد بومی" variant="outline-primary" @go="nativeStandard(p)" />
                <span v-if="p.tenderStageMissingCount" class="small text-danger">{{ faDigits(String(p.tenderStageMissingCount)) }} مدرک مرحله مناقصه کم است</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
