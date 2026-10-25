<script setup lang="ts">
import { ref } from "vue";
import { saveOperationStandard } from "@/api/ProductionAPIs";
import { fail, ok } from "@/composables/inventory";
import { inspectionRequirements } from "@/composables/production";
import { testTypes } from "@/composables/quality";
import { blankToNull } from "./masterKit";

// Editor of the standard of one routing step (OperationTemplate). The service refuses a step without work centre unless it is outside, and ITP-point inspection needs a test type.
const props = defineProps<{ row: any; centers: any[]; providers: any[] }>();
const emit = defineEmits(["close", "saved"]);
const r = props.row;
const f = ref<any>({
  operationTemplateId: r.operationTemplateId, workCenterId: r.workCenterId ?? null, setupHours: r.setupHours ?? 0, runHoursPerUnit: r.runHoursPerUnit ?? 0, moveDays: r.moveDays ?? 0, queueDays: r.queueDays ?? 0,
  isOutside: !!r.isOutside, defaultSupplierId: r.defaultSupplierId ?? null, outsideLeadDays: r.outsideLeadDays ?? 0, workInstructionDocumentId: r.workInstructionDocumentId ?? null,
  drawingReferenceRule: r.drawingReferenceRule || "", requiredQualificationKind: r.requiredQualificationKind || "", requiredQualificationProcess: r.requiredQualificationProcess || "",
  inspectionRequirement: r.inspectionRequirement ?? 0, itpTestType: r.itpTestType ?? null, firstArticleRequired: !!r.firstArticleRequired,
});
const busy = ref(false);

async function save() {
  const x = f.value;
  if (!x.isOutside && !x.workCenterId) return fail("مرکز کار مرحله را انتخاب کنید (یا مرحله را «بیرونی» علامت بزنید).");
  if (x.inspectionRequirement === 3 && !x.itpTestType) return fail("برای بازرسی از نوع نقطه ITP، نوع آزمون را انتخاب کنید.");
  busy.value = true;
  const [, error] = await saveOperationStandard({
    ...x, workCenterId: x.workCenterId || null, defaultSupplierId: x.isOutside ? x.defaultSupplierId || null : null, outsideLeadDays: x.isOutside ? Number(x.outsideLeadDays) || 0 : 0,
    workInstructionDocumentId: x.workInstructionDocumentId ? Number(x.workInstructionDocumentId) : null, drawingReferenceRule: blankToNull(x.drawingReferenceRule),
    requiredQualificationKind: blankToNull(x.requiredQualificationKind), requiredQualificationProcess: blankToNull(x.requiredQualificationProcess),
    itpTestType: x.inspectionRequirement === 3 ? x.itpTestType : null,
  });
  busy.value = false;
  if (error) return fail(error);
  ok("استاندارد مرحله ذخیره شد.");
  emit("saved");
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal wide">
      <h2 class="inv-title mb-2">استاندارد مرحله {{ row.sequence }} - {{ row.templateTitle }}</h2>
      <div class="row g-2">
        <div class="col-md-6"><label class="form-label small mb-0">مرکز کار</label><select v-model="f.workCenterId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="c in centers" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option></select></div>
        <div class="col-6 col-md-3"><label class="form-label small mb-0">راه‌اندازی (ساعت)</label><input v-model.number="f.setupHours" class="form-control form-control-sm" type="number" min="0" step="0.05" /></div>
        <div class="col-6 col-md-3"><label class="form-label small mb-0">اجرا (ساعت بر واحد)</label><input v-model.number="f.runHoursPerUnit" class="form-control form-control-sm" type="number" min="0" step="0.01" /></div>
        <div class="col-6 col-md-3"><label class="form-label small mb-0">جابجایی (روز)</label><input v-model.number="f.moveDays" class="form-control form-control-sm" type="number" min="0" step="0.5" /></div>
        <div class="col-6 col-md-3"><label class="form-label small mb-0">صف (روز)</label><input v-model.number="f.queueDays" class="form-control form-control-sm" type="number" min="0" step="0.5" /></div>
        <div class="col-12 col-md-6 d-flex align-items-end"><label class="form-check"><input v-model="f.isOutside" class="form-check-input" type="checkbox" /> <span class="form-check-label">مرحله بیرونی (پیمانکار)</span></label></div>
        <template v-if="f.isOutside">
          <div class="col-md-8"><label class="form-label small mb-0">پیمانکار پیش‌فرض</label><select v-model="f.defaultSupplierId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="p in providers" :key="p.id" :value="p.id">{{ p.name }}</option></select></div>
          <div class="col-md-4"><label class="form-label small mb-0">زمان تحویل پیمانکار (روز)</label><input v-model.number="f.outsideLeadDays" class="form-control form-control-sm" type="number" min="0" /></div>
        </template>
        <div class="col-md-4"><label class="form-label small mb-0">شماره سند دستورالعمل کار</label><input v-model.number="f.workInstructionDocumentId" class="form-control form-control-sm" type="number" min="1" placeholder="اختیاری" /></div>
        <div class="col-md-8"><label class="form-label small mb-0">قاعده ارجاع به نقشه</label><input v-model="f.drawingReferenceRule" class="form-control form-control-sm" placeholder="مثال: نقشه ماشین‌کاری آخرین ویرایش" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">صلاحیت لازم (نوع)</label><input v-model="f.requiredQualificationKind" class="form-control form-control-sm" placeholder="مثال: جوشکار" /></div>
        <div class="col-md-6"><label class="form-label small mb-0">صلاحیت لازم (فرایند)</label><input v-model="f.requiredQualificationProcess" class="form-control form-control-sm" placeholder="مثال: GTAW" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">الزام بازرسی</label><select v-model="f.inspectionRequirement" class="form-select form-select-sm"><option v-for="(t, k) in inspectionRequirements" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div v-if="f.inspectionRequirement === 3" class="col-md-4"><label class="form-label small mb-0">نوع آزمون ITP</label><select v-model="f.itpTestType" class="form-select form-select-sm"><option :value="null">-</option><option v-for="(t, k) in testTypes" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-4 d-flex align-items-end"><label class="form-check"><input v-model="f.firstArticleRequired" class="form-check-input" type="checkbox" /> <span class="form-check-label">بازرسی نمونه اول</span></label></div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
    </div>
  </div>
</template>
