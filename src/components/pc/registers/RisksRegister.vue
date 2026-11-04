<script setup lang="ts">
import { onMounted, reactive, ref, watch } from "vue";
import { rowsOf } from "@/api/ApiClient";
import { getHeatMap, getRisks, riskStatus, saveRisk } from "@/api/ProjectRegistersAPIs";
import JDate from "@/components/pc/JDate.vue";
import PositionSelect from "@/components/common/PositionSelect.vue";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ProjectControlRoles } from "@/composables/roles";
import { riskClass, riskLevels, riskResponses, riskStatuses } from "@/composables/tasks";

// Risk register (PMBOK): probability x impact 1..5, score, level from the settings, heat map, owner position, response, status, review date.
const props = defineProps({ contractId: { type: Number, required: true } });
const rows = ref<any[]>([]);
const heat = ref<any>(null);
const form = ref<any>(null);
const sel = ref<any>(null);
const stNote = reactive<any>({ status: 0, note: "" });
const canWrite = hasRole(ProjectControlRoles.readers);

async function load() {
  const [[d, e], [h]] = await Promise.all([getRisks({ ContractId: props.contractId, PageSize: 200 }), getHeatMap(props.contractId)]);
  if (e) return fail(e);
  rows.value = rowsOf(d);
  heat.value = h;
}
const cell = (p: number, i: number) => heat.value?.cells?.find((c: any) => c.probability === p && c.impact === i);
const cellClass = (p: number, i: number) => {
  const s = p * i;
  return s >= (heat.value?.criticalScore ?? 15) ? "bg-danger text-white" : s >= (heat.value?.highScore ?? 8) ? "bg-warning" : s >= 4 ? "bg-info-subtle" : "bg-success-subtle";
};
const blank = () => ({ id: null, contractId: props.contractId, title: "", cause: "", event: "", effect: "", category: "", isOpportunity: false, probability: 3, impact: 3, ownerPositionId: null, response: null, responsePlan: "", reviewDate: null, residualProbability: null, residualImpact: null });
const edit = (r: any) => (form.value = { ...r, reviewDate: r.reviewDate?.slice(0, 10) ?? null });
async function save() {
  const [, e] = await saveRisk(form.value);
  if (e) return fail(e);
  ok("ریسک ذخیره شد.");
  form.value = null;
  load();
}
async function setStatus() {
  const [, e] = await riskStatus({ id: sel.value.id, status: stNote.status, note: stNote.note });
  if (e) return fail(e);
  ok("وضعیت ثبت شد.");
  sel.value = null;
  load();
}
onMounted(load);
watch(() => props.contractId, load);
</script>

<template>
  <div>
    <div class="d-flex gap-3 flex-wrap mb-3">
      <div v-if="heat">
        <table class="text-center" style="border-collapse: separate; border-spacing: 2px">
          <tbody>
            <tr v-for="p in [5, 4, 3, 2, 1]" :key="p"><th class="small pe-1">{{ p }}</th><td v-for="i in [1, 2, 3, 4, 5]" :key="i" :class="cellClass(p, i)" style="width: 46px; height: 36px" :title="'احتمال ' + p + ' × اثر ' + i">{{ cell(p, i)?.count || "" }}</td></tr>
            <tr><th></th><th v-for="i in [1, 2, 3, 4, 5]" :key="i" class="small">{{ i }}</th></tr>
          </tbody>
        </table>
        <div class="small inv-muted">سطر = احتمال، ستون = اثر | تهدید باز: {{ heat.openThreats }} | فرصت باز: {{ heat.openOpportunities }}</div>
      </div>
      <div class="flex-grow-1"><button class="btn btn-sm btn-success d-print-none" @click="form = blank()">+ ریسک جدید</button></div>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>شماره</th><th>عنوان</th><th>دسته</th><th>احتمال × اثر</th><th>امتیاز</th><th>سطح</th><th>مالک</th><th>پاسخ</th><th>وضعیت</th><th>بازنگری</th><th>اقدام‌ها</th><th class="d-print-none"></th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="12" class="text-center inv-muted">ریسکی ثبت نشده.</td></tr>
          <tr v-for="r in rows" :key="r.id">
            <td class="inv-ltr">{{ r.number }}</td><td>{{ r.title }} <span v-if="r.isOpportunity" class="inv-badge ok">فرصت</span></td><td>{{ r.category }}</td><td>{{ r.probability }} × {{ r.impact }}</td><td>{{ r.score }}</td>
            <td><span :class="riskClass(r.level)" class="inv-badge">{{ riskLevels[r.level] }}</span></td><td>{{ r.ownerPositionTitle }}</td><td>{{ r.response ? riskResponses[r.response] : "" }}</td>
            <td>{{ riskStatuses[r.status] }}</td><td :class="{ 'text-danger': r.reviewOverdue }">{{ r.reviewDateText }}</td><td>{{ r.openActionCount }}/{{ r.actionCount }}</td>
            <td class="d-print-none text-nowrap"><button v-if="canWrite && r.status !== 4" class="btn btn-sm btn-outline-primary me-1" @click="edit(r)">ویرایش</button><button v-if="canWrite" class="btn btn-sm btn-outline-secondary" @click="sel = r; stNote.status = r.status; stNote.note = ''">وضعیت</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal wide">
        <h5>{{ form.id ? "ویرایش ریسک" : "ریسک جدید" }}</h5>
        <div class="row g-2">
          <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">دسته</label><input v-model="form.category" class="form-control form-control-sm" /></div>
          <div class="col-md-2 pt-4"><label><input v-model="form.isOpportunity" class="form-check-input" type="checkbox" /> فرصت</label></div>
          <div class="col-md-4"><label class="form-label small mb-0">علت</label><textarea v-model="form.cause" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-md-4"><label class="form-label small mb-0">رویداد</label><textarea v-model="form.event" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-md-4"><label class="form-label small mb-0">اثر</label><textarea v-model="form.effect" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-md-2"><label class="form-label small mb-0">احتمال (۱ تا ۵)</label><input v-model.number="form.probability" class="form-control form-control-sm" type="number" min="1" max="5" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">اثر (۱ تا ۵)</label><input v-model.number="form.impact" class="form-control form-control-sm" type="number" min="1" max="5" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">سمت مالک ریسک</label><PositionSelect v-model="form.ownerPositionId" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">راهبرد پاسخ</label><select v-model.number="form.response" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(t, k) in riskResponses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-8"><label class="form-label small mb-0">برنامه پاسخ (اقدام‌ها را به صورت وظیفه با پیوند ریسک ثبت کنید)</label><input v-model="form.responsePlan" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">تاریخ بازنگری</label><JDate v-model="form.reviewDate" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">احتمال باقی‌مانده</label><input v-model.number="form.residualProbability" class="form-control form-control-sm" type="number" min="1" max="5" /></div>
          <div class="col-md-2"><label class="form-label small mb-0">اثر باقی‌مانده</label><input v-model.number="form.residualImpact" class="form-control form-control-sm" type="number" min="1" max="5" /></div>
        </div>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-primary" @click="save">ذخیره</button><button class="btn btn-sm btn-light" @click="form = null">انصراف</button></div>
      </div>
    </div>
    <div v-if="sel" class="inv-modal-backdrop" @click.self="sel = null">
      <div class="inv-modal">
        <h5>وضعیت {{ sel.number }}</h5>
        <select v-model.number="stNote.status" class="form-select form-select-sm mb-2"><option v-for="(t, k) in riskStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <input v-model="stNote.note" class="form-control form-control-sm" placeholder="یادداشت (برای بستن، نتیجه را بنویسید)" />
        <div class="d-flex gap-2 mt-3"><button class="btn btn-sm btn-primary" @click="setStatus">ثبت</button><button class="btn btn-sm btn-light" @click="sel = null">انصراف</button></div>
      </div>
    </div>
  </div>
</template>
