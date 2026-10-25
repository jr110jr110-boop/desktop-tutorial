<script setup lang="ts">
import { ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import { saveSlEstimate, slEstimateFromEquipment } from "@/api/SalesAPIs";
import { fail, ok } from "@/composables/inventory";
import { equipmentLabel, inquiryLabel, inquirySearch, numOrNull, searchEquipments } from "./pipelineKit";

// New estimate: drafted from equipment templates (BOM x cost sources, routing standards x work-centre rates) or an empty draft. Percentages stay empty until entered.
// The server accepts an estimate only for an inquiry the CEO decided to bid on (status "در حال تهیه پیشنهاد").
const props = defineProps({ inquiry: { type: Object, default: null } });
const emit = defineEmits(["close", "created"]);

const inquiry = ref<any>(props.inquiry);
const rows = ref<{ equipment: any; count: string }[]>([{ equipment: null, count: "1" }]);
const overhead = ref("");
const contingency = ref("");
const margin = ref("");
const mode = ref<"template" | "empty">("template");
const busy = ref(false);
const searchBidding = inquirySearch(4);

async function create() {
  if (!inquiry.value) return fail({ message: "استعلام را انتخاب کنید." });
  busy.value = true;
  let result: [any, any];
  if (mode.value === "template") {
    const equipments = rows.value.filter((r) => r.equipment).map((r) => ({ equipmentId: r.equipment.id, count: Math.max(1, Math.round(numOrNull(r.count) || 1)) }));
    if (!equipments.length) {
      busy.value = false;
      return fail({ message: "دست‌کم یک قالب تجهیزات انتخاب کنید." });
    }
    result = await slEstimateFromEquipment({ inquiryId: inquiry.value.id, equipments, overheadPercent: numOrNull(overhead.value), contingencyPercent: numOrNull(contingency.value), targetMarginPercent: numOrNull(margin.value) });
  } else {
    result = await saveSlEstimate({ inquiryId: inquiry.value.id, note: null, overheadPercent: numOrNull(overhead.value), contingencyPercent: numOrNull(contingency.value), targetMarginPercent: numOrNull(margin.value), lines: [], risks: [] });
  }
  busy.value = false;
  if (result[1]) return fail(result[1]);
  ok("برآورد پیش‌نویس ساخته شد.");
  emit("created", result[0]);
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 720px">
      <h2 class="inv-title mb-2">برآورد جدید</h2>
      <div class="row g-2">
        <div class="col-12"><label class="form-label small mb-0">استعلام (فقط استعلام‌های در حال تهیه پیشنهاد)</label><LookupBox v-model="inquiry" :fetch="searchBidding" :label="inquiryLabel" :disabled="!!props.inquiry" /></div>
        <div class="col-12 d-flex gap-3">
          <label class="form-check"><input v-model="mode" class="form-check-input" type="radio" value="template" /> <span class="form-check-label">از قالب تجهیزات</span></label>
          <label class="form-check"><input v-model="mode" class="form-check-input" type="radio" value="empty" /> <span class="form-check-label">پیش‌نویس خالی</span></label>
        </div>
        <template v-if="mode === 'template'">
          <div v-for="(r, i) in rows" :key="i" class="col-12 d-flex gap-2 align-items-center">
            <div class="flex-grow-1"><LookupBox v-model="r.equipment" :fetch="searchEquipments" :label="equipmentLabel" placeholder="قالب تجهیزات" /></div>
            <input v-model="r.count" class="form-control form-control-sm inv-ltr" style="max-width: 90px" inputmode="numeric" placeholder="تعداد" />
            <button v-if="rows.length > 1" class="btn btn-sm btn-outline-danger" @click="rows.splice(i, 1)">حذف</button>
          </div>
          <div class="col-12"><button class="btn btn-sm btn-outline-secondary" @click="rows.push({ equipment: null, count: '1' })">قالب دیگر</button></div>
        </template>
        <div class="col-md-4"><label class="form-label small mb-0">سربار (درصد)</label><input v-model="overhead" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">پیش‌بینی‌نشده (درصد)</label><input v-model="contingency" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
        <div class="col-md-4"><label class="form-label small mb-0">حاشیه سود هدف (درصد)</label><input v-model="margin" class="form-control form-control-sm inv-ltr" inputmode="decimal" /></div>
        <div class="col-12 small text-muted">درصدها را خودتان وارد کنید؛ تا زمان ورود، قیمت محاسبه نمی‌شود.</div>
      </div>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ساخت برآورد</button></div>
    </div>
  </div>
</template>
