<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { createBooking, editBooking, getProductionMachines } from "@/api/ProductionAPIs";
import { normalizeTime, stamp } from "@/components/office/att/attCommon";
import { parseAmount } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { bookingKinds, timeOf } from "@/composables/production";
import { employeeLabel, loadReasons, operationLabel, rowsOf, searchEmployees, searchOperations } from "./floorCommon";

// Supervisor booking for another person (create) and correction of a booking (edit; after the day is closed only SHOPSUPERVISOR, the server checks it).
// A reason is always required. A booking whose cost is posted is refused by the server with its own message.
const props = defineProps({ mode: { type: String as () => "create" | "edit", required: true }, row: { type: Object, default: null } });
const emit = defineEmits(["close", "saved"]);

const r = props.row;
const day = (v: string | null | undefined) => (v ? String(v).slice(0, 10) : null);
const f = ref({
  employee: null as any,
  operation: null as any,
  kind: r?.kind ?? 2,
  date: day(r?.startAt) as string | null,
  startTime: r ? timeOf(r.startAt) : "",
  endDate: day(r?.endAt) as string | null,
  endTime: r ? timeOf(r.endAt) : "",
  machineId: (r?.machineId ?? null) as number | null,
  good: String(r?.goodQty ?? 0),
  scrap: String(r?.scrapQty ?? 0),
  rework: String(r?.reworkQty ?? 0),
  scrapReasonId: (r?.scrapReasonCodeId ?? null) as number | null,
  note: r?.note || "",
  reason: "",
});
const machines = ref<any[]>([]);
const scrapReasons = ref<any[]>([]);
const busy = ref(false);

async function save() {
  const startTime = normalizeTime(f.value.startTime);
  const endTime = normalizeTime(f.value.endTime);
  if (!f.value.reason.trim()) return fail({ message: "دلیل را بنویسید." });
  if (!f.value.date || !startTime || !endTime) return fail({ message: "تاریخ و ساعت شروع و پایان را وارد کنید." });
  const startAt = stamp(f.value.date, startTime);
  const endAt = stamp(f.value.endDate || f.value.date, endTime);
  const common = { kind: f.value.kind, startAt, endAt, machineId: f.value.machineId, goodQty: parseAmount(f.value.good) || 0, scrapQty: parseAmount(f.value.scrap) || 0, reworkQty: parseAmount(f.value.rework) || 0, scrapReasonCodeId: f.value.scrapReasonId, note: f.value.note, reason: f.value.reason };
  busy.value = true;
  let result: [any, any];
  if (props.mode === "create") {
    if (!f.value.employee || !f.value.operation) {
      busy.value = false;
      return fail({ message: "کارمند و مرحله را انتخاب کنید." });
    }
    result = await createBooking({ ...common, operationId: f.value.operation.id, employeeId: f.value.employee.id });
  } else result = await editBooking({ ...common, id: r.id, rowVersion: r.rowVersion ?? null });
  busy.value = false;
  if (result[1]) return fail(result[1]);
  ok(props.mode === "create" ? "ثبت زمان انجام شد." : "ثبت زمان اصلاح شد.");
  emit("saved");
}

onMounted(async () => {
  const reasons = await loadReasons();
  scrapReasons.value = reasons.filter((x) => x.kind === 1);
  const [d] = await getProductionMachines({ PageSize: 300 });
  machines.value = rowsOf(d);
});
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal wide">
      <h2 class="inv-title mb-2">{{ mode === "create" ? "ثبت زمان برای یک کارمند (سرپرست)" : r.dayClosed ? "اصلاح ثبت زمان روز بسته‌شده" : "ویرایش ثبت زمان" }}</h2>
      <div class="row g-2">
        <template v-if="mode === 'create'">
          <div class="col-md-6"><label class="form-label small mb-0">کارمند</label><LookupBox v-model="f.employee" :fetch="searchEmployees" :label="employeeLabel" placeholder="نام کارمند" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">مرحله (برگه کار)</label><LookupBox v-model="f.operation" :fetch="searchOperations" :label="operationLabel" placeholder="شماره برگه کار" /></div>
        </template>
        <div v-else class="col-12 small"><b>{{ r.employeeName }}</b> - <span class="inv-ltr">{{ r.jobCard }}</span> {{ r.operationTitle }}</div>
        <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model="f.kind" class="form-select form-select-sm"><option v-for="(t, k) in bookingKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-4"><label class="form-label small mb-0">ماشین</label><select v-model="f.machineId" class="form-select form-select-sm"><option :value="null">بدون ماشین</option><option v-for="m in machines" :key="m.machineId" :value="m.machineId">{{ m.title }}</option></select></div>
        <div class="col-md-4"></div>
        <div class="col-md-4"><label class="form-label small mb-0">تاریخ شروع</label><FnDate v-model="f.date" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">ساعت شروع</label><input v-model="f.startTime" class="form-control form-control-sm inv-ltr" placeholder="08:00" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">تاریخ پایان (خالی = همان روز)</label><FnDate v-model="f.endDate" /></div>
        <div class="col-md-2"><label class="form-label small mb-0">ساعت پایان</label><input v-model="f.endTime" class="form-control form-control-sm inv-ltr" placeholder="16:30" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">سالم</label><input v-model="f.good" class="form-control form-control-sm fn-num" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">ضایعات</label><input v-model="f.scrap" class="form-control form-control-sm fn-num" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">دوباره‌کاری</label><input v-model="f.rework" class="form-control form-control-sm fn-num" /></div>
        <div class="col-md-3"><label class="form-label small mb-0">دلیل ضایعات</label><select v-model="f.scrapReasonId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="s in scrapReasons" :key="s.id" :value="s.id">{{ s.title }}</option></select></div>
        <div class="col-12"><label class="form-label small mb-0">توضیح</label><input v-model="f.note" class="form-control form-control-sm" /></div>
        <div class="col-12"><label class="form-label small mb-0">دلیل {{ mode === "create" ? "ثبت برای دیگری" : "اصلاح" }} (الزامی)</label><input v-model="f.reason" class="form-control form-control-sm" /></div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
      </div>
    </div>
  </div>
</template>
