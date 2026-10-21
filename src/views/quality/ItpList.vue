<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import QPager from "@/components/quality/QPager.vue";
import { ensureItp, getItps, getItpTemplate, getItpTemplates, getPendingPoints } from "@/api/QualityAPIs";
import { fail, jalali, printPage } from "@/composables/inventory";
import { activityPoints, activityStatuses, itpStatuses, pointsText, productTypes, testTypes, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";
import KsReportHeader from "@/components/common/KsReportHeader.vue";

// Inspection and test plans (WI-QC-004): plans per work order, the hold / witness points that wait, and the templates.
const route = useRoute();
const router = useRouter();
const tab = ref<"itps" | "pending" | "templates">("itps");
const canCreate = hasRole(QualityRoles.planners);

const filters = ref({ Status: null as number | null, Contains: "", WorkOrderId: route.query.workOrderId ? Number(route.query.workOrderId) : null });
const itps = usePaged(getItps, filters);
const pendingFilters = ref({ WorkOrderId: filters.value.WorkOrderId });
const pending = usePaged(getPendingPoints, pendingFilters);
const templateFilters = ref({ ProductType: null as number | null, Contains: "" });
const templates = usePaged(getItpTemplates, templateFilters);
const template = ref<any>(null);

const create = ref({ workOrderId: null as number | null, productType: null as number | null, templateId: null as number | null, abroad: false });
const busy = ref(false);
const showCreate = ref(false);
const templateOptions = computed(() => templates.rows.value);

async function openTemplate(id: number) {
  const [data, error] = await getItpTemplate(id);
  if (error) return fail(error);
  template.value = data;
}

async function make() {
  if (!create.value.workOrderId) return fail("دستور ساخت را انتخاب کنید.");
  busy.value = true;
  const [data, error] = await ensureItp({ ...create.value });
  busy.value = false;
  if (error) return fail(error);
  router.push({ name: "QualityItpEdit", params: { id: data.id } });
}

function setTab(t: "itps" | "pending" | "templates") {
  tab.value = t;
  if (t === "pending") pending.load();
  if (t === "templates") templates.load();
}

onMounted(() => {
  itps.load();
  templates.load();
});
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="برنامه‌های بازرسی و آزمون (ITP)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">برنامه بازرسی و آزمون (ITP)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canCreate" class="btn btn-sm btn-primary" @click="showCreate = !showCreate">ITP جدید برای دستور ساخت</button>
        </div>
      </div>

      <div v-if="showCreate" class="border rounded p-2 mb-3 d-print-none">
        <p class="inv-muted mb-2">سیستم ITP را از قالب نوع محصول پیش‌نویس می‌کند؛ اگر دستور ساخت قبلاً ITP دارد همان باز می‌شود.</p>
        <div class="row g-2 align-items-end">
          <div class="col-md-3"><label class="form-label small mb-0">دستور ساخت *</label><WorkOrderSelect v-model="create.workOrderId" /></div>
          <div class="col-md-3">
            <label class="form-label small mb-0">نوع محصول (اختیاری)</label>
            <select v-model="create.productType" class="form-select form-select-sm">
              <option :value="null">از تجهیز دستور ساخت</option>
              <option v-for="(t, k) in productTypes" :key="k" :value="Number(k)">{{ t }}</option>
            </select>
          </div>
          <div class="col-md-3">
            <label class="form-label small mb-0">قالب (اختیاری)</label>
            <select v-model="create.templateId" class="form-select form-select-sm">
              <option :value="null">قالب فعال نوع محصول</option>
              <option v-for="t in templateOptions" :key="t.id" :value="t.id">{{ t.code }} - {{ t.title }}</option>
            </select>
          </div>
          <div class="col-md-2"><label class="form-check small"><input v-model="create.abroad" class="form-check-input" type="checkbox" /> کارفرما / TPI خارج از ایران (مهلت اطلاع طولانی‌تر)</label></div>
          <div class="col-md-1"><button :disabled="busy" class="btn btn-sm btn-primary w-100" @click="make">ایجاد</button></div>
        </div>
      </div>

      <ul class="nav nav-tabs mb-3 d-print-none">
        <li class="nav-item"><button :class="{ active: tab === 'itps' }" class="nav-link" @click="setTab('itps')">برنامه‌ها</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'pending' }" class="nav-link" @click="setTab('pending')">نقطه‌های H و W در انتظار</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'templates' }" class="nav-link" @click="setTab('templates')">قالب‌ها</button></li>
      </ul>

      <template v-if="tab === 'itps'">
        <div class="inv-filters mb-3 d-print-none">
          <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا دستور ساخت" @keydown.enter="itps.search()" />
          <select v-model="filters.Status" class="form-select form-select-sm" @change="itps.search()">
            <option :value="null">همه وضعیت‌ها</option>
            <option v-for="(t, k) in itpStatuses" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
          <WorkOrderSelect v-model="filters.WorkOrderId" @change="itps.search()" />
          <button class="btn btn-sm btn-secondary" @click="itps.search()">جستجو</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>دستور ساخت</th><th>قرارداد</th><th>بازنگری</th><th>وضعیت</th><th>فعالیت‌ها</th><th>انجام‌شده</th></tr></thead>
          <tbody>
            <tr v-if="!itps.rows.value.length"><td colspan="7" class="text-center inv-muted">برنامه‌ای نیست.</td></tr>
            <tr v-for="r in itps.rows.value" :key="r.id" role="button" @click="router.push({ name: 'QualityItpEdit', params: { id: r.id } })">
              <td class="inv-ltr">{{ r.number }}</td>
              <td class="inv-ltr">{{ r.workOrderCode }}</td>
              <td class="inv-ltr">{{ r.contractCode }}</td>
              <td>{{ r.revision }}</td>
              <td><span :class="{ ok: r.status === 2 || r.status === 3, warn: r.status === 1, info: r.status === 0 }" class="inv-badge">{{ itpStatuses[r.status] }}</span> <span v-if="r.autoDrafted" class="inv-badge info">پیش‌نویس سیستم</span></td>
              <td>{{ r.activityCount }}</td>
              <td>{{ r.doneCount }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="itps.page.value" :total="itps.total.value" @go="itps.go" />
      </template>

      <template v-if="tab === 'pending'">
        <p class="inv-muted">نقطه‌های توقف (H) و شاهد (W) فعالیت‌هایی که هنوز انجام نشده‌اند؛ مهلت شاهد = تاریخ اعلام + روزهای اطلاع ITP.</p>
        <table class="table table-sm table-hover">
          <thead><tr><th>ITP</th><th>دستور ساخت</th><th>ردیف</th><th>فعالیت</th><th>نقطه‌ها</th><th>وضعیت</th><th>تاریخ برنامه</th><th>مهلت شاهد</th></tr></thead>
          <tbody>
            <tr v-if="!pending.rows.value.length"><td colspan="8" class="text-center inv-muted">موردی نیست.</td></tr>
            <tr v-for="p in pending.rows.value" :key="p.activityId" role="button" @click="router.push({ name: 'QualityItpEdit', params: { id: p.itpId } })">
              <td class="inv-ltr">{{ p.itpNumber }}</td>
              <td class="inv-ltr">{{ p.workOrderCode }}</td>
              <td class="inv-ltr">{{ p.itemNo }}</td>
              <td>{{ p.activity }}</td>
              <td class="inv-ltr">{{ pointsText(p.points) }}</td>
              <td>{{ activityStatuses[p.status] }}</td>
              <td>{{ jalali(p.plannedDate) }}</td>
              <td>{{ jalali(p.witnessDeadline) }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="pending.page.value" :total="pending.total.value" @go="pending.go" />
      </template>

      <template v-if="tab === 'templates'">
        <div class="inv-filters mb-3 d-print-none">
          <input v-model="templateFilters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keydown.enter="templates.search()" />
          <select v-model="templateFilters.ProductType" class="form-select form-select-sm" @change="templates.search()">
            <option :value="null">همه محصولات</option>
            <option v-for="(t, k) in productTypes" :key="k" :value="Number(k)">{{ t }}</option>
          </select>
          <button class="btn btn-sm btn-secondary" @click="templates.search()">جستجو</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>عنوان</th><th>محصول</th><th>استانداردها</th><th>بازنگری</th><th>فعالیت</th><th>فعال</th></tr></thead>
          <tbody>
            <tr v-if="!templates.rows.value.length"><td colspan="7" class="text-center inv-muted">قالبی نیست.</td></tr>
            <tr v-for="t in templates.rows.value" :key="t.id" role="button" @click="openTemplate(t.id)">
              <td class="inv-ltr">{{ t.code }}</td><td>{{ t.title }}</td><td>{{ productTypes[t.productType] }}</td><td class="inv-ltr">{{ t.applicableCodes }}</td>
              <td>{{ t.revision }}</td><td>{{ t.activityCount }}</td><td>{{ t.isActive ? "بله" : "خیر" }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="templates.page.value" :total="templates.total.value" @go="templates.go" />
        <div v-if="template" class="border rounded p-2 mt-3">
          <div class="d-flex justify-content-between"><b>{{ template.code }} - {{ template.title }}</b><button class="btn btn-sm btn-outline-secondary" @click="template = null">بستن</button></div>
          <div class="table-responsive">
            <table class="table table-sm">
              <thead><tr><th>ردیف</th><th>مرحله</th><th>فعالیت</th><th>نوع</th><th>مرجع پذیرش</th><th>نقطه‌ها</th><th>مدرک</th><th>پارامترها</th></tr></thead>
              <tbody>
                <tr v-for="a in template.activities" :key="a.id">
                  <td class="inv-ltr">{{ a.itemNo || a.seq }}</td><td>{{ a.stage }}</td><td>{{ a.activity }}</td><td>{{ testTypes[a.testType] }}</td>
                  <td class="inv-ltr">{{ a.acceptanceReference }}</td><td class="inv-ltr">{{ activityPoints(a) }}</td><td>{{ a.documentProduced }}</td>
                  <td>
                    <div v-for="p in a.parameters" :key="p.id" class="small">
                      {{ p.name }} <span class="inv-ltr">[{{ p.min ?? "" }} .. {{ p.max ?? "" }}] {{ p.unit }}</span>
                      <span v-if="p.requiresLimits" class="inv-badge warn">حد را مهندسی تعیین کند</span>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
