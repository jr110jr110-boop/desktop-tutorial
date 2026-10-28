<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import CapaPrint from "@/components/quality/print/CapaPrint.vue";
import DocAttach from "@/components/quality/DocAttach.vue";
import ReasonDialog from "@/components/quality/ReasonDialog.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { capaAction, getCapa, saveCapa } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { capaSources, capaStatuses, dateInput, printForm, rootCauseMethods, useBackdateWindow } from "@/composables/quality";
import { currentUserId, hasRole, QualityRoles } from "@/composables/roles";

// One CAPA (FO-QA-017): problem, containment, root cause, actions, effectiveness check, closing by someone other than the owner.
// Everybody signed in may open it (R1q D13); the API shows it to the readers of the module, the owner of the CAPA and the owners of its actions.
const route = useRoute();
const router = useRouter();
const capa = ref<any>(null);
const busy = ref(false);
async function doPrint() {
  await nextTick();
  printForm();
}
const note = ref("");
const evidence = ref<string | null>(null);
const passed = ref(true);
const showCancel = ref(false);
const isNew = route.params.id === "new";
const me = currentUserId();
const canStaff = hasRole(QualityRoles.capaStaff);
const canClose = hasRole(QualityRoles.capaClosers);
const isQaManager = hasRole(QualityRoles.qaManager);
const editable = computed(() => canStaff && (isNew || (capa.value && capa.value.status <= 1)));
const status = computed(() => capa.value?.status);

// the day an action was really done: today or back to the back-date window of the company (never in the future); the API checks it
const { min: doneMin, max: doneMax, hint: doneHint, warning: doneWarning, load: loadWindow } = useBackdateWindow(false);
const doneDate = ref<string>(doneMax.value);

const isOwner = computed(() => me !== null && !!capa.value && capa.value.ownerUserId === me);
/** the API lets QA, QC and PMO finish any action, and the owner of the CAPA or of the action his own */
const canComplete = (a: any) => canStaff || (me !== null && (a.ownerUserId === me || isOwner.value));
const hasOpenActionForMe = computed(() => !!capa.value && capa.value.actions.some((a: any) => !a.doneAt && a.id && canComplete(a)));
const multiRound = computed(() => !!capa.value && (capa.value.iteration || 1) > 1);
const roundActions = computed(() => (capa.value ? capa.value.actions.filter((a: any) => (a.iteration || 1) === (capa.value.iteration || 1)) : []));
const readyToImplement = computed(() => roundActions.value.length > 0 && capa.value.actions.every((a: any) => !!a.doneAt));
/** the first day the effectiveness may be judged is still ahead (the API refuses earlier checks with the date) */
const tooEarly = computed(() => status.value === 2 && !!capa.value.effectivenessEarliest && capa.value.effectivenessEarliest > doneMax.value);
/** a CAPA a rule required is cancelled by the QA manager only */
const cancelBlocked = computed(() => !!capa.value && !!capa.value.mandatory && !isQaManager);
const canCancel = computed(() => canStaff && !!capa.value?.id && status.value < 4 && !cancelBlocked.value);
const escalationText = computed(() => {
  const level = capa.value?.escalationLevel || 0;
  if (level >= 2) return "دیرکرد طولانی: به تضمین کیفیت و مدیرعامل اطلاع داده شد";
  if (level === 1) return "دیرکرد: به تضمین کیفیت اطلاع داده شد";
  return "";
});

function load(dto: any) {
  capa.value = {
    ...dto,
    dueDate: dateInput(dto.dueDate),
    effectivenessDueDate: dateInput(dto.effectivenessDueDate),
    effectivenessEarliest: dateInput(dto.effectivenessEarliest),
    implementedAt: dateInput(dto.implementedAt),
    actions: (dto.actions || []).map((a: any) => ({ ...a, dueDate: dateInput(a.dueDate) })),
  };
}

function blank() {
  const q = route.query;
  return {
    id: null, rowVersion: null, status: 0, source: q.source ? Number(q.source) : 9, sourceId: q.sourceId ? Number(q.sourceId) : null, problem: String(q.problem || ""), containment: "",
    rootCauseMethod: 0, rootCauseAnalysis: "", ownerUserId: null, dueDate: null, effectivenessCriteria: "", actions: [] as any[],
    mandatory: false, iteration: 1, escalationLevel: 0,
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

/** Runs a CAPA action; true when the API accepted it. */
async function act(action: string, extra: any = {}): Promise<boolean> {
  busy.value = true;
  const [data, error] = await capaAction(capa.value.id, action, extra);
  busy.value = false;
  if (error) {
    fail(error);
    return false;
  }
  load(data);
  note.value = "";
  evidence.value = null;
  doneDate.value = doneMax.value;
  ok("انجام شد.");
  return true;
}

const completeAction = (a: any) => act("complete-action", { actionId: a.id, note: note.value, evidenceDocumentId: evidence.value, date: doneDate.value || null });

// R1q D21: cancelling asks for a written reason that stays on the record; a mandatory CAPA is cancelled by the QA manager only
async function cancelCapa(reason: string) {
  if (await act("cancel", { note: reason })) showCancel.value = false;
}

onMounted(async () => {
  loadWindow();
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
    <CapaPrint v-if="capa.id" :capa="capa" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">اقدام اصلاحی و پیشگیرانه <span v-if="capa.number" class="inv-ltr ms-2">{{ capa.number }}</span></h1>
          <span :class="{ ok: status === 4, bad: capa.overdue, warn: !capa.overdue && status < 4 }" class="inv-badge">{{ capaStatuses[status] }}</span>
          <span v-if="capa.overdue" class="inv-badge bad ms-1">از موعد گذشته</span>
          <span v-if="multiRound" class="inv-badge info ms-1" title="بررسی اثربخشی دور قبل تایید نشد و CAPA دوباره باز شد">دور {{ capa.iteration }}</span>
          <span v-if="escalationText" :class="capa.escalationLevel >= 2 ? 'bad' : 'warn'" class="inv-badge ms-1">{{ escalationText }}</span>
          <span v-if="capa.mandatory" class="inv-badge info ms-1" title="فقط مدیر QA می‌تواند آن را لغو کند">الزامی</span>
          <span v-if="capa.autoDrafted" class="inv-badge info ms-1">پیش‌نویس سیستم؛ تضمین کیفیت مسئول و موعد را تعیین کند</span>
        </div>
        <div class="d-flex gap-2">
          <router-link :to="{ name: 'QualityCapas' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button v-if="capa.id" class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ فرم FO-QA-017</button>
          <button v-if="editable" :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>

      <div v-if="status === 5" class="alert alert-secondary small" role="status">
        <strong>این CAPA لغو شد.</strong>
        <span v-if="capa.closedByName"> توسط {{ capa.closedByName }}</span>
        <span v-if="capa.cancelledAt"> در {{ jalali(capa.cancelledAt, true) }}</span>
        <div>دلیل لغو: {{ capa.cancelReason || capa.closeNote || "—" }}</div>
      </div>
      <div v-if="multiRound && status <= 1" class="alert alert-warning small" role="status">
        دور {{ capa.iteration }}: بررسی اثربخشی دور قبل تایید نشد{{ capa.effectivenessResult ? " (" + capa.effectivenessResult + ")" : "" }}. موعد تازه و اقدام‌های تازه تعریف کنید؛ اقدام‌های دورهای قبل به‌عنوان سابقه می‌مانند.
      </div>

      <div class="row g-2">
        <div class="col-md-3"><label class="form-label small mb-0">منبع</label><select v-model="capa.source" :disabled="!editable || !isNew" class="form-select form-select-sm"><option v-for="(t, k) in capaSources" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-3">
          <label class="form-label small mb-0">شناسه سند منبع</label>
          <input v-model.number="capa.sourceId" :disabled="!editable || !isNew" class="form-control form-control-sm" type="number" />
          <span v-if="capa.sourceNumber" class="inv-muted">سند: <span class="inv-ltr">{{ capa.sourceNumber }}</span></span>
        </div>
        <div class="col-md-6"><label class="form-label small mb-0">شرح مساله (عدم انطباق) *</label><textarea v-model="capa.problem" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-6"><label class="form-label small mb-0">اقدام مهار / اصلاح فوری</label><textarea v-model="capa.containment" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-2"><label class="form-label small mb-0">روش تحلیل علت</label><select v-model="capa.rootCauseMethod" :disabled="!editable" class="form-select form-select-sm"><option v-for="(t, k) in rootCauseMethods" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-4"><label class="form-label small mb-0">تحلیل علت ریشه‌ای</label><textarea v-model="capa.rootCauseAnalysis" :disabled="!editable" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-md-3"><label class="form-label small mb-0">مسئول CAPA</label><UserSelect v-model="capa.ownerUserId" :disabled="!editable" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">موعد</label><input v-model="capa.dueDate" :disabled="!editable" class="form-control form-control-sm" type="date" /></div>
        <div class="col-md-7"><label class="form-label small mb-0">معیار اثربخشی</label><input v-model="capa.effectivenessCriteria" :disabled="!editable" class="form-control form-control-sm" /></div>
        <div v-if="capa.implementedAt" class="col-md-2"><label class="form-label small mb-0">تاریخ اجرای اقدام‌ها</label><div class="small pt-1">{{ jalali(capa.implementedAt) }}</div></div>
        <div v-if="capa.effectivenessEarliest" class="col-md-3">
          <label class="form-label small mb-0">زودترین روز بررسی اثربخشی</label>
          <div class="small pt-1">{{ jalali(capa.effectivenessEarliest) }}</div>
        </div>
        <div v-if="capa.effectivenessDueDate" class="col-md-3"><label class="form-label small mb-0">موعد بررسی اثربخشی</label><div class="small pt-1">{{ jalali(capa.effectivenessDueDate) }}</div></div>
      </div>
      <p v-if="editable" class="inv-muted mt-2 mb-0">وقتی مسئول، موعد، روش و تحلیل علت، معیار اثربخشی و حداقل یک اقدام تکمیل باشد، وضعیت خودکار «اقدام‌ها تعریف شد» می‌شود.<template v-if="!isNew"> منبع CAPA پس از ثبت تغییر نمی‌کند؛ اگر اشتباه ثبت شده، آن را لغو و CAPA تازه‌ای ثبت کنید.</template></p>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">اقدام‌ها</h2>
        <button v-if="editable" class="btn btn-sm btn-outline-primary" @click="addAction">اقدام جدید</button>
      </div>
      <table class="table table-sm">
        <thead><tr><th v-if="multiRound">دور</th><th>شرح اقدام</th><th>مسئول</th><th>موعد</th><th>انجام شد</th><th>مدرک</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!capa.actions.length"><td :colspan="multiRound ? 7 : 6" class="text-center inv-muted">اقدامی تعریف نشده.</td></tr>
          <tr v-for="(a, i) in capa.actions" :key="a.id || 'n' + i">
            <template v-if="editable && !a.doneAt">
              <td v-if="multiRound">{{ a.iteration || capa.iteration }}</td>
              <td><input v-model="a.description" class="form-control form-control-sm" /></td>
              <td><UserSelect v-model="a.ownerUserId" /></td>
              <td><input v-model="a.dueDate" class="form-control form-control-sm" type="date" /></td>
              <td></td><td></td>
              <td class="text-nowrap">
                <!-- QA, QC and PMO finish any saved action of the round that is open (the same button the owner of an action gets in the read-only row) -->
                <button v-if="status === 1 && a.id && canComplete(a)" class="btn btn-sm btn-outline-success" :disabled="busy" @click="completeAction(a)">ثبت انجام</button>
                <button class="btn btn-sm btn-outline-danger ms-1" @click="capa.actions.splice(i, 1)">حذف</button>
              </td>
            </template>
            <template v-else>
              <td v-if="multiRound">{{ a.iteration || 1 }}</td>
              <td>{{ a.description }}</td><td>{{ a.ownerName || "—" }}</td>
              <td>{{ jalali(a.dueDate) }} <span v-if="a.overdue" class="inv-badge bad">گذشته</span></td>
              <td>{{ jalali(a.doneAt) }}</td>
              <td><span class="small">{{ a.evidenceNote }}</span> <span v-if="a.evidenceDocumentId" class="inv-badge ok">فایل</span></td>
              <td>
                <button v-if="status === 1 && !a.doneAt && canComplete(a)" class="btn btn-sm btn-outline-success" :disabled="busy" @click="completeAction(a)">ثبت انجام</button>
              </td>
            </template>
          </tr>
        </tbody>
      </table>
      <div v-if="status === 1 && hasOpenActionForMe" class="row g-2 align-items-end">
        <div class="col-md-5"><label class="form-label small mb-0">شرح انجام یا مدرک (برای اقدامی که «ثبت انجام» می‌زنید) *</label><input v-model="note" class="form-control form-control-sm" /></div>
        <div class="col-md-3">
          <label class="form-label small mb-0">تاریخ انجام</label>
          <input v-model="doneDate" :min="doneMin" :max="doneMax" class="form-control form-control-sm" type="date" />
          <span v-if="doneWarning(doneDate)" class="small text-danger">{{ doneWarning(doneDate) }}</span>
        </div>
        <div class="col-md-4"><DocAttach v-model="evidence" title="مدرک انجام اقدام اصلاحی" /></div>
        <p class="inv-muted small mb-0">{{ doneHint }} زمان انتظار بررسی اثربخشی از تاریخ آخرین اقدام همین دور حساب می‌شود.</p>
      </div>
    </div>

    <div v-if="capa.id" class="inv-card">
      <h2 class="inv-title mb-2">اجرا، اثربخشی و بستن</h2>
      <div class="d-flex flex-wrap gap-2 align-items-end">
        <button v-if="status === 1 && (canStaff || isOwner)" :disabled="busy || !readyToImplement" class="btn btn-sm btn-outline-primary" @click="act('implemented')">همه اقدام‌ها اجرا شد</button>
        <template v-if="status === 2 && canClose">
          <div class="flex-grow-1" style="min-width: 260px"><label class="form-label small mb-0">نتیجه بررسی اثربخشی (طبق معیار) *</label><input v-model="note" class="form-control form-control-sm" /></div>
          <label class="form-check small"><input v-model="passed" class="form-check-input" type="checkbox" /> اثربخش بود</label>
          <button :disabled="busy" class="btn btn-sm btn-success" @click="act('effectiveness', { note, passed })">ثبت اثربخشی</button>
        </template>
        <template v-if="status === 3 && canClose">
          <div class="flex-grow-1" style="min-width: 260px"><label class="form-label small mb-0">یادداشت بستن</label><input v-model="note" class="form-control form-control-sm" /></div>
          <button :disabled="busy" class="btn btn-sm btn-success" @click="act('close', { note })">بستن CAPA</button>
        </template>
        <button v-if="canCancel" :disabled="busy" class="btn btn-sm btn-outline-danger" @click="showCancel = true">لغو CAPA</button>
      </div>
      <p v-if="status === 1 && !readyToImplement" class="inv-muted small mt-2 mb-0">پس از انجام همه اقدام‌ها (و حداقل یک اقدام برای دور جاری)، «همه اقدام‌ها اجرا شد» فعال می‌شود.</p>
      <p v-if="status === 2" class="small mt-2 mb-0" :class="tooEarly ? 'text-danger' : 'inv-muted'">
        بررسی اثربخشی زودتر از {{ jalali(capa.effectivenessEarliest) }} ثبت نمی‌شود (چند روز پس از اجرای اقدام‌ها، تا اثر اقدام دیده شود).<template v-if="tooEarly"> هنوز زود است؛ سامانه ثبت زودتر را نمی‌پذیرد.</template>
        اگر اثربخش نبود، CAPA برای دور بعد (اقدام‌ها و موعد تازه) دوباره باز می‌شود.
      </p>
      <p v-if="cancelBlocked && status < 4" class="inv-muted small mt-2 mb-0">این CAPA الزامی است (از عدم انطباق، یافته ممیزی یا پیش‌نویس سیستم)؛ فقط مدیر تضمین کیفیت (QA) می‌تواند آن را با ثبت دلیل لغو کند.</p>
      <p class="inv-muted mt-2 mb-0">اثربخشی و بستن را تضمین کیفیت یا QC انجام می‌دهد، نه کسی که CAPA را تعریف یا ویرایش کرده یا مسئول CAPA یا یکی از اقدام‌های آن است؛ لغو CAPA هم همین‌طور.</p>
      <div v-if="capa.effectivenessResult" class="small mt-2">{{ multiRound && status < 3 ? "نتیجه بررسی اثربخشی دور قبل (تایید نشد)" : "نتیجه اثربخشی" }}: {{ capa.effectivenessResult }}</div>
      <div v-if="capa.closedAt && status === 4" class="small mt-1">بسته شد: {{ capa.closedByName }} {{ jalali(capa.closedAt, true) }} — {{ capa.closeNote }}</div>
    </div>

    <ReasonDialog
      v-if="showCancel"
      :busy="busy"
      :hint="(capa.mandatory ? 'این CAPA الزامی است؛ لغو آن فقط با دلیل مکتوب ممکن است و دلیل در پرونده می‌ماند. ' : 'دلیل لغو در پرونده می‌ماند. ') + 'کسی که CAPA را تعریف یا ویرایش کرده یا مسئول CAPA یا یکی از اقدام‌های آن است نمی‌تواند آن را لغو کند.'"
      confirm-text="لغو CAPA"
      label="دلیل لغو"
      title="لغو CAPA"
      @close="showCancel = false"
      @confirm="cancelCapa"
    />
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
