<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import WpsPrint from "@/components/quality/print/WpsPrint.vue";
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import { getPqrs, getWps, getWpsList, pqrAction, savePqr, saveWps, wpsAction } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { usePaged, wpsStatuses } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// WPS (welding procedure specification) and PQR (procedure qualification record), ASME IX.
// A WPS needs at least one approved PQR and two different approvers: QC (quality) and engineering (technical).
const tab = ref<"wps" | "pqr">("wps");
const filters = ref({ Status: null as number | null, Contains: "" });
const list = usePaged(getWpsList, filters);
const pqrs = ref<any[]>([]);
const wps = ref<any>(null);
const pqrForm = ref<any>(null);
const busy = ref(false);
const printPqr = ref<any>(null);
async function doPrint(pqr: any = null) {
  printPqr.value = pqr;
  await nextTick();
  printForm();
}
const canPlan = hasRole(QualityRoles.planners);
const canQc = hasRole(QualityRoles.inspectors);
const canEng = hasRole(QualityRoles.engineering);
const editing = ref(false);

const blankWps = () => ({
  id: null, number: "", revision: "0", process: "", basePNo: "", fillerFNo: "", fillerANo: "", thicknessMin: null, thicknessMax: null, positions: "",
  preheatMinC: null, interpassMaxC: null, pwht: "", shieldingGas: "", note: "", pqrIds: [] as number[], status: 0,
});
const nn = (v: any) => (v === "" || v === undefined ? null : v);

async function loadPqrs() {
  const [data, error] = await getPqrs();
  if (error) return fail(error);
  pqrs.value = data || [];
}

async function open(id: number) {
  const [data, error] = await getWps(id);
  if (error) return fail(error);
  wps.value = { ...data, pqrIds: (data.pqrs || []).map((p: any) => p.id) };
  editing.value = false;
}

async function save() {
  const w = wps.value;
  busy.value = true;
  const [data, error] = await saveWps({
    ...w,
    thicknessMin: nn(w.thicknessMin), thicknessMax: nn(w.thicknessMax), preheatMinC: nn(w.preheatMinC), interpassMaxC: nn(w.interpassMaxC),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد؛ تاییدهای قبلی پاک شد.");
  wps.value = { ...data, pqrIds: (data.pqrs || []).map((p: any) => p.id) };
  editing.value = false;
  list.load();
}

async function act(action: string) {
  busy.value = true;
  const [data, error] = await wpsAction(wps.value.id, action);
  busy.value = false;
  if (error) return fail(error);
  wps.value = { ...data, pqrIds: (data.pqrs || []).map((p: any) => p.id) };
  ok("انجام شد.");
  list.load();
}

async function savePqrForm() {
  busy.value = true;
  const [, error] = await savePqr(pqrForm.value);
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  pqrForm.value = null;
  loadPqrs();
}

async function approvePqr(p: any) {
  const [, error] = await pqrAction(p.id, "approve");
  if (error) return fail(error);
  ok("PQR تایید شد.");
  loadPqrs();
}

function setTab(t: "wps" | "pqr") {
  tab.value = t;
  if (t === "pqr") loadPqrs();
}

onMounted(() => {
  list.load();
  loadPqrs();
});
</script>

<template>
  <div class="inv-page">
    <WpsPrint v-if="printPqr" :pqr="printPqr" />
    <WpsPrint v-else-if="wps && wps.id" :wps="wps" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">WPS و PQR</h1>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'QualityPersonnel' }" class="btn btn-sm btn-outline-secondary">پرسنل و گواهی‌ها</router-link>
          <router-link :to="{ name: 'QualityWeldLogs' }" class="btn btn-sm btn-outline-secondary">گزارش جوشکاری</router-link>
          <button v-if="canPlan && tab === 'wps'" class="btn btn-sm btn-primary" @click="(wps = blankWps()), (editing = true)">WPS جدید</button>
          <button v-if="canPlan && tab === 'pqr'" class="btn btn-sm btn-primary" @click="pqrForm = { id: null, number: '', codeEdition: '', process: '', resultsDocumentId: null, note: '' }">PQR جدید</button>
        </div>
      </div>
      <ul class="nav nav-tabs mb-3">
        <li class="nav-item"><button :class="{ active: tab === 'wps' }" class="nav-link" @click="setTab('wps')">WPS</button></li>
        <li class="nav-item"><button :class="{ active: tab === 'pqr' }" class="nav-link" @click="setTab('pqr')">PQR</button></li>
      </ul>

      <template v-if="tab === 'wps'">
        <div class="inv-filters mb-3">
          <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا فرایند" @keydown.enter="list.search()" />
          <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in wpsStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
        </div>
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>بازنگری</th><th>فرایند</th><th>P-No پایه</th><th>PQR</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="6" class="text-center inv-muted">WPS ای نیست.</td></tr>
            <tr v-for="w in list.rows.value" :key="w.id" role="button" @click="open(w.id)">
              <td class="inv-ltr">{{ w.number }}</td><td class="inv-ltr">{{ w.revision }}</td><td class="inv-ltr">{{ w.process }}</td><td class="inv-ltr">{{ w.basePNo }}</td><td>{{ w.pqrCount }}</td>
              <td><span :class="{ ok: w.status === 1, bad: w.status === 2 }" class="inv-badge">{{ wpsStatuses[w.status] }}</span></td>
            </tr>
          </tbody>
        </table>
        <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

        <div v-if="wps" class="border rounded p-3 mt-3">
          <div class="inv-toolbar">
            <b>{{ wps.id ? "WPS " + wps.number + " / " + wps.revision : "WPS جدید" }}</b>
            <div class="d-flex flex-wrap gap-2">
              <span v-if="wps.id" :class="{ ok: wps.status === 1, bad: wps.status === 2 }" class="inv-badge">{{ wpsStatuses[wps.status] }}</span>
              <button v-if="wps.id" class="btn btn-sm btn-outline-secondary" @click="doPrint()">چاپ WPS</button>
              <button v-if="canPlan && wps.id && wps.status === 0 && !editing" class="btn btn-sm btn-outline-primary" @click="editing = true">ویرایش</button>
              <button v-if="canPlan && wps.id && wps.status !== 2" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('obsolete')">منسوخ کردن</button>
              <button class="btn btn-sm btn-outline-secondary" @click="wps = null">بستن</button>
            </div>
          </div>
          <div class="row g-2">
            <div class="col-md-2"><label class="form-label small mb-0">شماره *</label><input v-model="wps.number" :disabled="!editing" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-1"><label class="form-label small mb-0">بازنگری</label><input v-model="wps.revision" :disabled="!editing" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">فرایند *</label><input v-model="wps.process" :disabled="!editing" class="form-control form-control-sm inv-ltr" placeholder="GTAW" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">P-No پایه</label><input v-model="wps.basePNo" :disabled="!editing" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">F-No فیلر</label><input v-model="wps.fillerFNo" :disabled="!editing" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">A-No فیلر</label><input v-model="wps.fillerANo" :disabled="!editing" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">ضخامت از (mm)</label><input v-model="wps.thicknessMin" :disabled="!editing" class="form-control form-control-sm inv-ltr" type="number" step="any" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">ضخامت تا (mm)</label><input v-model="wps.thicknessMax" :disabled="!editing" class="form-control form-control-sm inv-ltr" type="number" step="any" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">وضعیت‌های جوشکاری</label><input v-model="wps.positions" :disabled="!editing" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">حداقل پیش‌گرم (°C)</label><input v-model="wps.preheatMinC" :disabled="!editing" class="form-control form-control-sm inv-ltr" type="number" step="any" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">حداکثر بین‌پاسی (°C)</label><input v-model="wps.interpassMaxC" :disabled="!editing" class="form-control form-control-sm inv-ltr" type="number" step="any" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">PWHT</label><input v-model="wps.pwht" :disabled="!editing" class="form-control form-control-sm" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">گاز محافظ</label><input v-model="wps.shieldingGas" :disabled="!editing" class="form-control form-control-sm" /></div>
            <div class="col-md-6"><label class="form-label small mb-0">توضیح</label><input v-model="wps.note" :disabled="!editing" class="form-control form-control-sm" /></div>
            <div class="col-md-12">
              <label class="form-label small mb-0">PQR های مبنا (حداقل یک PQR تاییدشده)</label>
              <div class="d-flex flex-wrap gap-2">
                <label v-for="p in pqrs" :key="p.id" class="form-check small border rounded px-2 py-1">
                  <input v-model="wps.pqrIds" :disabled="!editing" :value="p.id" class="form-check-input ms-1" type="checkbox" />
                  <span class="inv-ltr">{{ p.number }}</span> {{ p.process }} <span :class="p.approved ? 'ok' : 'warn'" class="inv-badge">{{ p.approved ? "تاییدشده" : "تایید نشده" }}</span>
                </label>
              </div>
            </div>
          </div>
          <div v-if="editing" class="mt-3"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
          <div v-if="wps.id" class="d-flex flex-wrap gap-2 align-items-center mt-3">
            <span :class="wps.qcApprovedAt ? 'ok' : ''" class="inv-badge">تایید کیفی (QC): {{ wps.qcApprovedByName || "—" }} {{ jalali(wps.qcApprovedAt) }}</span>
            <span :class="wps.engineeringApprovedAt ? 'ok' : ''" class="inv-badge">تایید فنی (مهندسی): {{ wps.engineeringApprovedByName || "—" }} {{ jalali(wps.engineeringApprovedAt) }}</span>
            <template v-if="wps.status === 0">
              <button v-if="canQc && !wps.qcApprovedAt" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('approve-qc')">تایید کیفی</button>
              <button v-if="canEng && !wps.engineeringApprovedAt" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act('approve-engineering')">تایید فنی</button>
            </template>
          </div>
          <p v-if="wps.id" class="inv-muted mt-2 mb-0">هر دو تایید باید توسط دو نفر متفاوت و غیر از تهیه‌کننده باشد؛ ویرایش WPS تاییدها را پاک می‌کند.</p>
        </div>
      </template>

      <template v-if="tab === 'pqr'">
        <div v-if="pqrForm" class="border rounded p-2 mb-3">
          <div class="row g-2">
            <div class="col-md-2"><label class="form-label small mb-0">شماره *</label><input v-model="pqrForm.number" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">ویرایش کد (مثلاً ASME IX 2021)</label><input v-model="pqrForm.codeEdition" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-2"><label class="form-label small mb-0">فرایند *</label><input v-model="pqrForm.process" class="form-control form-control-sm inv-ltr" /></div>
            <div class="col-md-3"><label class="form-label small mb-0">سند نتایج آزمون</label><DocAttach v-model="pqrForm.resultsDocumentId" title="نتایج آزمون PQR" /></div>
            <div class="col-md-8"><label class="form-label small mb-0">توضیح</label><input v-model="pqrForm.note" class="form-control form-control-sm" /></div>
            <div class="col-md-4 d-flex align-items-end gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="savePqrForm">ذخیره</button><button class="btn btn-sm btn-outline-secondary" @click="pqrForm = null">انصراف</button></div>
          </div>
        </div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>ویرایش کد</th><th>فرایند</th><th>سند نتایج</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!pqrs.length"><td colspan="6" class="text-center inv-muted">PQR ای نیست.</td></tr>
            <tr v-for="p in pqrs" :key="p.id">
              <td class="inv-ltr">{{ p.number }}</td><td class="inv-ltr">{{ p.codeEdition }}</td><td class="inv-ltr">{{ p.process }}</td><td>{{ p.resultsDocumentId ? "پیوست شد" : "—" }}</td>
              <td><span :class="p.approved ? 'ok' : 'warn'" class="inv-badge">{{ p.approved ? "تایید: " + p.approvedByName : "تایید نشده" }}</span></td>
              <td class="text-nowrap">
                <button class="btn btn-sm btn-outline-secondary" @click="doPrint(p)">چاپ</button>
                <button v-if="canPlan && !p.approved" class="btn btn-sm btn-outline-secondary" @click="pqrForm = { ...p }">ویرایش</button>
                <button v-if="canEng && !p.approved" class="btn btn-sm btn-outline-success" @click="approvePqr(p)">تایید مهندسی</button>
              </td>
            </tr>
          </tbody>
        </table>
        <p class="inv-muted mb-0">PQR را مهندسی تایید می‌کند و تاییدکننده نباید ثبت‌کننده باشد.</p>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
