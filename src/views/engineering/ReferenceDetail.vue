<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import { getEnReferenceList, postEnReferenceAction, saveEnReferenceList, uploadEnReferenceLines, uploadEnReferenceNew } from "@/api/EngineeringAPIs";
import { downloadDoc, enPaths, EnRoles, enUnits, errText, faDigits, faNum, runEn, useEnDialog } from "@/composables/engineering";
import { saveBlob } from "@/api/ApiClient";
import { fail, ok } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";

// One reference document list. In Draft the grid is editable (seq, type, discipline, title, required, tender stage, client submittal, standard clause, units: empty = all six units).
// Submitting, approving (never by whoever edited it), returning and revising follow the DTO flags. A revision that loosens the list needs the manager AND the CEO: the server opens the deviation.
const route = useRoute();
const router = useRouter();
const id = computed(() => Number(route.params.id));
const isNew = computed(() => id.value === 0);
const x = ref<any>(null);
const form = ref<any>({ code: "", title: "", standardSource: "", note: "", lines: [] });
const loading = ref(false);
const saving = ref(false);
const saveError = ref("");

async function load() {
  saveError.value = "";
  if (isNew.value) {
    x.value = null;
    form.value = { code: "", title: "", standardSource: "", note: "", lines: [] };
    return;
  }
  loading.value = true;
  const [d, e] = await getEnReferenceList(id.value);
  loading.value = false;
  if (e || !d) return fail(e || "فهرست پیدا نشد.");
  x.value = d;
  form.value = { code: d.code, title: d.title, standardSource: d.standardSource || "", note: d.note || "", lines: (d.lines || []).map((l: any) => ({ ...l })) };
}
onMounted(load);
watch(id, load);
const d = useEnDialog(load);
const editable = computed(() => (isNew.value && hasRole(EnRoles.writers)) || !!x.value?.canEdit);
const needsCheck = computed(() => !x.value || x.value.status <= 1);
// The lines are never typed: they come from a CSV file (project instruction 6). Stored units are enum names; empty = all six.
const unitText = (l: any) => (l.units ? String(l.units).split(",").map((c) => enUnits.find((u) => u.code.toLowerCase() === c.trim().toLowerCase())?.text || c).join("، ") : "همه واحدها");
const flag = (v: boolean) => (v ? "بله" : "خیر");
const sampleCsv = "\uFEFF" + [
  "docType,title,discipline,required,tender,units",
  "DWG,نقشه مونتاژ,مکانیک,بله,بله,",
  "DATASHEET,برگه اطلاعات,مکانیک,بله,خیر,تضمین کیفیت|کنترل کیفیت",
  '"NOTE","یادداشت محاسبات، نسخه نهایی",,خیر,خیر,تولید|برنامه‌ریزی',
].join("\r\n") + "\r\n";
const downloadSample = () => saveBlob(new Blob([sampleCsv], { type: "text/csv;charset=utf-8" }), "reference-list-sample.csv");
function uploadFile() {
  d.ask({
    title: isNew.value ? "ساخت فهرست از فایل" : "بارگذاری فهرست از فایل",
    hint: (isNew.value ? "کد و عنوان فهرست از همین صفحه برداشته می‌شود. " : "ردیف‌های پیش‌نویس با ردیف‌های فایل جایگزین می‌شود. ") + "فایل با پسوند csv و کدگذاری UTF-8 (در اکسل: Save As ← CSV UTF-8) و سرستون docType,title,discipline,required,tender,units؛ ستون‌های اختیاری بعد از آن: clientSubmittal,clause,note (اگر نباشند، مقدار ثبت‌شدهٔ ردیف‌های هم‌نام نگه داشته می‌شود). جداکنندهٔ ستون‌ها «,» یا «;» است و واحدها با «|» یا «،» جدا می‌شوند (فایل نمونه را از همین صفحه بگیرید).",
    fields: [{ key: "file", label: "فایل CSV فهرست", type: "file", required: true }],
    run: async (v) => {
      if (isNew.value) {
        const [res, e] = await uploadEnReferenceNew({ code: form.value.code, title: form.value.title, standardSource: form.value.standardSource, note: form.value.note }, v.file);
        if (!e && res?.id) await router.replace(enPaths.reference(res.id));
        return [res, e];
      }
      return uploadEnReferenceLines(id.value, x.value.rowVersion, v.file);
    },
    okText: isNew.value ? "فهرست از فایل ساخته شد." : "ردیف‌های فهرست از فایل جایگزین شد.",
  });
}

async function save() {
  saving.value = true;
  saveError.value = "";
  // a header save: code, title, source and note only; no lines are sent (the lines change only through the file), the server keeps them
  const body = { id: id.value, code: form.value.code, title: form.value.title, standardSource: form.value.standardSource, note: form.value.note, rowVersion: x.value?.rowVersion };
  const [, e] = await saveEnReferenceList(body);
  saving.value = false;
  if (e) {
    saveError.value = errText(e);
    return;
  }
  ok("سربرگ فهرست ذخیره شد.");
  await load();
}
const act = (action: string, note?: string) => postEnReferenceAction({ id: id.value, action, note, rowVersion: x.value.rowVersion });
const reason = (title: string, action: string, okText: string) =>
  d.ask({ title, fields: [{ key: "n", label: "دلیل", type: "textarea", required: true, min: 15, max: 2000 }], run: (v) => act(action, v.n), okText });
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">فهرست مدارک مرجع <span v-if="x" class="inv-ltr">{{ x.code }}</span><span v-if="isNew">— فهرست تازه</span></h1>
        <router-link :to="enPaths.references" class="btn btn-sm btn-outline-secondary">فهرست‌ها</router-link>
      </div>
      <p v-if="loading" class="inv-muted">در حال خواندن…</p>
      <div v-if="needsCheck" class="en-banner amber" data-test="en-ref-check">این فهرست هنوز تاییدشده نیست؛ پیش از تایید باید با متن استاندارد {{ form.code || x?.code || "مربوط" }} و PMBOK تطبیق داده شود. محتوای فهرست را سیستم نمی‌سازد و بر عهده مهندسی است.</div>
      <div v-if="x?.status === 1" class="en-banner amber">منتظر تایید است؛ درخواست منقضی نمی‌شود.</div>
      <div v-if="x?.voidReason" class="en-banner red">ابطال‌شده: {{ x.voidReason }}</div>
      <p v-if="x?.sourceDocumentId" class="small" data-test="en-ref-source">فایل منبع: <span class="inv-ltr">{{ x.sourceFileName || "فایل" }}</span> <a href="#" class="ms-2" @click.prevent="downloadDoc(x.sourceDocumentId, x.sourceFileName || 'mdl.csv')">دریافت فایل</a></p>
      <template v-if="x">
        <div class="d-flex flex-wrap gap-2 align-items-center mb-2">
          <EnChip :status="x.status" :text="x.statusText" group="ref" /> <span v-if="x.isCurrent" class="inv-badge ok">جاری</span>
          <span class="small">ویرایش {{ faNum(x.revision, 0) }} — تهیه: {{ x.preparedByName }}<span v-if="x.approvedByName"> — تایید: {{ x.approvedByName }} {{ faDigits(x.approvedAtText || "") }}</span></span>
        </div>
      </template>

      <div class="row g-2 mb-3">
        <div class="col-md-3"><label class="form-label small mb-0">کد (استاندارد)</label><input v-model="form.code" :disabled="!editable || !isNew" class="form-control form-control-sm inv-ltr" maxlength="40" /></div>
        <div class="col-md-5"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" :disabled="!editable" class="form-control form-control-sm" maxlength="200" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">منبع استاندارد</label><input v-model="form.standardSource" :disabled="!editable" class="form-control form-control-sm" maxlength="300" /></div>
        <div class="col-12"><label class="form-label small mb-0">یادداشت</label><textarea v-model="form.note" :disabled="!editable" class="form-control form-control-sm" rows="2" maxlength="2000"></textarea></div>
      </div>

      <div class="table-responsive">
        <table class="table table-sm" data-test="en-ref-grid">
          <thead><tr><th>ردیف</th><th>نوع مدرک</th><th>رشته</th><th>عنوان</th><th>الزامی</th><th>مرحله مناقصه</th><th>ارائه به کارفرما</th><th>بند استاندارد</th><th>واحدها</th></tr></thead>
          <tbody>
            <tr v-for="(l, i) in form.lines" :key="l.id || 'n' + i">
              <td>{{ faNum(i + 1, 0) }}</td>
              <td class="inv-ltr">{{ l.documentType }}</td>
              <td>{{ l.discipline }}</td>
              <td>{{ l.title }}</td>
              <td>{{ flag(l.required) }}</td>
              <td>{{ flag(l.tenderStage) }}</td>
              <td>{{ flag(l.clientSubmittal) }}</td>
              <td>{{ l.standardClause }}</td>
              <td>{{ unitText(l) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="!form.lines.length" class="inv-muted small" data-test="en-ref-empty">هنوز ردیفی نیست؛ فهرست را از فایل بارگذاری کنید.</p>
      <div v-if="saveError" class="alert alert-danger py-2 small" role="alert">{{ saveError }}</div>
      <div v-if="x && !editable && x.editBlockedReason" class="small text-danger mb-2">{{ x.editBlockedReason }}</div>
      <div class="mt-2">
        <button v-if="editable" class="btn btn-sm btn-outline-primary ms-2" type="button" data-test="en-ref-upload" @click="uploadFile">{{ isNew ? "ساخت فهرست از فایل" : "بارگذاری فهرست از فایل" }}</button>
        <button class="btn btn-sm btn-outline-secondary ms-2" type="button" data-test="en-ref-sample" @click="downloadSample">دریافت فایل نمونه</button>
        <button v-if="editable && !isNew" :disabled="saving" class="btn btn-sm btn-primary ms-2" type="button" @click="save">ذخیره سربرگ</button>
        <template v-if="x">
          <EnAction :can="x.canSubmit" :reason="x.submitBlockedReason" :roles="EnRoles.writers" :confirm="`فهرست ${x.code} برای تایید ارسال شود؟ تصمیم با تاییدکنندگان فهرست است.`" label="ارسال برای تایید" variant="outline-primary" @go="runEn(() => act('submit'), 'برای تایید ارسال شد.', load)" />
          <EnAction :can="x.canApprove" :reason="x.approveBlockedReason" :roles="EnRoles.refApprovers" :confirm="`فهرست ${x.code} (ویرایش ${faNum(x.revision, 0)}) تایید شود؟ از این پس ملاک پرونده‌های این استاندارد است.`" label="تایید فهرست" variant="success" @go="runEn(() => act('approve'), 'فهرست تایید شد.', load)" />
          <EnAction :can="x.canReturn" :reason="x.returnBlockedReason" :roles="EnRoles.refApprovers" label="برگشت" variant="outline-danger" @go="reason('برگشت فهرست', 'return', 'فهرست برگشت داده شد.')" />
          <EnAction :can="x.canRevise" :reason="x.reviseBlockedReason" :roles="EnRoles.writers" :confirm="`ویرایش تازه‌ای از فهرست ${x.code} ساخته شود؟ ویرایش فعلی دست‌نخورده می‌ماند و ویرایش تازه باید دوباره تایید شود.`" label="ویرایش تازه" variant="outline-secondary" @go="runEn(() => act('revise'), 'ویرایش تازه ساخته شد؛ اگر فهرست را سهل‌تر کند مدیر مهندسی و مدیرعامل هر دو باید تایید کنند.', load)" />
          <EnAction :can="x.canVoid" :reason="x.voidBlockedReason" :roles="EnRoles.refApprovers" label="ابطال" variant="outline-danger" @go="reason('ابطال فهرست', 'void', 'فهرست باطل شد.')" />
        </template>
      </div>
      <p v-if="x?.approveBlockedReason && x.status === 1" class="small inv-muted mt-2">{{ x.approveBlockedReason }}</p>
    </div>
    <EnPrompt v-if="d.dlg.value" :busy="d.busy.value" :error="d.error.value" :fields="d.dlg.value.fields" :hint="d.dlg.value.hint" :title="d.dlg.value.title" @cancel="d.close" @confirm="d.confirm" />
  </div>
</template>
