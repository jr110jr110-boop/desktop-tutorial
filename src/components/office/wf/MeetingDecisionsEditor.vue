<script setup lang="ts">
// Decisions of the minutes: text, owner position, Persian due date, agenda item. Finished decisions (done / cancelled) are read-only and are not sent back.
import FnDate from "@/components/fn/FnDate.vue";
import { decisionStatuses } from "@/composables/office";
import PositionSelect from "./PositionSelect.vue";

const props = defineProps({ list: { type: Array as () => any[], required: true }, agenda: { type: Array as () => any[], required: true }, readOnly: Boolean });
const add = () => props.list.push({ id: null, text: "", ownerPositionId: null, dueDate: null, agendaSeq: null, status: 0 });
const locked = (d: any) => props.readOnly || (d.id && d.status !== 0);
</script>

<template>
  <div>
    <div v-for="(d, i) in list" :key="i" class="border rounded p-2 mb-2">
      <div class="row g-2">
        <div class="col-12 col-md-6"><label class="form-label small mb-0">متن مصوبه</label><textarea v-model="d.text" :disabled="locked(d)" class="form-control form-control-sm" rows="2"></textarea></div>
        <div class="col-12 col-md-3"><label class="form-label small mb-0">مسئول اجرا (سمت)</label><PositionSelect v-model="d.ownerPositionId" :disabled="locked(d)" /></div>
        <div class="col-6 col-md-2"><label class="form-label small mb-0">مهلت</label><FnDate :model-value="d.dueDate ? String(d.dueDate).slice(0, 10) : null" :disabled="locked(d)" @update:model-value="d.dueDate = $event" /></div>
        <div class="col-6 col-md-3">
          <label class="form-label small mb-0">بند دستور جلسه</label>
          <select v-model="d.agendaSeq" :disabled="locked(d)" class="form-select form-select-sm"><option :value="null">بدون بند</option><option v-for="(a, k) in agenda" :key="k" :value="k + 1">{{ k + 1 }}. {{ a.title }}</option></select>
        </div>
        <div class="col-6 col-md-1 d-flex align-items-end">
          <span v-if="d.id && d.status !== 0" class="inv-badge ok">{{ decisionStatuses[d.status] }}</span>
          <button v-else-if="!readOnly" class="btn btn-sm btn-outline-danger" @click="list.splice(i, 1)">حذف</button>
        </div>
      </div>
    </div>
    <button v-if="!readOnly" class="btn btn-sm btn-outline-primary" @click="add">مصوبه جدید</button>
    <span v-if="!list.length && readOnly" class="inv-muted small">مصوبه‌ای ثبت نشده است.</span>
  </div>
</template>
