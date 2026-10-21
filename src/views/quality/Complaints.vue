<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import WorkOrderSelect from "@/components/quality/WorkOrderSelect.vue";
import { complaintAction, getComplaint, getComplaints, saveComplaint } from "@/api/QualityAPIs";
import { fail, jalali, ok, printPage, today } from "@/composables/inventory";
import { complaintStatuses, dateInput, severities, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Customer complaints (ISO 9001 9.1.2 and 10.2): received -> review -> response (due in 10 days) -> close; links to an NCR or a CAPA.
const router = useRouter();
const filters = ref({ Status: null as number | null, Overdue: null as boolean | null, Contains: "" });
const list = usePaged(getComplaints, filters);
const canWrite = hasRole(QualityRoles.complaints);
const canStaff = hasRole(QualityRoles.capaStaff);
const c = ref<any>(null);
const response = ref("");
const ncrId = ref<number | null>(null);
const severity = ref(0);
const busy = ref(false);

const blank = () => ({ id: null, workOrderId: null, receivedDate: today(), channel: "", description: "", severity: 0, status: 0 });
const editable = () => c.value && (!c.value.id || c.value.status <= 1);

async function open(id: number) {
  const [data, error] = await getComplaint(id);
  if (error) return fail(error);
  c.value = { ...data, receivedDate: dateInput(data.receivedDate) };
  severity.value = data.severity;
  response.value = data.response || "";
}

async function save() {
  busy.value = true;
  const [data, error] = await saveComplaint({
    id: c.value.id, workOrderId: c.value.workOrderId, contractId: c.value.contractId || null, receivedDate: c.value.receivedDate, channel: c.value.channel, description: c.value.description, severity: Number(c.value.severity),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  c.value = { ...data, receivedDate: dateInput(data.receivedDate) };
  list.load();
}

async function act(action: string, extra: any = {}) {
  busy.value = true;
  const [data, error] = await complaintAction(c.value.id, action, extra);
  busy.value = false;
  if (error) return fail(error);
  c.value = { ...data, receivedDate: dateInput(data.receivedDate) };
  ok("انجام شد.");
  list.load();
}

onMounted(list.load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader title="شکایت‌های مشتری" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">شکایت‌های مشتری</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="c = blank()">شکایت جدید</button>
        </div>
      </div>
      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره یا شرح" @keydown.enter="list.search()" />
        <select v-model="filters.Status" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in complaintStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Overdue" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه</option><option :value="true">از مهلت پاسخ گذشته</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>دستور ساخت</th><th>تاریخ دریافت</th><th>شدت</th><th>مهلت پاسخ</th><th>وضعیت</th><th>NCR</th><th>CAPA</th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">شکایتی نیست.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id" role="button" @click="open(r.id)">
            <td class="inv-ltr">{{ r.number }}</td><td class="inv-ltr">{{ r.workOrderCode }}</td><td>{{ jalali(r.receivedDate) }}</td><td>{{ severities[r.severity] }}</td>
            <td>{{ jalali(r.responseDue) }} <span v-if="r.overdue" class="inv-badge bad">گذشته</span></td>
            <td><span :class="{ ok: r.status >= 2, warn: r.status < 2 }" class="inv-badge">{{ complaintStatuses[r.status] }}</span></td>
            <td>{{ r.ncrId ? "بله" : "" }}</td><td>{{ r.capaId ? "بله" : "" }}</td>
          </tr>
        </tbody>
      </table>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

      <div v-if="c" class="border rounded p-3 mt-3 d-print-none">
        <div class="inv-toolbar">
          <b>{{ c.number ? "شکایت " + c.number : "شکایت جدید" }}</b>
          <div class="d-flex gap-2">
            <span v-if="c.id" :class="{ ok: c.status >= 2, warn: c.status < 2 }" class="inv-badge">{{ complaintStatuses[c.status] }}</span>
            <button v-if="editable() && canWrite" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
            <button class="btn btn-sm btn-outline-secondary" @click="c = null">بستن</button>
          </div>
        </div>
        <div class="row g-2">
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ دریافت</label><input v-model="c.receivedDate" :disabled="!editable()" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">دستور ساخت</label><WorkOrderSelect v-model="c.workOrderId" :disabled="!editable()" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">کانال دریافت (نامه، ایمیل، تلفن ...)</label><input v-model="c.channel" :disabled="!editable()" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">شدت</label><select v-model="c.severity" :disabled="!editable()" class="form-select form-select-sm"><option v-for="(t, k) in severities" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div v-if="c.id" class="col-md-3"><label class="form-label small mb-0">مهلت پاسخ</label><div class="small pt-1">{{ jalali(c.responseDue) }} <span v-if="c.overdue" class="inv-badge bad">گذشته</span></div></div>
          <div class="col-md-12"><label class="form-label small mb-0">شرح شکایت *</label><textarea v-model="c.description" :disabled="!editable()" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>

        <div v-if="c.id" class="mt-3">
          <div class="d-flex flex-wrap gap-2 align-items-end">
            <template v-if="c.status === 0 && canWrite">
              <select v-model="severity" class="form-select form-select-sm" style="max-width: 180px"><option v-for="(t, k) in severities" :key="k" :value="Number(k)">{{ t }}</option></select>
              <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act('review', { severity })">شروع بررسی (تعیین شدت)</button>
            </template>
            <template v-if="c.status < 3 && canWrite">
              <input v-model.number="ncrId" class="form-control form-control-sm" placeholder="شناسه NCR" style="max-width: 140px" type="number" />
              <button :disabled="busy || !ncrId" class="btn btn-sm btn-outline-secondary" @click="act('link-ncr', { ncrId })">ارتباط با NCR</button>
            </template>
            <button v-if="c.status < 3 && !c.capaId && canStaff" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('create-capa')">ساخت CAPA</button>
          </div>
          <p v-if="c.ncrNumber || c.capaNumber" class="small mt-2 mb-0">
            <span v-if="c.ncrId">NCR: <router-link :to="{ name: 'InventoryNcrEdit', params: { id: c.ncrId } }" class="inv-ltr">{{ c.ncrNumber }}</router-link></span>
            <span v-if="c.capaId" class="ms-3">CAPA: <router-link :to="{ name: 'QualityCapaEdit', params: { id: c.capaId } }" class="inv-ltr">{{ c.capaNumber }}</router-link></span>
          </p>
          <div v-if="c.status === 1 && canWrite" class="mt-2">
            <label class="form-label small mb-0">پاسخ به مشتری *</label>
            <textarea v-model="response" class="form-control form-control-sm" rows="2"></textarea>
            <button :disabled="busy" class="btn btn-sm btn-success mt-2" @click="act('respond', { response })">ثبت پاسخ</button>
          </div>
          <div v-if="c.status >= 2" class="small mt-2">پاسخ ({{ jalali(c.respondedAt) }}): {{ c.response }}</div>
          <button v-if="c.status === 2 && canStaff" :disabled="busy" class="btn btn-sm btn-success mt-2" @click="act('close')">بستن شکایت</button>
          <p class="inv-muted mt-2 mb-0">شکایت عمده پیش از بستن باید به NCR یا CAPA مرتبط شود. بستن را تضمین کیفیت یا QC انجام می‌دهد.</p>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
