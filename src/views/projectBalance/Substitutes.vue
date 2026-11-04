<script setup lang="ts">
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import { decideSubstitute, getSubstitutes, saveSubstitute } from "@/api/ProjectBalanceAPIs";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { BalanceRoles, qty, substituteScopes, substituteStatusClass, substituteStatuses } from "@/composables/projectBalance";

// Approved substitutes of an item: engineering proposes, a different engineering user (or the PMO) approves. The balance uses only approved, valid ones.
const rows = ref<any[]>([]);
const status = ref<number | null>(null);
const form = ref<any>(null);
const busy = ref(false);
const loading = ref(false);
const canPropose = hasRole(BalanceRoles.substituteProposers);

async function load() {
  loading.value = true;
  const [data, error] = await getSubstitutes({ status: status.value });
  loading.value = false;
  if (error) return fail(error);
  rows.value = data || [];
}
const blank = () => ({ itemId: null, substituteItemId: null, ratio: 1, scope: 1, workOrderId: null, validFrom: "", validTo: "", reason: "" });
async function save() {
  const f = form.value;
  busy.value = true;
  const [, error] = await saveSubstitute({ ...f, validFrom: f.validFrom || null, validTo: f.validTo || null });
  busy.value = false;
  if (error) return fail(error);
  form.value = null;
  ok("جایگزین ثبت شد؛ برای استفاده در بالانس باید توسط فرد دیگری تایید شود.");
  load();
}
async function decide(r: any, action: "approve" | "reject" | "revoke") {
  let note: string | undefined;
  if (action !== "approve") {
    note = window.prompt(action === "reject" ? "دلیل رد" : "دلیل لغو") || "";
    if (!note.trim()) return;
  }
  busy.value = true;
  const [, error] = await decideSubstitute(r.id, action, note);
  busy.value = false;
  if (error) return fail(error);
  ok(action === "approve" ? "جایگزین تایید شد." : action === "reject" ? "جایگزین رد شد." : "جایگزین لغو شد.");
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="کالاهای جایگزین" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">کالاهای جایگزین</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <button v-if="canPropose" class="btn btn-sm btn-primary" @click="form = blank()">پیشنهاد جایگزین</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <select v-model="status" class="form-select form-select-sm" style="max-width: 220px" @change="load">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in substituteStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>کالای اصلی</th><th>جایگزین</th><th>نسبت</th><th>دامنه</th><th>اعتبار</th><th>دلیل فنی</th><th>وضعیت</th><th>پیشنهاد / تصمیم</th><th class="d-print-none"></th></tr></thead>
          <tbody>
            <tr v-if="!rows.length"><td colspan="9" class="text-center inv-muted">{{ loading ? "در حال بارگذاری..." : "جایگزینی ثبت نشده است." }}</td></tr>
            <tr v-for="r in rows" :key="r.id">
              <td><span class="inv-ltr fw-bold">{{ r.itemCode }}</span> {{ r.itemTitle }}</td>
              <td><span class="inv-ltr fw-bold">{{ r.substituteItemCode }}</span> {{ r.substituteItemTitle }}</td>
              <td>{{ qty(r.ratio) }}</td>
              <td>{{ substituteScopes[r.scope] }}<span v-if="r.workOrderCode" class="inv-ltr small"> · {{ r.workOrderCode }}</span></td>
              <td class="small">{{ r.validFrom ? jalali(r.validFrom) : "—" }} تا {{ r.validTo ? jalali(r.validTo) : "—" }}</td>
              <td class="small">{{ r.reason }}</td>
              <td><span class="inv-badge" :class="substituteStatusClass[r.status]">{{ substituteStatuses[r.status] }}</span></td>
              <td class="small">{{ r.proposedByName }}<span v-if="r.decidedByName"> ← {{ r.decidedByName }} ({{ jalali(r.decidedAt) }})</span><div v-if="r.decisionNote" class="inv-muted">{{ r.decisionNote }}</div></td>
              <td class="d-print-none text-nowrap">
                <button v-if="r.canEdit" class="btn btn-sm btn-outline-secondary" @click="form = { ...r, validFrom: r.validFrom ? r.validFrom.substring(0, 10) : '', validTo: r.validTo ? r.validTo.substring(0, 10) : '' }">ویرایش</button>
                <button v-if="r.canDecide" class="btn btn-sm btn-outline-success ms-1" :disabled="busy" @click="decide(r, 'approve')">تایید</button>
                <button v-if="r.canDecide" class="btn btn-sm btn-outline-danger ms-1" :disabled="busy" @click="decide(r, 'reject')">رد</button>
                <button v-if="r.canRevoke" class="btn btn-sm btn-outline-warning ms-1" :disabled="busy" @click="decide(r, 'revoke')">لغو</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="inv-muted small mb-0">نسبت: چند واحد کالای جایگزین به‌جای یک واحد کالای اصلی. تاییدکننده نمی‌تواند پیشنهاددهنده باشد؛ هوش مصنوعی تایید نمی‌کند.</p>
    </div>

    <div v-if="form" class="inv-modal-backdrop d-print-none" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ form.id ? "ویرایش پیشنهاد جایگزین" : "پیشنهاد جایگزین" }}</h2>
        <div class="row g-2">
          <div class="col-md-6"><label class="form-label small mb-0">شناسهٔ کالای اصلی *</label><input v-model.number="form.itemId" type="number" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">شناسهٔ کالای جایگزین *</label><input v-model.number="form.substituteItemId" type="number" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نسبت *</label><input v-model.number="form.ratio" type="number" min="0" step="any" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">دامنه</label><select v-model.number="form.scope" class="form-select form-select-sm"><option v-for="(t, k) in substituteScopes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">شناسهٔ دستور کار (مخصوص پروژه)</label><input v-model.number="form.workOrderId" type="number" class="form-control form-control-sm" :disabled="form.scope !== 2" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">اعتبار از</label><input v-model="form.validFrom" type="date" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">اعتبار تا</label><input v-model="form.validTo" type="date" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">دلیل فنی *</label><textarea v-model="form.reason" rows="2" class="form-control form-control-sm"></textarea></div>
        </div>
        <div class="d-flex gap-2 mt-3">
          <button :disabled="busy || !form.itemId || !form.substituteItemId || !form.reason" class="btn btn-sm btn-primary" @click="save">ثبت</button>
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
