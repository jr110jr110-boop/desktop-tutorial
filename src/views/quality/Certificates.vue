<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import CertificatePrint from "@/components/quality/print/CertificatePrint.vue";
import { onMounted, ref } from "vue";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import QPager from "@/components/quality/QPager.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { getCertificates, saveCertificate, verifyCertificate } from "@/api/QualityAPIs";
import { getLots } from "@/api/InventoryAPIs";
import { rowsOf } from "@/api/ApiClient";
import { fail, jalali, ok, printPage } from "@/composables/inventory";
import { certificateResults, certificateTypes, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Material certificates (EN 10204 2.2 / 3.1 / 3.2) per lot. The warehouse may attach them; only QC verifies.
const filters = ref({ Result: null as number | null, Contains: "" });
const list = usePaged(getCertificates, filters);
const canWrite = hasRole(QualityRoles.certificateWriters);
const canVerify = hasRole(QualityRoles.inspectors);
const form = ref<any>(null);
const item = ref<any>(null);
const lots = ref<any[]>([]);
const verifyNote = ref<Record<number, string>>({});
const busy = ref(false);
const printCert = ref<any>(null);
async function doPrint(c: any) {
  printCert.value = c;
  await nextTick();
  printForm();
}

const blank = () => ({ id: null, lotId: null, type: 2, number: "", issuer: "", documentId: null, summary: "" });

async function loadLots() {
  lots.value = [];
  if (!item.value) return;
  const [data] = await getLots({ ItemId: item.value.id, PageSize: 200 });
  lots.value = rowsOf(data);
}

function edit(c: any) {
  form.value = { id: c.id, lotId: c.lotId, type: c.type, number: c.number, issuer: c.issuer, documentId: c.documentId, summary: c.summary };
  item.value = { id: c.itemId, code: c.itemCode, title: c.itemTitle };
  lots.value = [{ id: c.lotId, lotNumber: c.lotNumber }];
}

async function save() {
  busy.value = true;
  const [, error] = await saveCertificate(form.value);
  busy.value = false;
  if (error) return fail(error);
  ok("گواهی ثبت شد.");
  form.value = null;
  list.load();
}

async function verify(c: any, accept: boolean) {
  busy.value = true;
  const [, error] = await verifyCertificate(c.id, accept, verifyNote.value[c.id]);
  busy.value = false;
  if (error) return fail(error);
  ok(accept ? "گواهی پذیرفته شد." : "گواهی رد شد.");
  list.load();
}

onMounted(list.load);
</script>

<template>
  <div class="inv-page ks-report">
    <CertificatePrint v-if="printCert" :cert="printCert" />
    <KsReportHeader title="گواهی‌های مواد (EN 10204)" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">گواهی‌های مواد (EN 10204)</h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ فهرست</button>
          <router-link :to="{ name: 'QualityTraceability' }" class="btn btn-sm btn-outline-secondary">ردیابی سریال / بچ</router-link>
          <button v-if="canWrite" class="btn btn-sm btn-primary" @click="(form = blank()), (item = null), (lots = [])">گواهی جدید</button>
        </div>
      </div>

      <div v-if="form" class="border rounded p-2 mb-3 d-print-none">
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">کالا *</label><ItemPicker v-model="item" :active-only="false" @select="loadLots" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">بچ / ذوب *</label><select v-model="form.lotId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="l in lots" :key="l.id" :value="l.id">{{ l.lotNumber }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">نوع گواهی</label><select v-model="form.type" class="form-select form-select-sm"><option v-for="(t, k) in certificateTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">شماره گواهی *</label><input v-model="form.number" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">صادرکننده</label><input v-model="form.issuer" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">فایل گواهی</label><DocAttach v-model="form.documentId" title="گواهی مواد" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">خلاصه شیمیایی / مکانیکی</label><input v-model="form.summary" class="form-control form-control-sm" /></div>
          <div class="col-md-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>

      <div class="inv-filters mb-3 d-print-none">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="شماره گواهی، بچ یا کالا" @keydown.enter="list.search()" />
        <select v-model="filters.Result" class="form-select form-select-sm" @change="list.search()"><option :value="null">همه</option><option v-for="(t, k) in certificateResults" :key="k" :value="Number(k)">{{ t }}</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search()">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm">
          <thead><tr><th>کالا</th><th>بچ</th><th>ذوب</th><th>نوع</th><th>شماره</th><th>صادرکننده</th><th>ثبت‌کننده</th><th>وضعیت</th><th class="d-print-none">تایید QC</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">گواهی‌ای نیست.</td></tr>
            <tr v-for="c in list.rows.value" :key="c.id">
              <td><span class="inv-ltr">{{ c.itemCode }}</span> {{ c.itemTitle }}</td><td class="inv-ltr">{{ c.lotNumber }}</td><td class="inv-ltr">{{ c.heatNumber }}</td>
              <td>{{ certificateTypes[c.type] }}</td><td class="inv-ltr">{{ c.number }} <button class="btn btn-sm btn-link p-0 d-print-none" @click="doPrint(c)">چاپ</button></td><td>{{ c.issuer }}</td><td>{{ c.creatorName }}</td>
              <td>
                <span :class="{ ok: c.result === 1, bad: c.result === 2, warn: c.result === 0 }" class="inv-badge">{{ certificateResults[c.result] }}</span>
                <div v-if="c.verifiedByName" class="small">{{ c.verifiedByName }} {{ jalali(c.verifiedAt) }}</div>
              </td>
              <td class="d-print-none">
                <div v-if="c.result === 0 && canVerify" class="d-flex gap-1">
                  <input v-model="verifyNote[c.id]" class="form-control form-control-sm" placeholder="یادداشت" style="min-width: 120px" />
                  <button :disabled="busy" class="btn btn-sm btn-success" @click="verify(c, true)">تایید</button>
                  <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="verify(c, false)">رد</button>
                </div>
                <button v-else-if="c.result === 0 && canWrite" class="btn btn-sm btn-outline-secondary" @click="edit(c)">ویرایش</button>
                <span v-else class="small">{{ c.verificationNote }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
