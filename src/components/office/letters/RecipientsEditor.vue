<script setup lang="ts">
// Recipients of a letter: positions of the organisation or external parties, "to" or "cc". An external party can be added on the spot.
import { computed, ref, watch } from "vue";
import PickSelect from "./PickSelect.vue";
import { saveParty } from "@/api/OfficeAPIs";
import { partyKinds, recipientTypes } from "@/composables/office";
import { fail, ok } from "@/composables/inventory";
import { positionText } from "./letterKit";

const props = defineProps<{ modelValue: any[]; positions: any[]; parties: any[]; disabled?: boolean; kind?: number }>();
const emit = defineEmits(["update:modelValue", "party-created"]);
const draft = ref<{ type: number; target: "position" | "party"; id: number | null }>({ type: 1, target: "position", id: null });
// an incoming (1) or internal (3) letter has no external-party recipients
const noParty = computed(() => props.kind === 1 || props.kind === 3);
watch(noParty, (v) => { if (v) draft.value = { ...draft.value, target: "position", id: null }; }, { immediate: true });
const newParty = ref<{ name: string; kind: number } | null>(null);

const positionOptions = computed(() => props.positions.map((p) => ({ value: p.id, text: positionText(p) })));
const partyOptions = computed(() => props.parties.map((p) => ({ value: p.id, text: p.name })));
const textOf = (r: any) => (r.positionId ? positionText(props.positions.find((p) => p.id === r.positionId)) || r.positionTitle : props.parties.find((p) => p.id === r.partyId)?.name || r.partyName) || "";

function add() {
  if (!draft.value.id) return fail("گیرنده را انتخاب کنید.");
  const row = { type: draft.value.type, positionId: draft.value.target === "position" ? draft.value.id : null, partyId: draft.value.target === "party" ? draft.value.id : null };
  if (props.modelValue.some((r) => r.positionId === row.positionId && r.partyId === row.partyId)) return fail("این گیرنده قبلا اضافه شده است.");
  emit("update:modelValue", [...props.modelValue, row]);
  draft.value.id = null;
}
function remove(i: number) {
  emit("update:modelValue", props.modelValue.filter((_, k) => k !== i));
}
async function createParty() {
  const p = newParty.value!;
  if (!p.name.trim()) return fail("نام طرف مکاتبه را وارد کنید.");
  const [data, error] = await saveParty({ name: p.name.trim(), kind: p.kind, isActive: true });
  if (error) return fail(error);
  ok("طرف مکاتبه ثبت شد.");
  newParty.value = null;
  emit("party-created", data);
  if (data?.id) draft.value = { type: draft.value.type, target: "party", id: data.id };
}
</script>

<template>
  <div>
    <table v-if="modelValue.length" class="table table-sm mb-2">
      <thead><tr><th>نوع</th><th>گیرنده</th><th></th></tr></thead>
      <tbody>
        <tr v-for="(r, i) in modelValue" :key="i">
          <td>{{ recipientTypes[r.type] }}</td>
          <td>{{ textOf(r) }} <span v-if="r.partyId" class="inv-badge">خارج از سازمان</span></td>
          <td><button v-if="!disabled" class="btn btn-sm btn-link text-danger" @click="remove(i)">حذف</button></td>
        </tr>
      </tbody>
    </table>
    <p v-else class="inv-muted mb-2">گیرنده‌ای افزوده نشده است.</p>

    <div v-if="!disabled" class="d-flex gap-2 flex-wrap align-items-start">
      <select v-model.number="draft.type" class="form-select form-select-sm" style="width: 8rem"><option v-for="(t, k) in recipientTypes" :key="k" :value="Number(k)">{{ t }}</option></select>
      <select v-model="draft.target" class="form-select form-select-sm" style="width: 11rem" @change="draft.id = null"><option value="position">سمت سازمانی</option><option v-if="!noParty" value="party">طرف خارجی</option></select>
      <div style="min-width: 16rem; flex: 1">
        <PickSelect v-model="draft.id" :options="draft.target === 'position' ? positionOptions : partyOptions" placeholder="انتخاب گیرنده" />
      </div>
      <button class="btn btn-sm btn-outline-primary" @click="add">افزودن</button>
      <button v-if="draft.target === 'party'" class="btn btn-sm btn-outline-secondary" @click="newParty = { name: '', kind: 3 }">طرف خارجی جدید</button>
    </div>
    <div v-if="newParty" class="border rounded p-2 mt-2 d-flex gap-2 flex-wrap">
      <input v-model="newParty.name" class="form-control form-control-sm" placeholder="نام شرکت یا سازمان" style="max-width: 18rem" />
      <select v-model.number="newParty.kind" class="form-select form-select-sm" style="width: 9rem"><option v-for="(t, k) in partyKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
      <button class="btn btn-sm btn-primary" @click="createParty">ثبت</button>
      <button class="btn btn-sm btn-outline-secondary" @click="newParty = null">انصراف</button>
    </div>
  </div>
</template>
