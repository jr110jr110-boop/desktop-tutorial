<script setup lang="ts">
// Refer a letter to positions: instruction, kind (action / information / comment) and due date.
import { computed, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PickSelect from "./PickSelect.vue";
import { referLetter } from "@/api/OfficeAPIs";
import { referralActions } from "@/composables/office";
import { fail, ok } from "@/composables/inventory";
import { positionText, type MyPosition } from "./letterKit";

const props = defineProps<{ letterId: number; parentId?: number | null; positions: any[]; myPositions: MyPosition[]; actingDefault?: number | null }>();
const emit = defineEmits(["done", "cancel"]);
const targets = ref<number[]>([]);
const pick = ref<number | null>(null);
const instruction = ref("");
const action = ref(1);
const dueDate = ref<string | null>(null);
const fromPositionId = ref<number | null>(props.actingDefault ?? (props.myPositions.length === 1 ? props.myPositions[0].positionId : null));
const busy = ref(false);
const options = computed(() => props.positions.filter((p) => !targets.value.includes(p.id)).map((p) => ({ value: p.id, text: positionText(p) })));
const titleOf = (id: number) => positionText(props.positions.find((p) => p.id === id)) || String(id);

function add() {
  if (pick.value && !targets.value.includes(pick.value)) targets.value.push(pick.value);
  pick.value = null;
}
async function submit() {
  if (!targets.value.length) return fail("حداقل یک سمت را برای ارجاع انتخاب کنید.");
  if (!instruction.value.trim()) return fail("دستور یا توضیح ارجاع را بنویسید.");
  busy.value = true;
  const [, error] = await referLetter({ letterId: props.letterId, parentReferralId: props.parentId || null, fromPositionId: fromPositionId.value, toPositionIds: targets.value, instruction: instruction.value.trim(), action: action.value, dueDate: dueDate.value });
  busy.value = false;
  if (error) return fail(error);
  ok("ارجاع ثبت شد.");
  emit("done");
}
</script>

<template>
  <div class="inv-modal-backdrop screen-only" @click.self="emit('cancel')">
    <div class="inv-modal">
      <h2 class="inv-title mb-2">ارجاع نامه</h2>
      <div v-if="myPositions.length > 1" class="mb-2">
        <label class="form-label small mb-0">ارجاع از طرف سمت</label>
        <select v-model.number="fromPositionId" class="form-select form-select-sm"><option :value="null">انتخاب سمت</option><option v-for="p in myPositions" :key="p.positionId" :value="p.positionId">{{ positionText(p) }}</option></select>
      </div>
      <label class="form-label small mb-0">ارجاع به *</label>
      <div class="d-flex gap-2 align-items-start mb-1">
        <div style="flex: 1"><PickSelect v-model="pick" :options="options" placeholder="انتخاب سمت" /></div>
        <button class="btn btn-sm btn-outline-primary" @click="add">افزودن</button>
      </div>
      <div class="mb-2">
        <span v-for="id in targets" :key="id" class="inv-badge info me-1">{{ titleOf(id) }} <a href="#" class="text-danger" @click.prevent="targets = targets.filter((x) => x !== id)">×</a></span>
      </div>
      <div class="row g-2 mb-2">
        <div class="col-md-6"><label class="form-label small mb-0">نوع ارجاع</label><select v-model.number="action" class="form-select form-select-sm"><option v-for="(t, k) in referralActions" :key="k" :value="Number(k)">{{ t }}</option></select></div>
        <div class="col-md-6"><label class="form-label small mb-0">مهلت</label><FnDate v-model="dueDate" /></div>
      </div>
      <label class="form-label small mb-0">دستور / توضیح *</label>
      <textarea v-model="instruction" class="form-control form-control-sm" rows="3"></textarea>
      <div class="d-flex gap-2 justify-content-end mt-3">
        <button class="btn btn-sm btn-outline-secondary" @click="emit('cancel')">انصراف</button>
        <button :disabled="busy" class="btn btn-sm btn-primary" @click="submit">ارجاع</button>
      </div>
    </div>
  </div>
</template>
