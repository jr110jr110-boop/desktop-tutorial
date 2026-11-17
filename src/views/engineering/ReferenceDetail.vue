<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import EnAction from "@/components/engineering/EnAction.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import EnPrompt from "@/components/engineering/EnPrompt.vue";
import { getEnReferenceList, postEnReferenceAction, saveEnReferenceList } from "@/api/EngineeringAPIs";
import { enPaths, EnRoles, enUnits, errText, faDigits, faNum, runEn, useEnDialog } from "@/composables/engineering";
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
const unitNotice = ref("");

const blank = () => ({ seq: 0, documentType: "", discipline: "", title: "", required: true, tenderStage: false, clientSubmittal: false, standardClause: "", note: "", units: "" });
async function load() {
  saveError.value = "";
  if (isNew.value) {
    x.value = null;
    form.value = { code: "", title: "", standardSource: "", note: "", lines: [blank()] };
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
// Stored units are enum names; empty = all six. The tick boxes of an empty value start from all six codes, so unticking one keeps the other five.
const unitsOf = (l: any): string[] => {
  const raw = l.units ? String(l.units).split(/[,،;]/).map((s) => s.trim().toLowerCase()).filter(Boolean) : [];
  return raw.length ? enUnits.filter((u) => raw.includes(u.code.toLowerCase())).map((u) => u.code) : enUnits.map((u) => u.code);
};
function toggleUnit(l: any, code: string, ev?: Event) {
  const cur = unitsOf(l);
  const next = cur.includes(code) ? cur.filter((c) => c !== code) : [...cur, code];
  if (!next.length) {
    unitNotice.value = "هر سطر دست‌کم یک واحد دارد؛ برای رساندن به همهٔ واحدها همهٔ واحدها را انتخاب کنید.";
    if (ev?.target) (ev.target as HTMLInputElement).checked = true;
    return;
  }
  unitNotice.value = "";
  l.units = next.length === enUnits.length ? "" : next.join(",");
}
const addLine = () => form.value.lines.push(blank());
const removeLine = (i: number) => form.value.lines.splice(i, 1);

async function save() {
  saving.value = true;
  saveError.value = "";
  const body = { id: isNew.value ? null : id.value, ...form.value, lines: form.value.lines.map((l: any, i: number) => ({ ...l, seq: i + 1 })), rowVersion: x.value?.rowVersion };
  const [res, e] = await saveEnReferenceList(body);
  saving.value = false;
  if (e) {
    saveError.value = errText(e);
    return;
  }
  ok("فهرست ذخیره شد.");
  if (isNew.value && res?.id) router.replace(enPaths.reference(res.id));
  else await load();
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
          <thead><tr><th>ردیف</th><th>نوع مدرک</th><th>رشته</th><th>عنوان</th><th>الزامی</th><th>مرحله مناقصه</th><th>ارائه به کارفرما</th><th>بند استاندارد</th><th>واحدها (خالی = همه)</th><th v-if="editable"></th></tr></thead>
          <tbody>
            <tr v-for="(l, i) in form.lines" :key="l.id || 'n' + i">
              <td>{{ faNum(i + 1, 0) }}</td>
              <td><input v-model="l.documentType" :disabled="!editable" class="form-control form-control-sm" maxlength="40" /></td>
              <td><input v-model="l.discipline" :disabled="!editable" class="form-control form-control-sm" maxlength="80" /></td>
              <td><input v-model="l.title" :disabled="!editable" class="form-control form-control-sm" maxlength="300" /></td>
              <td><input v-model="l.required" :disabled="!editable" type="checkbox" /></td>
              <td><input v-model="l.tenderStage" :disabled="!editable" type="checkbox" /></td>
              <td><input v-model="l.clientSubmittal" :disabled="!editable" type="checkbox" /></td>
              <td><input v-model="l.standardClause" :disabled="!editable" class="form-control form-control-sm" maxlength="100" /></td>
              <td class="en-units"><label v-for="u in enUnits" :key="u.value"><input :checked="unitsOf(l).includes(u.code)" :disabled="!editable" type="checkbox" @change="toggleUnit(l, u.code, $event)" /> {{ u.text }}</label></td>
              <td v-if="editable"><button class="btn btn-sm btn-outline-danger" type="button" @click="removeLine(i)">حذف</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="unitNotice" class="alert alert-warning py-2 small" role="status">{{ unitNotice }}</div>
      <div v-if="saveError" class="alert alert-danger py-2 small" role="alert">{{ saveError }}</div>
      <div v-if="x && !editable && x.editBlockedReason" class="small text-danger mb-2">{{ x.editBlockedReason }}</div>
      <div class="mt-2">
        <button v-if="editable" class="btn btn-sm btn-outline-secondary ms-2" type="button" @click="addLine">افزودن سطر</button>
        <button v-if="editable" :disabled="saving" class="btn btn-sm btn-primary ms-2" type="button" @click="save">ذخیره</button>
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
