<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import WeldLogPrint from "@/components/quality/print/WeldLogPrint.vue";
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import { getQualifications, getTestReports, getWeldLogs, getWpsList, saveWeldLog } from "@/api/QualityAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage, today } from "@/composables/inventory";
import { qualificationStates, useBackdateWindow, usePaged, vtResults } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Weld log: every weld joint with WPS, welder qualification, filler batch, temperatures, visual result and the NDT reports.
const route = useRoute();
const router = useRouter();
const filters = ref({ WorkOrderId: route.query.workOrderId ? Number(route.query.workOrderId) : null, SerialNumber: "" });
const list = usePaged(getWeldLogs, filters);
const canQc = hasRole(QualityRoles.inspectors);
// a weld log may be dated from MaxBackdateDays days back to today, never later (R1q D15, review M15: a weld restarts the continuity of the welder); the API refuses other dates
// with its own Persian text, which is shown
const { min: dateMin, max: dateMax, hint: dateHint, warning: dateWarning, load: loadWindow } = useBackdateWindow(false);
const form = ref<any>(null);
const wpsList = ref<any[]>([]);
const welders = ref<any[]>([]);
const reports = ref<any[]>([]);
const busy = ref(false);
async function doPrint() {
  await nextTick();
  printForm();
}
const weldLabel = () => (list.rows.value[0]?.workOrderCode || "") + (filters.value.SerialNumber ? " / " + filters.value.SerialNumber : "");

const blank = () => ({
  weldDate: today(), workOrderId: filters.value.WorkOrderId, serialNumber: "", jointNo: "", wpsId: null, qualificationId: null, fillerBatch: "", preheatC: null, interpassC: null, vt: 0, note: "", reportIds: [] as number[],
});
const nn = (v: any) => (v === "" || v === undefined ? null : v);

async function openForm() {
  loadWindow();
  form.value = blank();
  const [w] = await getWpsList({ Status: 1, PageSize: 200 });
  wpsList.value = rowsOf(w);
  const [q] = await getQualifications({ PageSize: 300 });
  welders.value = rowsOf(q).filter((x: any) => (x.kind === 1 || x.kind === 2) && (x.state === 0 || x.state === 1));
  loadReports();
}

async function loadReports() {
  reports.value = [];
  if (!form.value.workOrderId) return;
  const [r] = await getTestReports({ WorkOrderId: form.value.workOrderId, PageSize: 200 });
  reports.value = rowsOf(r);
}

const welderOptions = () => {
  const wps = wpsList.value.find((w) => w.id === form.value?.wpsId);
  return wps ? welders.value.filter((q) => q.method === wps.process) : welders.value;
};

async function save() {
  const f = form.value;
  busy.value = true;
  const [, error] = await saveWeldLog({ ...f, preheatC: nn(f.preheatC), interpassC: nn(f.interpassC) });
  busy.value = false;
  if (error) return fail(error);
  ok("ثبت شد.");
  form.value = null;
  list.load();
}

onMounted(list.load);
</script>

<template>
  <div class="inv-page ks-report">
    <WeldLogPrint :rows="list.rows.value" :label="weldLabel()" />
    <KsReportHeader title="گزارش جوشکاری (Weld Log)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">گزارش جوشکاری</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ گزارش</button>
          <router-link :to="{ name: 'QualityWps' }" class="btn btn-sm btn-outline-secondary">WPS و PQR</router-link>
          <button v-if="canQc" class="btn btn-sm btn-primary" @click="openForm">اتصال جوش جدید</button>
        </div>
      </div>

      <div v-if="form" class="border rounded p-2 mb-3 d-print-none">
        <div class="row g-2">
          <div class="col-md-2">
            <label class="form-label small mb-0">تاریخ جوشکاری</label>
            <input v-model="form.weldDate" :max="dateMax" :min="dateMin" class="form-control form-control-sm" type="date" />
            <span v-if="dateWarning(form.weldDate)" class="small text-danger">{{ dateWarning(form.weldDate) }}</span>
          </div>
          <div class="col-md-2"><label class="form-label small mb-0">دستور ساخت *</label><WorkOrderSelect v-model="form.workOrderId" @change="loadReports" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">سریال قطعه</label><input v-model="form.serialNumber" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">شماره اتصال *</label><input v-model="form.jointNo" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-4">
            <label class="form-label small mb-0">WPS (تاییدشده) *</label>
            <select v-model="form.wpsId" class="form-select form-select-sm" @change="form.qualificationId = null">
              <option :value="null">-</option><option v-for="w in wpsList" :key="w.id" :value="w.id">{{ w.number }}/{{ w.revision }} - {{ w.process }}</option>
            </select>
          </div>
          <div class="col-md-4">
            <label class="form-label small mb-0">جوشکار (گواهی معتبر هم‌فرایند) *</label>
            <select v-model="form.qualificationId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="q in welderOptions()" :key="q.id" :value="q.id">{{ q.personName }} {{ q.stampId }} - {{ q.method }} ({{ qualificationStates[q.state] }})</option>
            </select>
          </div>
          <div class="col-md-2"><label class="form-label small mb-0">بچ فیلر</label><input v-model="form.fillerBatch" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">پیش‌گرم (°C)</label><input v-model="form.preheatC" class="form-control form-control-sm inv-ltr" type="number" step="any" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">بین‌پاسی (°C)</label><input v-model="form.interpassC" class="form-control form-control-sm inv-ltr" type="number" step="any" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">بازرسی چشمی (VT)</label><select v-model="form.vt" class="form-select form-select-sm"><option v-for="(t, k) in vtResults" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-12">
            <label class="form-label small mb-0">گزارش‌های NDT مرتبط</label>
            <div class="d-flex flex-wrap gap-2">
              <label v-for="r in reports" :key="r.id" class="form-check small border rounded px-2 py-1"><input v-model="form.reportIds" :value="r.id" class="form-check-input ms-1" type="checkbox" /> <span class="inv-ltr">{{ r.number }}</span></label>
              <span v-if="!reports.length" class="inv-muted">گزارشی برای این دستور ساخت نیست.</span>
            </div>
          </div>
          <div class="col-md-8"><label class="form-label small mb-0">توضیح</label><input v-model="form.note" class="form-control form-control-sm" /></div>
          <div class="col-md-4 d-flex align-items-end gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
        <p class="inv-muted mt-2 mb-0">سرور بررسی می‌کند: WPS تاییدشده، هم‌فرایند بودن گواهی، معتبر بودن گواهی در تاریخ جوش (تداوم ۶ ماه) و دماهای پیش‌گرم و بین‌پاسی طبق WPS.</p>
        <p class="inv-muted small mt-1 mb-0">{{ dateHint }}</p>
      </div>

      <div class="inv-filters mb-3 d-print-none">
        <WorkOrderSelect v-model="filters.WorkOrderId" @change="list.search()" />
        <input v-model="filters.SerialNumber" class="form-control form-control-sm inv-ltr" placeholder="سریال" @keydown.enter="list.search()" />
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>تاریخ</th><th>دستور ساخت</th><th>سریال</th><th>اتصال</th><th>WPS</th><th>جوشکار</th><th>مهر</th><th>فرایند</th><th>بچ فیلر</th><th>پیش‌گرم</th><th>بین‌پاسی</th><th>VT</th><th>گزارش NDT</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="13" class="text-center inv-muted">ثبتی نیست.</td></tr>
            <tr v-for="l in list.rows.value" :key="l.id">
              <td>{{ jalali(l.weldDate) }}</td><td class="inv-ltr">{{ l.workOrderCode }}</td><td class="inv-ltr">{{ l.serialNumber }}</td><td class="inv-ltr">{{ l.jointNo }}</td><td class="inv-ltr">{{ l.wpsNumber }}</td>
              <td>{{ l.welderName }}</td><td class="inv-ltr">{{ l.welderStamp }}</td><td class="inv-ltr">{{ l.process }}</td><td class="inv-ltr">{{ l.fillerBatch }}</td><td>{{ l.preheatC }}</td><td>{{ l.interpassC }}</td>
              <td><span :class="{ ok: l.vt === 1, bad: l.vt === 2 }" class="inv-badge">{{ vtResults[l.vt] }}</span></td>
              <td class="inv-ltr"><a v-for="(n, i) in l.reportNumbers" :key="n" href="#" class="me-1" @click.prevent="router.push({ name: 'QualityReportEdit', params: { id: l.reportIds[i] } })">{{ n }}</a></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
