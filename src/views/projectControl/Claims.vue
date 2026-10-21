<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ContractSelect from "@/components/pc/ContractSelect.vue";
import NeedContract from "@/components/pc/NeedContract.vue";
import { claimAction, getClaims, saveClaim } from "@/api/ProjectControlAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, ok, rial } from "@/composables/inventory";
import { claimDirections, claimStatuses, contractLabel, pcContractId } from "@/composables/projectControl";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

// Claims (ادعا): quantify, approvals of PMO and design/manufacturing, notify, negotiate, settle, withdraw, litigate.
const rows = ref<any[]>([]);
const form = ref<any>(null);
const act = ref<any>(null);
const busy = ref(false);
const can = hasRole(ProjectControlRoles.claims);
const isPmo = hasRole(ProjectControlRoles.pmo);
const canDesign = hasRole(ProjectControlRoles.claimDesign);
const canSettle = hasRole(ProjectControlRoles.claimSettle);
const isCeo = hasRole(ProjectControlRoles.ceo);

async function load() {
  if (!pcContractId.value) return;
  const [d, e] = await getClaims({ ContractId: pcContractId.value });
  if (e) return fail(e);
  rows.value = rowsOf(d);
}
async function save() {
  busy.value = true;
  const [, e] = await saveClaim({ ...form.value, contractId: pcContractId.value });
  busy.value = false;
  if (e) return fail(e);
  ok("ذخیره شد.");
  form.value = null;
  load();
}
async function doAct(r: any, action: string, extra: any = {}) {
  busy.value = true;
  const [, e] = await claimAction({ id: r.id, action, ...extra });
  busy.value = false;
  if (e) return fail(e);
  ok("انجام شد.");
  act.value = null;
  load();
}
onMounted(load);
watch(pcContractId, load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="ادعاها (Claims)" :subtitle="contractLabel(pcContractId)" />
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title screen-only">ادعاها</h1><div class="d-print-none"><ContractSelect /></div></div>
      <NeedContract v-if="!pcContractId" />
      <template v-else>
        <div class="mb-2 d-print-none"><button v-if="can" class="btn btn-sm btn-primary" @click="form = { direction: 1, issue: '', basis: '', isCrossClaim: false }">ادعای جدید</button></div>
        <table class="table table-sm">
          <thead><tr><th>شماره</th><th>جهت</th><th>موضوع و مبنا</th><th>برآورد</th><th>تاییدها</th><th>وضعیت</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="7" class="text-center inv-muted">ادعایی ثبت نشده است.</td></tr>
            <tr v-for="r in rows" :key="r.id">
              <td class="inv-ltr">{{ r.number }}</td><td>{{ claimDirections[r.direction] }}<span v-if="r.isCrossClaim" class="inv-badge info ms-1">ادعای متقابل</span></td>
              <td>{{ r.issue }}<div class="small inv-muted">{{ r.basis }}</div></td>
              <td class="small"><div v-if="r.daysQuantified">{{ r.daysQuantified }} روز</div><div v-if="r.amountQuantified">{{ rial(r.amountQuantified) }} ریال</div></td>
              <td class="small"><div>PMO: <span :class="r.pmoApproved ? 'ok' : 'warn'" class="inv-badge">{{ r.pmoApproved ? "تایید" : "—" }}</span></div><div>طراحی/ساخت: <span :class="r.designManufacturingApproved ? 'ok' : 'warn'" class="inv-badge">{{ r.designManufacturingApproved ? "تایید" : "—" }}</span></div><div class="inv-muted">{{ r.reviewNote }}</div></td>
              <td>{{ claimStatuses[r.status] }}</td>
              <td class="d-print-none text-nowrap">
                <template v-if="r.status < 4 || r.status === 6">
                  <button v-if="can && (r.status === 0 || r.status === 1)" class="btn btn-sm btn-outline-secondary me-1" @click="act = { r, action: 'quantify', days: r.daysQuantified, amount: r.amountQuantified, note: '' }">برآورد</button>
                  <button v-if="isPmo && !r.pmoApproved" class="btn btn-sm btn-outline-primary me-1" @click="act = { r, action: 'approve-pmo', note: '' }">تایید PMO</button>
                  <button v-if="canDesign && !r.designManufacturingApproved" class="btn btn-sm btn-outline-primary me-1" @click="act = { r, action: 'approve-design', note: '' }">تایید طراحی/ساخت</button>
                  <button v-if="can && r.status === 1" class="btn btn-sm btn-outline-primary me-1" @click="doAct(r, 'notify')">ابلاغ</button>
                  <button v-if="can && r.status === 2" class="btn btn-sm btn-outline-primary me-1" @click="doAct(r, 'negotiate')">شروع مذاکره</button>
                  <button v-if="canSettle && (r.status === 2 || r.status === 3)" class="btn btn-sm btn-outline-success me-1" @click="act = { r, action: 'settle', note: '' }">تسویه</button>
                  <button v-if="isCeo && r.status === 3" class="btn btn-sm btn-outline-danger me-1" @click="doAct(r, 'litigate')">ارجاع قضایی</button>
                  <button v-if="(can || isCeo) && r.status !== 6" class="btn btn-sm btn-outline-danger" @click="doAct(r, 'withdraw')">انصراف</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </template>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h3 class="h6">{{ form.id ? "ویرایش ادعا" : "ادعای جدید" }}</h3>
        <label class="form-label small mb-0">جهت</label><select v-model.number="form.direction" class="form-select form-select-sm mb-2"><option v-for="(n, k) in claimDirections" :key="k" :value="Number(k)">{{ n }}</option></select>
        <label class="form-label small mb-0">موضوع</label><textarea v-model="form.issue" class="form-control form-control-sm mb-2" rows="2"></textarea>
        <label class="form-label small mb-0">مبنای قراردادی</label><textarea v-model="form.basis" class="form-control form-control-sm mb-2" rows="2"></textarea>
        <label class="form-check mb-3"><input v-model="form.isCrossClaim" class="form-check-input" type="checkbox" /> ادعای متقابل</label>
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="form = null">انصراف</button><button :disabled="busy || !form.issue" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
    <div v-if="act" class="inv-modal-backdrop" @click.self="act = null">
      <div class="inv-modal">
        <h3 class="h6">{{ act.action === 'quantify' ? "برآورد ادعا" : act.action === 'settle' ? "تسویه ادعا" : "تایید ادعا" }} — <span class="inv-ltr">{{ act.r.number }}</span></h3>
        <template v-if="act.action === 'quantify'">
          <label class="form-label small mb-0">زمان (روز)</label><input v-model.number="act.days" class="form-control form-control-sm mb-2" type="number" min="0" />
          <label class="form-label small mb-0">مبلغ (ریال)</label><input v-model.number="act.amount" class="form-control form-control-sm mb-2" type="number" min="0" />
        </template>
        <label class="form-label small mb-0">یادداشت</label><input v-model="act.note" class="form-control form-control-sm mb-3" />
        <div class="d-flex gap-2 justify-content-end"><button class="btn btn-sm btn-secondary" @click="act = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="doAct(act.r, act.action, { days: act.days, amount: act.amount, note: act.note })">ثبت</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
