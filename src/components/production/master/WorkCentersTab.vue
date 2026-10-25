<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import RatesDialog from "./RatesDialog.vue";
import { getWorkCenters, saveWorkCenter } from "@/api/ProductionAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok, rial } from "@/composables/inventory";
import { percentText, workCenterKinds } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";
import { loadCalendars } from "./masterKit";

// Work centres: list + editor. The hourly rates are not part of the editor: finance sets them in the rates dialog, and the API sends them only to the roles that may see them.
const filters = ref({ Kind: null as number | null, IsActive: null as boolean | null, Contains: "" });
const list = usePagedList(getWorkCenters, filters);
const calendars = ref<any[]>([]);
const form = ref<any>(null);
const ratesFor = ref<any>(null);
const busy = ref(false);
const canEdit = hasRole(ProductionRoles.masterEditors);
const canSetRates = hasRole(ProductionRoles.rateSetters);
const seesRates = hasRole(ProductionRoles.rateViewers);

function edit(r: any | null) {
  form.value = r
    ? { id: r.id, code: r.code, title: r.title, kind: r.kind, calendarId: r.calendarId, shiftsPerDay: r.shiftsPerDay, hoursPerShift: r.hoursPerShift, efficiencyPercent: r.efficiencyPercent,
        utilisationPercent: r.utilisationPercent, parallelResources: r.parallelResources, costCenter: r.costCenter || "", queueDaysDefault: r.queueDaysDefault, isActive: r.isActive, rowVersion: r.rowVersion }
    : { id: null, code: "", title: "", kind: 1, calendarId: null, shiftsPerDay: 1, hoursPerShift: 8, efficiencyPercent: 100, utilisationPercent: 85, parallelResources: 1, costCenter: "", queueDaysDefault: 0, isActive: true, rowVersion: null };
}

async function save() {
  const f = form.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail("کد و عنوان مرکز کار را بنویسید.");
  busy.value = true;
  const [, error] = await saveWorkCenter({ ...f, code: String(f.code).trim(), title: String(f.title).trim(), costCenter: String(f.costCenter || "").trim() || null });
  busy.value = false;
  if (error) return fail(error);
  ok("مرکز کار ذخیره شد.");
  form.value = null;
  list.load();
}

onMounted(async () => {
  calendars.value = await loadCalendars();
  list.load();
});
defineExpose({ reload: list.load });
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div class="inv-filters">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keyup.enter="list.search" />
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in workCenterKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.IsActive" class="form-select form-select-sm"><option :value="null">فعال و غیرفعال</option><option :value="true">فعال</option><option :value="false">غیرفعال</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="edit(null)">مرکز کار جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead>
          <tr><th>کد</th><th>عنوان</th><th>نوع</th><th>تقویم</th><th>شیفت</th><th>ساعت شیفت</th><th>بازدهی</th><th>بهره‌وری</th><th>منابع موازی</th><th>مرکز هزینه</th><th>صف (روز)</th><th>ماشین‌ها</th><th v-if="seesRates">نرخ ماشین (ریال/ساعت)</th><th v-if="seesRates">نرخ نیروی کار (ریال/ساعت)</th><th>وضعیت</th><th></th></tr>
        </thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td :colspan="seesRates ? 16 : 14" class="text-center inv-muted">مرکز کاری ثبت نشده است.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td>{{ workCenterKinds[r.kind] }}</td><td>{{ r.calendarName || "-" }}</td><td>{{ r.shiftsPerDay }}</td><td>{{ r.hoursPerShift }}</td>
            <td>{{ percentText(r.efficiencyPercent) }}</td><td>{{ percentText(r.utilisationPercent) }}</td><td>{{ r.parallelResources }}</td><td>{{ r.costCenter || "-" }}</td><td>{{ r.queueDaysDefault }}</td><td>{{ r.machineCount }}</td>
            <td v-if="seesRates" class="fn-num">{{ r.machineRateRial === null || r.machineRateRial === undefined ? "-" : rial(r.machineRateRial) }}</td>
            <td v-if="seesRates" class="fn-num">{{ r.labourRateRial === null || r.labourRateRial === undefined ? "-" : rial(r.labourRateRial) }}</td>
            <td><span :class="r.isActive ? 'ok' : ''" class="inv-badge">{{ r.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td class="text-nowrap">
              <button v-if="canEdit" class="btn btn-sm btn-outline-primary me-1" @click="edit(r)">ویرایش</button>
              <button v-if="canSetRates" class="btn btn-sm btn-outline-secondary" @click="ratesFor = r">نرخ‌ها</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش مرکز کار" : "مرکز کار جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">کد</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in workCenterKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تقویم کاری</label><select v-model="form.calendarId" class="form-select form-select-sm"><option :value="null">تقویم پیش‌فرض</option><option v-for="c in calendars" :key="c.id" :value="c.id">{{ c.name }}</option></select></div>
          <div class="col-6 col-md-3"><label class="form-label small mb-0">شیفت در روز</label><input v-model.number="form.shiftsPerDay" class="form-control form-control-sm" type="number" min="1" /></div>
          <div class="col-6 col-md-3"><label class="form-label small mb-0">ساعت هر شیفت</label><input v-model.number="form.hoursPerShift" class="form-control form-control-sm" type="number" min="0" step="0.25" /></div>
          <div class="col-6 col-md-3"><label class="form-label small mb-0">بازدهی (٪)</label><input v-model.number="form.efficiencyPercent" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-6 col-md-3"><label class="form-label small mb-0">بهره‌وری (٪)</label><input v-model.number="form.utilisationPercent" class="form-control form-control-sm" type="number" min="0" /></div>
          <div class="col-6 col-md-4"><label class="form-label small mb-0">منابع موازی</label><input v-model.number="form.parallelResources" class="form-control form-control-sm" type="number" min="1" /></div>
          <div class="col-6 col-md-4"><label class="form-label small mb-0">مرکز هزینه</label><input v-model="form.costCenter" class="form-control form-control-sm" /></div>
          <div class="col-6 col-md-4"><label class="form-label small mb-0">صف پیش‌فرض (روز)</label><input v-model.number="form.queueDaysDefault" class="form-control form-control-sm" type="number" min="0" step="0.5" /></div>
          <div class="col-12"><label class="form-check"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> <span class="form-check-label">فعال</span></label></div>
        </div>
        <p class="inv-muted mt-2 mb-0">نرخ ساعتی ماشین و نیروی کار فقط توسط مالی در پنجره «نرخ‌ها» ثبت می‌شود.</p>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>

    <RatesDialog v-if="ratesFor" :center="ratesFor" @close="ratesFor = null" @saved="ratesFor = null; list.load()" />
  </div>
</template>
