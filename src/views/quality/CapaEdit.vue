<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import DocAttach from "@/components/quality/DocAttach.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { capaAction, getCapa, saveCapa } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { capaSources, capaStatuses, dateInput, rootCauseMethods } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// One CAPA (FO-QA-017): problem, containment, root cause, actions, effectiveness check, closing by someone other than the owner.
const route = useRoute();
const router = useRouter();
const capa = ref<any>(null);
const busy = ref(false);
const note = ref("");
const cancelNote = ref("");
const evidence = ref<string | null>(null);
const passed = ref(true);
const isNew = route.params.id === "new";
const canStaff = hasRole(QualityRoles.capaStaff);
const canClose = hasRole(QualityRoles.capaClosers);
const editable = computed(() => canStaff && (isNew || (capa.value && capa.value.status <= 1)));
const status = computed(() => capa.value?.status);

function load(dto: any) {
  capa.value = {
    ...dto,
    dueDate: dateInput(dto.dueDate),
    effectivenessDueDate: dateInput(dto.effectivenessDueDate),
    actions: (dto.actions || []).map((a: any) => ({ ...a, dueDate: dateInput(a.dueDate) })),
  };
}

function blank() {
  const q = route.query;
  return {
    id: null, rowVersion: null, status: 0, source: q.source ? Number(q.source) : 9, sourceId: q.sourceId ? Number(q.sourceId) : null, problem: String(q.problem || ""), containment: "",
    rootCauseMethod: 0, rootCauseAnalysis: "", ownerUserId: null, dueDate: null, effectivenessCriteria: "", actions: [] as any[],
  };
}

const addAction = () => capa.value.actions.push({ id: null, description: "", ownerUserId: null, dueDate: null });

async function save() {
  const c = capa.value;
  busy.value = true;
  const [data, error] = await saveCapa({
    id: c.id, rowVersion: c.rowVersion, source: c.source, sourceId: c.sourceId || null, problem: c.problem, containment: c.containment, rootCauseMethod: Number(c.rootCauseMethod),
    rootCauseAnalysis: c.rootCauseAnalysis, ownerUserId: c.ownerUserId || null, dueDate: c.dueDate || null, effectivenessCriteria: c.effectivenessCriteria,
    actions: c.actions.map((a: any) => ({ id: a.id, description: a.description, ownerUserId: a.ownerUserId || null, dueDate: a.dueDate || null })),
  });
  busy.value = false;
  if (error) return fail(error);
  ok("ذخیره شد.");
  load(data);
  if (isNew) router.replace({ name: "QualityCapaEdit", params: { id: data.id } });
}

async function act(action: string, extra: any = {}) {
  busy.value = true;
  const [data, error] = await capaAction(capa.value.id, action, extra);
  busy.value = false;
  if (error) return fail(error);
  load(data);
  note.value = "";
  evidence.value = null;
  ok("انجام شد.");
}

onMounted(async () => {
  if (isNew) {
    capa.value = blank();
    return;
  }
  const [data, error] = await getCapa(Number(route.params.id));
  if (error) return fail(error);
  load(data);
});
</script>

<template>
  <div v-if="capa" class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">اقدام اصلاحی و پیشگیرانه <span v-if="capa.number" class="inv-ltr ms-2">{{ capa.number }}</span></h1>
          <span :class="{ ok: status === 4, bad: capa.overdue, warn: !capa.overdue && status < 4 }" class="inv-badge">{{ capaStatuses[status] }}</span>
          <span v-if="capa.overdue" class="inv-badge bad ms-1">از موعد گذشته</span>
          <span v-if="capa.autoDrafted" class="inv-badge info ms-1">پیش‌نویس سیستم؛ تضمین کیفیت مسئول و موعد را تعیین کند</span>
        </div>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'QualityCapas' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>

      <div class="row g-2">
        <div class="col-md-3"><label class="form-label small mb-0">منبع</label><select v-model="capa.source" :disabled="!editable" class="form-select form-select-sm"><option v-for="(t, k) in capaSources" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-3">
          <label class="form-label small mb-0">شناسه سند منبع</label>
          <input v-model.number="capa.sourceId" :disabled="!editable" class="form-control form-control-sm" type="number" />
          <span v-if="capa.sourceNumber" class="inv-muted">سند: <span class="inv-ltr">{{ capa.sourceNumber }}</span></span>
        </div>
        <div class="col-md-6"><label class="form-label small mb-0">شرح مساله (عدم انطباق) *</label><textarea v-model="capa.problem" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-6"><label class="form-label small mb-0">اقدام مهار / اصلاح فوری</label><textarea v-model="capa.containment" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-2"><label class="form-label small mb-0">روش تحلیل علت</label><select v-model="capa.rootCauseMethod" :disabled="!editable" class="form-select form-select-sm"><option v-for="(t, k) in rootCauseMethods" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-4"><label class="form-label small mb-0">تحلیل علت ریشه‌ای</label><textarea v-model="capa.rootCauseAnalysis" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-3"><label class="form-label small mb-0">مسئول CAPA</label><UserSelect v-model="capa.ownerUserId" :disabled="!editable" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">موعد</label><input v-model="capa.dueDate" :disabled="!editable" class="form-control form-control-sm" type="date" /></div>
        <div class="col-md-7"><label class="form-label small mb-0">معیار اثربخشی</label><input v-model="capa.effectivenessCriteria" :disabled="!editable" class="form-control form-control-sm" /></div>
        <div v-if="capa.effectivenessDueDate" class="col-md-3"><label class="form-label small mb-0">موعد بررسی اثربخشی</label><div class="small pt-1">{{ jalali(capa.effectivenessDueDate) }}</div></div>
      </div>
      <p v-if="editable" class="inv-muted mt-2 mb-0">وقتی مسئول، موعد، روش و تحلیل علت، معیار اثربخشی و حداقل یک اقدام تکمیل باشد، وضعیت خودکار «اقدام‌ها تعریف شد» می‌شود.</p>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">اقدام‌ها</h2>
        <button v-if="editable" class="btn btn-sm btn-outline-primary" @click="addAction">اقدام جدید</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th>شرح اقدام</th><th>مسئول</th><th>موعد</th><th>انجام شد</th><th>مدرک</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!capa.actions.length"><td colspan="6" class="text-center inv-muted">اقدامی تعریف نشده.</td></tr>
          <tr v-for="(a, i) in capa.actions" :key="a.id || 'n' + i">
            <template v-if="editable && !a.doneAt">
              <td><input v-model="a.description" class="form-control form-control-sm" /></td>
              <td><UserSelect v-model="a.ownerUserId" /></td>
              <td><input v-model="a.dueDate" class="form-control form-control-sm" type="date" /></td>
              <td></td><td></td>
              <td><button class="btn btn-sm btn-outline-danger" @click="capa.actions.splice(i, 1)">حذف</button></td>
            </template>
            <template v-else>
              <td>{{ a.description }}</td><td>{{ a.ownerName || "—" }}</td>
              <td>{{ jalali(a.dueDate) }} <span v-if="a.overdue" class="inv-badge bad">گذشته</span></td>
              <td>{{ jalali(a.doneAt) }}</td>
              <td><span class="small">{{ a.evidenceNote }}</span> <span v-if="a.evidenceDocumentId" class="inv-badge ok">فایل</span></td>
              <td>
                <button v-if="status === 1 && !a.doneAt" class="btn btn-sm btn-outline-success" :disabled="busy" @click="act('complete-action', { actionId: a.id, note, evidenceDocumentId: evidence })">ثبت انجام</button>
              </td>
            </template>
          </tr>
        </tbody>
      </table>
      <div v-if="status === 1" class="row g-2 align-items-end">
        <div class="col-md-6"><label class="form-label small mb-0">شرح انجام / مدرک (برای ثبت انجام اقدام)</label><input v-model="note" class="form-control form-control-sm" /></div>
        <div class="col-md-4"><DocAttach v-model="evidence" title="مدرک انجام اقدام اصلاحی" /></div>
      </div>
    </div>

    <div v-if="capa.id" class="inv-card">
      <h2 class="inv-title mb-2">اجرا، اثربخشی و بستن</h2>
      <div class="d-flex flex-wrap gap-2 align-items-end">
        <button v-if="status === 1 && canStaff" :disabled="busy" class="btn btn-sm btn-outline-primary" @click="act('implemented')">همه اقدام‌ها اجرا شد</button>
        <template v-if="status === 2 && canClose">
          <div class="flex-grow-1" style="min-width: 260px"><label class="form-label small mb-0">نتیجه بررسی اثربخشی (طبق معیار) *</label><input v-model="note" class="form-control form-control-sm" /></div>
          <label class="form-check small"><input v-model="passed" class="form-check-input" type="checkbox" /> اثربخش بود</label>
          <button :disabled="busy" class="btn btn-sm btn-success" @click="act('effectiveness', { note, passed })">ثبت اثربخشی</button>
        </template>
        <template v-if="status === 3 && canClose">
          <div class="flex-grow-1" style="min-width: 260px"><label class="form-label small mb-0">یادداشت بستن</label><input v-model="note" class="form-control form-control-sm" /></div>
          <button :disabled="busy" class="btn btn-sm btn-success" @click="act('close', { note })">بستن CAPA</button>
        </template>
        <template v-if="status < 4 && canStaff">
          <input v-model="cancelNote" class="form-control form-control-sm" placeholder="دلیل لغو" style="max-width: 240px" />
          <button :disabled="busy || !cancelNote.trim()" class="btn btn-sm btn-outline-danger" @click="act('cancel', { note: cancelNote })">لغو CAPA</button>
        </template>
      </div>
      <p class="inv-muted mt-2 mb-0">اثربخشی و بستن را مسئول CAPA انجام نمی‌دهد؛ فقط تضمین کیفیت یا QC. اگر اثربخشی تایید نشود CAPA به مرحله «اقدام‌ها تعریف شد» برمی‌گردد.</p>
      <div v-if="capa.effectivenessResult" class="small mt-2">نتیجه اثربخشی: {{ capa.effectivenessResult }}</div>
      <div v-if="capa.closedAt" class="small mt-1">بسته شد: {{ capa.closedByName }} {{ jalali(capa.closedAt, true) }} — {{ capa.closeNote }}</div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
