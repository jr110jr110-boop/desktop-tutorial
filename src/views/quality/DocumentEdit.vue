<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import DocAttach from "@/components/quality/DocAttach.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { getControlledDocument, revisionAction, saveControlledDocument, saveRevision } from "@/api/QualityAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { documentStatuses, documentTypes } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One controlled document with its revisions: prepared by one person, reviewed by a second, approved by a third; the previous revision becomes obsolete.
const route = useRoute();
const doc = ref<any>(null);
const rev = ref<any>(null);
const busy = ref(false);
const effective = ref(today());
const recipients = ref<{ userIds: (number | null)[]; units: string }>({ userIds: [null], units: "" });
const distributeFor = ref<number | null>(null);
const canReview = hasRole(QualityRoles.documentReviewers);
const canApprove = hasRole(QualityRoles.documentApprovers);

async function load() {
  const [data, error] = await getControlledDocument(Number(route.params.id));
  if (error) return fail(error);
  doc.value = data;
}

async function saveHeader() {
  busy.value = true;
  const [data, error] = await saveControlledDocument({ id: doc.value.id, type: doc.value.type, department: doc.value.department, title: doc.value.title, retentionClass: doc.value.retentionClass, responsibleUnit: doc.value.responsibleUnit });
  busy.value = false;
  if (error) return fail(error);
  doc.value = data;
  ok("ذخیره شد.");
}

async function addRevision() {
  busy.value = true;
  const [data, error] = await saveRevision({ documentId: doc.value.id, ...rev.value });
  busy.value = false;
  if (error) return fail(error);
  doc.value = data;
  rev.value = null;
  ok("بازنگری ثبت شد.");
}

async function act(id: number, action: string, extra: any = {}) {
  busy.value = true;
  const [data, error] = await revisionAction(id, action, extra);
  busy.value = false;
  if (error) return fail(error);
  doc.value = data;
  ok("انجام شد.");
}

async function distribute(id: number) {
  const ids = recipients.value.userIds.filter(Boolean);
  const units = recipients.value.units.split(/[,،\n]+/).map((u) => u.trim()).filter(Boolean);
  await act(id, "distribute", { recipientUserIds: ids, recipientUnits: units });
  distributeFor.value = null;
  recipients.value = { userIds: [null], units: "" };
}

onMounted(load);
</script>

<template>
  <div v-if="doc" class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">{{ doc.title }} <span class="inv-ltr ms-2">{{ doc.code }}</span></h1>
          <span :class="{ ok: doc.status === 3, bad: doc.status === 4, warn: doc.status < 3 }" class="inv-badge">{{ documentStatuses[doc.status] }}</span>
          <span class="inv-muted ms-2">بازنگری جاری {{ doc.currentRevision || "—" }} — اجرا از {{ jalali(doc.effectiveDate) || "—" }}</span>
        </div>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'QualityDocuments' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="canReview" :disabled="busy" class="btn btn-sm btn-primary" @click="saveHeader">ذخیره مشخصات</button>
          <button v-if="canReview" class="btn btn-sm btn-outline-primary" @click="rev = { revision: '', changeSummary: '', fileDocumentId: null }">بازنگری جدید</button>
        </div>
      </div>
      <div class="row g-2">
        <div class="col-md-4"><label class="form-label small mb-0">عنوان</label><input v-model="doc.title" :disabled="!canReview" class="form-control form-control-sm" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">نوع</label><select v-model="doc.type" :disabled="!canReview" class="form-select form-select-sm"><option v-for="(t, k) in documentTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-2"><label class="form-label small mb-0">واحد</label><input v-model="doc.department" :disabled="!canReview" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">رده نگهداری</label><input v-model="doc.retentionClass" :disabled="!canReview" class="form-control form-control-sm" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">واحد مسئول</label><input v-model="doc.responsibleUnit" :disabled="!canReview" class="form-control form-control-sm" /></div>
      </div>
    </div>

    <div v-if="rev" class="inv-card">
      <h2 class="inv-title mb-2">بازنگری جدید</h2>
      <div class="row g-2">
        <div class="col-md-2"><label class="form-label small mb-0">شماره بازنگری *</label><input v-model="rev.revision" class="form-control form-control-sm inv-ltr" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">شرح تغییرات *</label><input v-model="rev.changeSummary" class="form-control form-control-sm" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">فایل بازنگری</label><DocAttach v-model="rev.fileDocumentId" title="فایل بازنگری سند" /></div>
        <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="addRevision">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="rev = null">انصراف</button></div>
      </div>
      <p class="inv-muted mt-2 mb-0">همزمان فقط یک بازنگری در جریان تایید می‌تواند باشد.</p>
    </div>

    <div v-for="r in doc.revisions" :key="r.id" class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">بازنگری <span class="inv-ltr">{{ r.revision }}</span> <span :class="{ ok: r.status === 3, bad: r.status === 4, warn: r.status < 3 }" class="inv-badge ms-2">{{ documentStatuses[r.status] }}</span></h2>
        <div class="d-flex flex-wrap gap-2 align-items-end">
          <button v-if="r.status === 0 && canReview" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act(r.id, 'review')">بررسی شد</button>
          <template v-if="r.status === 1 && canApprove">
            <input v-model="effective" class="form-control form-control-sm" style="max-width: 160px" type="date" />
            <button :disabled="busy" class="btn btn-sm btn-success" @click="act(r.id, 'approve', { effectiveDate: effective })">تایید و اجرا</button>
          </template>
          <button v-if="r.status === 3 && canApprove" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act(r.id, 'obsolete')">منسوخ</button>
          <button v-if="r.status === 3 && canReview" class="btn btn-sm btn-outline-secondary" @click="distributeFor = r.id">توزیع</button>
        </div>
      </div>
      <p class="small mb-1">{{ r.changeSummary }} <DocAttach :model-value="r.fileDocumentId" :disabled="true" title="فایل بازنگری" /></p>
      <div class="d-flex flex-wrap gap-2 mb-2">
        <span class="inv-badge ok">تهیه: {{ r.preparedByName }}</span>
        <span :class="r.reviewedAt ? 'ok' : ''" class="inv-badge">بررسی: {{ r.reviewedByName || "—" }} {{ jalali(r.reviewedAt) }}</span>
        <span :class="r.approvedAt ? 'ok' : ''" class="inv-badge">تایید: {{ r.approvedByName || "—" }} {{ jalali(r.approvedAt) }}</span>
        <span v-if="r.effectiveDate" class="inv-badge info">اجرا از {{ jalali(r.effectiveDate) }}</span>
      </div>
      <div v-if="distributeFor === r.id" class="border rounded p-2 mb-2">
        <div class="row g-2 align-items-end">
          <div class="col-md-5">
            <label class="form-label small mb-0">گیرندگان (کاربر)</label>
            <div v-for="(u, i) in recipients.userIds" :key="i" class="mb-1"><UserSelect v-model="recipients.userIds[i]" /></div>
            <button class="btn btn-sm btn-link p-0" @click="recipients.userIds.push(null)">+ گیرنده دیگر</button>
          </div>
          <div class="col-md-5"><label class="form-label small mb-0">واحدها (با ویرگول جدا کنید)</label><input v-model="recipients.units" class="form-control form-control-sm" /></div>
          <div class="col-md-2 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="distribute(r.id)">ارسال</button><button class="btn btn-sm btn-outline-secondary" @click="distributeFor = null">انصراف</button></div>
        </div>
      </div>
      <table v-if="r.distributions.length" class="table table-sm mb-0">
        <thead><tr><th>گیرنده</th><th>واحد</th><th>ارسال</th><th>دریافت تایید شد</th><th></th></tr></thead>
        <tbody>
          <tr v-for="d in r.distributions" :key="d.id">
            <td>{{ d.recipientName }}</td><td>{{ d.recipientUnit }}</td><td>{{ jalali(d.sentAt, true) }}</td>
            <td>{{ d.acknowledgedAt ? jalali(d.acknowledgedAt, true) : "—" }}</td>
            <td><button v-if="!d.acknowledgedAt" :disabled="busy" class="btn btn-sm btn-outline-success" @click="act(r.id, 'acknowledge', { distributionId: d.id })">تایید دریافت</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
