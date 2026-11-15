<script setup lang="ts">
// The resource pool: people-roles, machines, equipment, materials and subcontractors a plan can use. Planning and the PMO edit it; the others read it.
// The rate column shows the approved rate of today, «محرمانه» when the API holds it back and «بدون نرخ مصوب» when there is none.
import { computed, onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import { getCalendarOptions, getResources, deleteResource, resourceInput, saveResource, setResourceActive } from "@/api/ResourceAPIs";
import type { CalendarOption, Resource, ResourceInput } from "@/api/ResourceAPIs";
import { apiErrorText } from "@/composables/apiMessage";
import { fail, ok } from "@/composables/inventory";
import { unitManagerTitles } from "@/composables/projectControl";
import { canEditPool, linkLabel, rateText, resourceKinds, resourceUnits, ResourceTexts, qty, validateResource } from "@/composables/resources";
import { hasRole, ProjectControlRoles } from "@/composables/roles";

const router = useRouter();
const rows = ref<Resource[]>([]);
const loaded = ref(false);
const busy = ref(false);
const kind = ref<number | null>(null);
const active = ref<string>("");
const contains = ref("");
const form = ref<ResourceInput | null>(null);
/** the refusal of the last save, shown inside the form (the form stays open) */
const formError = ref("");
const editing = ref<Resource | null>(null);
const calendars = ref<CalendarOption[]>([]);
const roleResources = ref<Resource[]>([]);
const canEdit = canEditPool();
const canRate = hasRole(ProjectControlRoles.rateMakers);

async function load() {
  const [data, error] = await getResources({ kind: kind.value, isActive: active.value === "" ? null : active.value === "1", contains: contains.value.trim() || null });
  loaded.value = true;
  if (error) return fail(error);
  rows.value = data;
}

async function openForm(r: Resource | null) {
  editing.value = r;
  formError.value = "";
  form.value = resourceInput(r);
  // the calendars and the role resources are read only when the form opens (only planning and the PMO open it)
  const [[cal], [roles]] = await Promise.all([getCalendarOptions(), getResources({ kind: 1, isActive: true })]);
  calendars.value = cal || [];
  roleResources.value = (roles || []).filter((x) => !x.employeeId && !x.rateResourceId && x.id !== r?.id);
}

// priority 4d: a machine linked to a work centre reads its capacity and calendar from the work centre (one truth with production); they are shown, not typed, and it is counted in hours
// only a machine counted in hours reads through: an old link of a machine counted in days or pieces stays editable with its own numbers, and a new or changed link is made in hours
const linkIsNew = computed(() => !editing.value || editing.value.kind !== 2 || editing.value.workCenterId !== form.value?.workCenterId);
const linkedMachine = computed(() => !!form.value && form.value.kind === 2 && !!form.value.workCenterId && (form.value.unit === 1 || linkIsNew.value));
watch(linkedMachine, (v) => { if (v && form.value && linkIsNew.value) form.value.unit = 1; });
const kindUnitLocked = computed(() => !!editing.value?.hasAssignments);
const linkText = computed(() => (form.value ? linkLabel(form.value.kind) : null));
// the link field of the kind: employee (labour), work center (machine), provider (subcontract)
const linkValue = computed<number | null>({
  get: () => (!form.value ? null : form.value.kind === 1 ? form.value.employeeId : form.value.kind === 2 ? form.value.workCenterId : form.value.kind === 5 ? form.value.providerId : null),
  set: (v) => {
    if (!form.value) return;
    const id = v ? Number(v) : null;
    form.value.employeeId = form.value.kind === 1 ? id : null;
    form.value.workCenterId = form.value.kind === 2 ? id : null;
    form.value.providerId = form.value.kind === 5 ? id : null;
    if (form.value.kind !== 1 || !id) form.value.rateResourceId = null;
  },
});
function kindChanged() {
  if (!form.value) return;
  form.value.employeeId = null;
  form.value.workCenterId = null;
  form.value.providerId = null;
  form.value.rateResourceId = null;
}

async function save() {
  if (!form.value) return;
  const problem = validateResource(form.value);
  formError.value = "";
  if (problem) {
    formError.value = problem;
    return;
  }
  busy.value = true;
  const [, error] = await saveResource({ ...form.value, code: form.value.code.trim(), title: form.value.title.trim(), responsibleUnit: form.value.responsibleUnit || null, notes: (form.value.notes || "").trim() || null });
  busy.value = false;
  if (error) {
    formError.value = apiErrorText(error);
    return;
  }
  ok("منبع ذخیره شد.");
  form.value = null;
  load();
}
async function toggle(r: Resource) {
  busy.value = true;
  const [, error] = await setResourceActive(r.id, !r.isActive);
  busy.value = false;
  if (error) return fail(error);
  ok(r.isActive ? "منبع غیرفعال شد." : "منبع فعال شد.");
  load();
}
async function remove(r: Resource) {
  if (!confirm(`منبع «${r.code}» حذف شود؟`)) return;
  busy.value = true;
  const [, error] = await deleteResource(r.id);
  busy.value = false;
  if (error) return fail(error);
  ok("منبع حذف شد.");
  load();
}
const toRates = (r: Resource) => router.push({ path: "/project-control/resources", query: { tab: "rates", resource: String(r.id) } });
const linkOf = (r: Resource) => (r.employeeId ? "کارمند #" + r.employeeId : r.workCenterId ? "مرکز کار #" + r.workCenterId : r.providerId ? "تامین‌کننده #" + r.providerId : "");

onMounted(load);
watch([kind, active], load);
</script>

<template>
  <div>
    <div class="inv-filters d-print-none">
      <select v-model.number="kind" class="form-select form-select-sm" style="max-width: 170px">
        <option :value="null">همه نوع‌ها</option>
        <option v-for="(n, k) in resourceKinds" :key="k" :value="Number(k)">{{ n }}</option>
      </select>
      <select v-model="active" class="form-select form-select-sm" style="max-width: 150px">
        <option value="">فعال و غیرفعال</option>
        <option value="1">فعال</option>
        <option value="0">غیرفعال</option>
      </select>
      <input v-model="contains" class="form-control form-control-sm" style="max-width: 220px" placeholder="جست‌وجو در کد و عنوان" @keyup.enter="load" />
      <button class="btn btn-sm btn-outline-secondary" @click="load">جست‌وجو</button>
      <button v-if="canEdit" class="btn btn-sm btn-primary ms-auto" @click="openForm(null)">منبع تازه</button>
    </div>

    <div class="table-responsive">
      <table class="table table-sm table-hover align-middle">
        <thead>
          <tr><th>کد</th><th>عنوان</th><th>نوع</th><th>واحد</th><th>ظرفیت</th><th>واحد مسئول</th><th>پیوند</th><th>نرخ مصوب امروز</th><th>وضعیت</th><th class="d-print-none"></th></tr>
        </thead>
        <tbody>
          <tr v-if="loaded && !rows.length"><td colspan="10" class="text-center inv-muted">منبعی ثبت نشده است.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.code }}</td>
            <td>{{ r.title }}</td>
            <td>{{ resourceKinds[r.kind] }}</td>
            <td>{{ resourceUnits[r.unit] }}</td>
            <td>{{ qty(r.capacity) }}<span v-if="r.capacityFromWorkCenter" class="inv-badge info ms-1" :title="'ظرفیت و تقویم از مرکز کار ' + (r.workCenterCode || '') + ' خوانده می‌شود'">از مرکز کار</span></td>
            <td>{{ r.responsibleUnit ? unitManagerTitles[r.responsibleUnit] || r.responsibleUnit : "—" }}</td>
            <td>
              <span v-if="linkOf(r)">{{ linkOf(r) }}</span><span v-else class="inv-muted">—</span>
              <div v-if="r.rateResourceCode" class="small inv-muted">نرخ از نقش <span class="inv-ltr">{{ r.rateResourceCode }}</span></div>
            </td>
            <td>
              <span :class="r.rateHidden ? 'inv-muted' : r.effectiveRate === null ? 'text-danger' : ''">{{ rateText(r.effectiveRate, r.rateHidden, r.unit) }}</span>
              <div v-if="r.rateResourceCode && !r.rateHidden && r.effectiveRate !== null" class="small inv-muted">نرخ نقش</div>
            </td>
            <td><span class="inv-badge" :class="r.isActive ? 'ok' : ''">{{ r.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td class="d-print-none text-nowrap">
              <template v-if="canEdit">
                <button class="btn btn-sm btn-outline-secondary" @click="openForm(r)">ویرایش</button>
                <button :disabled="busy" class="btn btn-sm btn-outline-secondary ms-1" @click="toggle(r)">{{ r.isActive ? "غیرفعال‌سازی" : "فعال‌سازی" }}</button>
                <button v-if="!r.hasAssignments" :disabled="busy" class="btn btn-sm btn-outline-danger ms-1" @click="remove(r)">حذف</button>
                <button v-else disabled :title="ResourceTexts.resourceUsed" class="btn btn-sm btn-outline-danger ms-1">حذف</button>
              </template>
              <button v-if="canRate && r.canCarryRate" class="btn btn-sm btn-outline-primary ms-1" @click="toRates(r)">نرخ</button>
              <button v-else-if="canRate" disabled :title="ResourceTexts.ratePrivacy" class="btn btn-sm btn-outline-primary ms-1">نرخ</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-if="canEdit" class="inv-muted small mb-0">منبعی که در طرح منابع به کار رفته حذف نمی‌شود، فقط غیرفعال می‌شود. منبع وصل‌شده به کارمند نرخ ندارد و نرخ «منبع نقش» مربوط را می‌گیرد (محرمانگی حقوق).</p>

    <div v-if="form" class="inv-modal-backdrop screen-only" @click.self="form = null">
      <div class="inv-modal wide">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش منبع" : "منبع تازه" }}</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small mb-0">کد *</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" maxlength="50" /></div>
          <div class="col-md-9"><label class="form-label small mb-0">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" maxlength="300" /></div>
          <div class="col-md-3">
            <label class="form-label small mb-0">نوع *</label>
            <select v-model.number="form.kind" :disabled="kindUnitLocked" :title="kindUnitLocked ? ResourceTexts.kindUnitLocked : ''" class="form-select form-select-sm" @change="kindChanged">
              <option v-for="(n, k) in resourceKinds" :key="k" :value="Number(k)">{{ n }}</option>
            </select>
          </div>
          <div class="col-md-3">
            <label class="form-label small mb-0">واحد شمارش *</label>
            <select v-model.number="form.unit" :disabled="kindUnitLocked || linkedMachine" :title="kindUnitLocked ? ResourceTexts.kindUnitLocked : ''" class="form-select form-select-sm">
              <option v-for="(n, k) in resourceUnits" :key="k" :value="Number(k)">{{ n }}</option>
            </select>
          </div>
          <div class="col-md-3"><label class="form-label small mb-0">ظرفیت *</label><input v-model.number="form.capacity" :disabled="linkedMachine" class="form-control form-control-sm" type="number" min="0" step="any" />
            <div v-if="linkedMachine" class="small inv-muted">از مرکز کار خوانده می‌شود{{ editing?.capacityFromWorkCenter ? ": " + qty(editing.capacity) : " (پس از ذخیره)" }}؛ ظرفیت = ماشین‌های موازی × کارایی × بهره‌وری.</div></div>
          <div class="col-md-3">
            <label class="form-label small mb-0">تقویم کاری</label>
            <select v-model="form.calendarId" :disabled="linkedMachine" class="form-select form-select-sm"><option :value="null">{{ linkedMachine ? "تقویم مرکز کار" : "تقویم پروژه" }}</option><option v-for="c in calendars" :key="c.id" :value="c.id">{{ c.name }}</option></select>
          </div>
          <div class="col-md-4">
            <label class="form-label small mb-0">واحد مسئول</label>
            <select v-model="form.responsibleUnit" class="form-select form-select-sm"><option :value="null">—</option><option v-for="(n, k) in unitManagerTitles" :key="k" :value="k">{{ n }}</option></select>
          </div>
          <div v-if="linkText" class="col-md-4"><label class="form-label small mb-0">{{ linkText }} (اختیاری)</label><input v-model.number="linkValue" class="form-control form-control-sm" type="number" min="1" /></div>
          <div v-if="form.kind === 1 && form.employeeId" class="col-md-4">
            <label class="form-label small mb-0">منبع نقش مبنای نرخ *</label>
            <select v-model="form.rateResourceId" class="form-select form-select-sm"><option :value="null">انتخاب کنید</option><option v-for="x in roleResources" :key="x.id" :value="x.id">{{ x.code }} - {{ x.title }}</option></select>
          </div>
          <div class="col-12"><label class="form-label small mb-0">یادداشت</label><textarea v-model="form.notes" class="form-control form-control-sm" rows="2" maxlength="1000"></textarea></div>
          <div v-if="form.id" class="col-12"><label class="small"><input v-model="form.isActive" type="checkbox" /> فعال</label></div>
        </div>
        <p v-if="kindUnitLocked" class="small text-danger mt-2 mb-0">{{ ResourceTexts.kindUnitLocked }}</p>
        <div v-if="formError" class="alert alert-danger py-2 mt-2 mb-0" role="alert">{{ formError }}</div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>
