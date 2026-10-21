<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import { getCalibrationIntervals, getEquipmentDue, getEquipmentList, getOotCases, saveCalibrationInterval, saveEquipment } from "@/api/QualityAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { equipmentBadge, equipmentStatuses, equipmentTypes, ootStatuses, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Measuring equipment register (WI-QC-001, ISO 10012 / ISO 9001 7.1.5): register, due list, out-of-tolerance cases, default intervals.
const router = useRouter();
const tab = ref<"register" | "due" | "oot" | "intervals">("register");
const canQc = hasRole(QualityRoles.inspectors);
const canManage = hasRole(QualityRoles.managers);

const filters = ref({ Type: null as number | null, Status: null as number | null, Contains: "" });
const register = usePaged(getEquipmentList, filters);
const dueFilters = ref({ Days: 30 });
const due = usePaged(getEquipmentDue, dueFilters);
const ootFilters = ref({ Status: null as number | null });
const oot = usePaged(getOotCases, ootFilters);
const intervals = ref<any[]>([]);
const form = ref<any>(null);
const busy = ref(false);

function blank() {
  return { id: null, name: "", type: 1, maker: "", model: "", serialNumber: "", ownerUnit: "", location: "", range: "", accuracy: "", requiresCalibration: true, intervalMonths: null, note: "" };
}

async function create() {
  if (!form.value.name.trim()) return fail("نام تجهیز را وارد کنید.");
  busy.value = true;
  const [data, error] = await saveEquipment({ ...form.value, intervalMonths: form.value.intervalMonths || null });
  busy.value = false;
  if (error) return fail(error);
  ok("تجهیز ثبت شد.");
  form.value = null;
  router.push({ name: "QualityEquipmentEdit", params: { id: data.id } });
}

async function loadIntervals() {
  const [data, error] = await getCalibrationIntervals();
  if (error) return fail(error);
  intervals.value = (data || []).map((i: any) => ({ ...i, months: i.months }));
}

async function saveInterval(i: any) {
  const [data, error] = await saveCalibrationInterval(i.equipmentType, Number(i.months), i.source);
  if (error) return fail(error);
  intervals.value = data || intervals.value;
  ok("ذخیره شد.");
}

function setTab(t: "register" | "due" | "oot" | "intervals") {
  tab.value = t;
  if (t === "due") due.load();
  if (t === "oot") oot.load();
  if (t === "intervals") loadIntervals();
}

function printLabels(rows: any[]) {
  const ids = rows.filter((r) => r.requiresCalibration !== false).map((r) => r.id);
  if (!ids.length) return fail("تجهیزی برای چاپ برچسب نیست.");
  window.open(router.resolve({ name: "QualityCalibrationLabels", query: { ids: ids.join(",") } }).href, "_blank");
}

onMounted(register.load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="تجهیزات اندازه‌گیری و کالیبراسیون" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">تجهیزات اندازه‌گیری و کالیبراسیون</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button class="btn btn-sm btn-outline-secondary" @click="printLabels(tab === 'due' ? due.rows.value : register.rows.value)">چاپ برچسب‌های صفحه</button>
          <button v-if="canQc" class="btn btn-sm btn-primary" @click="form = blank()">تجهیز جدید</button>
        </div>
      </div>

      <div v-if="form" class="border rounded p-2 mb-3 d-print-none">
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">نام *</label><input v-model="form.name" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">نوع</label><select v-model="form.type" class="form-select form-select-sm"><option v-for="(t, k) in equipmentTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">سازنده</label><input v-model="form.maker" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مدل</label><input v-model="form.model" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">شماره سریال</label><input v-model="form.serialNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">واحد مالک</label><input v-model="form.ownerUnit" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">محل</label><input v-model="form.location" class="form-control form-control-sm" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">محدوده اندازه‌گیری</label><input v-model="form.range" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">دقت / تفکیک</label><input v-model="form.accuracy" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">فاصله کالیبراسیون (ماه)</label><input v-model.number="form.intervalMonths" class="form-control form-control-sm" placeholder="پیش‌فرض نوع" type="number" min="1" /></div>
          <div class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="form.requiresCalibration" class="form-check-input" type="checkbox" /> نیاز به کالیبراسیون دارد</label></div>
          <div class="col-md-4 d-flex align-items-end gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
        <p class="inv-muted mt-2 mb-0">کد INN-IST-nnn را سیستم می‌دهد.</p>
      </div>

      <ul class="nav nav-tabs mb-3 d-print-none">
        <li class="nav-item"><button :class="{ active: tab === 'register' }" class="nav-link" @click="setTab('register')">دفتر تجهیزات</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'due' }" class="nav-link" @click="setTab('due')">سررسید و گذشته</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'oot' }" class="nav-link" @click="setTab('oot')">خارج از تلرانس</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'intervals' }" class="nav-link" @click="setTab('intervals')">فاصله‌های پیش‌فرض</button></li>
      </ul>

      <template v-if="tab === 'register' || tab === 'due'">
        <div v-if="tab === 'register'" class="inv-filters mb-3 d-print-none">
          <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد، نام یا سریال" @keydown.enter="register.search()" />
          <select v-model="filters.Type" class="form-select form-select-sm" @change="register.search()"><option :value="null">همه انواع</option><option v-for="(t, k) in equipmentTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
          <select v-model="filters.Status" class="form-select form-select-sm" @change="register.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in equipmentStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <button class="btn btn-sm btn-secondary" @click="register.search()">جستجو</button>
        </div>
        <div v-else class="inv-filters mb-3 d-print-none">
          <label class="small">سررسید تا <input v-model.number="dueFilters.Days" class="form-control form-control-sm d-inline-block" style="width: 80px" type="number" min="1" /> روز آینده و گذشته‌ها</label>
          <button class="btn btn-sm btn-secondary" @click="due.search()">نمایش</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>کد</th><th>نام</th><th>نوع</th><th>سریال</th><th>محل</th><th>آخرین کالیبراسیون</th><th>سررسید</th><th>روز مانده</th><th>وضعیت</th></tr></thead>
          <tbody>
            <template v-for="src in [tab === 'due' ? due : register]" :key="tab">
              <tr v-if="!src.rows.value.length"><td colspan="9" class="text-center inv-muted">تجهیزی نیست.</td></tr>
              <tr v-for="e in src.rows.value" :key="e.id" role="button" @click="router.push({ name: 'QualityEquipmentEdit', params: { id: e.id } })">
                <td class="inv-ltr">{{ e.code }}</td><td>{{ e.name }}</td><td>{{ equipmentTypes[e.type] }}</td><td class="inv-ltr">{{ e.serialNumber }}</td><td>{{ e.location }}</td>
                <td>{{ jalali(e.lastCalibrated) }}</td><td>{{ jalali(e.nextDue) }}</td><td>{{ e.daysToDue ?? "" }}</td>
                <td><span :class="equipmentBadge(e.status)" class="inv-badge">{{ equipmentStatuses[e.status] }}</span></td>
              </tr>
            </template>
          </tbody>
        </table>
        <QPager v-if="tab === 'register'" :page="register.page.value" :total="register.total.value" @go="register.go" />
        <QPager v-else :page="due.page.value" :total="due.total.value" @go="due.go" />
      </template>

      <template v-if="tab === 'oot'">
        <div class="inv-filters mb-3 d-print-none">
          <select v-model="ootFilters.Status" class="form-select form-select-sm" @change="oot.search()"><option :value="null">همه</option><option v-for="(t, k) in ootStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        </div>
        <p class="inv-muted">وقتی نتیجه «همان‌طور که یافت شد» در کالیبراسیون مردود باشد، سیستم سوابق بازرسی و آزمون استفاده‌کننده را فهرست می‌کند و برای هر کدام ارزیابی لازم است (ISO 7.1.5.2).</p>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تجهیز</th><th>از تاریخ</th><th>تا تاریخ</th><th>وضعیت</th><th>سوابق</th><th>در انتظار ارزیابی</th></tr></thead>
          <tbody>
            <tr v-if="!oot.rows.value.length"><td colspan="7" class="text-center inv-muted">موردی نیست.</td></tr>
            <tr v-for="c in oot.rows.value" :key="c.id" role="button" @click="router.push({ name: 'QualityOotCase', params: { id: c.id } })">
              <td class="inv-ltr">{{ c.number }}</td><td class="inv-ltr">{{ c.equipmentCode }}</td><td>{{ jalali(c.fromDate) }}</td><td>{{ jalali(c.toDate) }}</td>
              <td><span :class="c.status === 0 ? 'bad' : 'ok'" class="inv-badge">{{ ootStatuses[c.status] }}</span></td><td>{{ c.itemCount }}</td><td>{{ c.pendingCount }}</td>
            </tr>
          </tbody>
        </table>
        <QPager :page="oot.page.value" :total="oot.total.value" @go="oot.go" />
      </template>

      <template v-if="tab === 'intervals'">
        <p class="inv-muted">فاصله پیش‌فرض کالیبراسیون هر نوع تجهیز (ماه)؛ تصمیم موقت شرکت و قابل تغییر توسط مدیر QC / QA.</p>
        <table class="table table-sm">
          <thead><tr><th>نوع تجهیز</th><th>ماه</th><th>مبنا</th><th></th></tr></thead>
          <tbody>
            <tr v-for="i in intervals" :key="i.equipmentType">
              <td>{{ equipmentTypes[i.equipmentType] }}</td>
              <td><input v-model.number="i.months" :disabled="!canManage" class="form-control form-control-sm" style="width: 90px" type="number" min="1" max="60" /></td>
              <td><input v-model="i.source" :disabled="!canManage" class="form-control form-control-sm" /></td>
              <td><button v-if="canManage" class="btn btn-sm btn-primary" @click="saveInterval(i)">ذخیره</button></td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
