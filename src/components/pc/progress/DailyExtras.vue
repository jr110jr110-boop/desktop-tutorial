<script setup lang="ts">
import { hseKinds, machineOwnerships, manpowerKinds } from "@/composables/tasks";

// The extra sections of the daily site report (appendix 5): manpower, materials, machinery, obstacles, HSE events.
// The lists are edited in place on the report object; the parent saves and confirms (confirmer is not the author or an editor).
const props = defineProps({ sub: { type: String, default: "manpower" }, rep: { type: Object, required: true }, locked: Boolean, activities: { type: Array as () => any[], default: () => [] } });
const emit = defineEmits(["update:sub"]);
const subs = [["manpower", "نیروی انسانی"], ["materials", "مصالح مصرفی"], ["machinery", "ماشین‌آلات"], ["obstacles", "موانع و مشکلات"], ["hse", "حوادث HSE"]];
const add = (key: string, row: any) => { if (!props.rep[key]) props.rep[key] = []; props.rep[key].push(row); };
const del = (key: string, i: number) => props.rep[key].splice(i, 1);
</script>

<template>
  <div>
    <div class="row g-2 mb-2">
      <div class="col-md-3"><label class="form-label small mb-0">وضعیت هوا</label><input v-model="rep.weather" :disabled="locked" class="form-control form-control-sm" maxlength="100" /></div>
      <div class="col-md-9"><label class="form-label small mb-0">توضیحات کلی</label><input v-model="rep.notes" :disabled="locked" class="form-control form-control-sm" maxlength="4000" /></div>
    </div>
    <ul class="nav nav-pills mb-2 d-print-none"><li v-for="s in subs" :key="s[0]" class="nav-item"><a :class="{ active: sub === s[0] }" class="nav-link py-1" href="#" @click.prevent="emit('update:sub', s[0])">{{ s[1] }}</a></li></ul>

    <div v-if="sub === 'manpower'" class="table-responsive">
      <table class="table table-sm"><thead><tr><th>نوع</th><th>سمت / پیمانکار</th><th>نوع فعالیت</th><th style="width: 80px">کل</th><th style="width: 80px">حاضر</th><th style="width: 80px">غایب</th><th style="width: 90px">نفر-روز</th><th>توضیح</th><th></th></tr></thead>
        <tbody>
          <tr v-for="(m, i) in rep.manpower" :key="i">
            <td><select v-model.number="m.kind" :disabled="locked" class="form-select form-select-sm"><option v-for="(t, k) in manpowerKinds" :key="k" :value="Number(k)">{{ t }}</option></select></td>
            <td><input v-model="m.title" :disabled="locked" class="form-control form-control-sm" /></td><td><input v-model="m.activityType" :disabled="locked" class="form-control form-control-sm" /></td>
            <td><input v-model.number="m.count" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model.number="m.present" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model.number="m.absent" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td>
            <td><input v-model.number="m.personDays" :disabled="locked || m.kind === 1" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model="m.note" :disabled="locked" class="form-control form-control-sm" /></td>
            <td><button v-if="!locked" class="btn btn-sm btn-outline-danger d-print-none" @click="del('manpower', i)">×</button></td>
          </tr>
          <tr v-if="!rep.manpower?.length"><td colspan="9" class="text-center inv-muted">ردیفی نیست.</td></tr>
        </tbody></table>
      <button v-if="!locked" class="btn btn-sm btn-outline-primary d-print-none" @click="add('manpower', { kind: 1, title: '', activityType: '', count: 0, present: 0, absent: 0, personDays: 0, note: '' })">+ ردیف</button>
    </div>

    <div v-else-if="sub === 'materials'" class="table-responsive">
      <table class="table table-sm"><thead><tr><th>شرح مصالح</th><th style="width: 100px">واحد</th><th style="width: 120px">مقدار</th><th>توضیح</th><th></th></tr></thead>
        <tbody>
          <tr v-for="(m, i) in rep.materials" :key="i"><td><input v-model="m.description" :disabled="locked" class="form-control form-control-sm" /></td><td><input v-model="m.unit" :disabled="locked" class="form-control form-control-sm" /></td><td><input v-model.number="m.quantity" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model="m.note" :disabled="locked" class="form-control form-control-sm" /></td><td><button v-if="!locked" class="btn btn-sm btn-outline-danger d-print-none" @click="del('materials', i)">×</button></td></tr>
          <tr v-if="!rep.materials?.length"><td colspan="5" class="text-center inv-muted">ردیفی نیست.</td></tr>
        </tbody></table>
      <button v-if="!locked" class="btn btn-sm btn-outline-primary d-print-none" @click="add('materials', { description: '', unit: '', quantity: 0, note: '' })">+ ردیف</button>
    </div>

    <div v-else-if="sub === 'machinery'" class="table-responsive">
      <table class="table table-sm"><thead><tr><th>ماشین / تجهیز</th><th>مالکیت</th><th style="width: 90px">حاضر</th><th style="width: 90px">فعال</th><th style="width: 100px">ساعت کار</th><th>توضیح</th><th></th></tr></thead>
        <tbody>
          <tr v-for="(m, i) in rep.machinery" :key="i"><td><input v-model="m.name" :disabled="locked" class="form-control form-control-sm" /></td>
            <td><select v-model.number="m.ownership" :disabled="locked" class="form-select form-select-sm"><option v-for="(t, k) in machineOwnerships" :key="k" :value="Number(k)">{{ t }}</option></select></td>
            <td><input v-model.number="m.present" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model.number="m.active" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model.number="m.hours" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model="m.note" :disabled="locked" class="form-control form-control-sm" /></td><td><button v-if="!locked" class="btn btn-sm btn-outline-danger d-print-none" @click="del('machinery', i)">×</button></td></tr>
          <tr v-if="!rep.machinery?.length"><td colspan="7" class="text-center inv-muted">ردیفی نیست.</td></tr>
        </tbody></table>
      <button v-if="!locked" class="btn btn-sm btn-outline-primary d-print-none" @click="add('machinery', { name: '', ownership: 1, present: 0, active: 0, hours: 0, note: '' })">+ ردیف</button>
    </div>

    <div v-else-if="sub === 'obstacles'" class="table-responsive">
      <table class="table table-sm"><thead><tr><th>دسته</th><th>شرح مانع</th><th>فعالیت</th><th style="width: 100px">ساعت اثر</th><th style="width: 80px">رفع شد</th><th></th></tr></thead>
        <tbody>
          <tr v-for="(o, i) in rep.obstacles" :key="i"><td><input v-model="o.category" :disabled="locked" class="form-control form-control-sm" /></td><td><input v-model="o.description" :disabled="locked" class="form-control form-control-sm" /></td>
            <td><select v-model.number="o.activityId" :disabled="locked" class="form-select form-select-sm"><option :value="null">-</option><option v-for="a in activities" :key="a.activityId" :value="a.activityId">{{ a.activityCode }}</option></select></td>
            <td><input v-model.number="o.impactHours" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model="o.resolved" :disabled="locked" class="form-check-input" type="checkbox" /></td><td><button v-if="!locked" class="btn btn-sm btn-outline-danger d-print-none" @click="del('obstacles', i)">×</button></td></tr>
          <tr v-if="!rep.obstacles?.length"><td colspan="6" class="text-center inv-muted">مانعی گزارش نشده.</td></tr>
        </tbody></table>
      <button v-if="!locked" class="btn btn-sm btn-outline-primary d-print-none" @click="add('obstacles', { category: '', description: '', activityId: null, impactHours: 0, resolved: false })">+ ردیف</button>
    </div>

    <div v-else class="table-responsive">
      <table class="table table-sm"><thead><tr><th>نوع</th><th>شرح</th><th style="width: 90px">تعداد درگیر</th><th>اقدام انجام‌شده</th><th></th></tr></thead>
        <tbody>
          <tr v-for="(h, i) in rep.hseEvents" :key="i"><td><select v-model.number="h.kind" :disabled="locked" class="form-select form-select-sm"><option v-for="(t, k) in hseKinds" :key="k" :value="Number(k)">{{ t }}</option></select></td><td><input v-model="h.description" :disabled="locked" class="form-control form-control-sm" /></td>
            <td><input v-model.number="h.personsInvolved" :disabled="locked" class="form-control form-control-sm" type="number" min="0" /></td><td><input v-model="h.actionTaken" :disabled="locked" class="form-control form-control-sm" /></td><td><button v-if="!locked" class="btn btn-sm btn-outline-danger d-print-none" @click="del('hseEvents', i)">×</button></td></tr>
          <tr v-if="!rep.hseEvents?.length"><td colspan="5" class="text-center inv-muted">رویداد ایمنی گزارش نشده.</td></tr>
        </tbody></table>
      <button v-if="!locked" class="btn btn-sm btn-outline-primary d-print-none" @click="add('hseEvents', { kind: 6, description: '', personsInvolved: 0, actionTaken: '' })">+ رویداد</button>
    </div>
  </div>
</template>
