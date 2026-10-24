<script setup lang="ts">
// Registers a deputy (نیابت) for a position. Used by the delegations tab and by "my office". The API decides who may (holder: approved at once; HR / PMO / CEO: others).
import { ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import { saveDelegation } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";
import PositionSelect from "./PositionSelect.vue";
import UserPick from "./UserPick.vue";

const props = defineProps({
  /** limit the positions to these ids (my own positions) */
  only: { type: Array as () => number[] | null, default: null },
  positionId: { type: Number, default: null },
});
const emit = defineEmits(["close", "saved"]);
const f = ref({ positionId: props.positionId as number | null, deputyUserId: null as number | null, fromDate: null as string | null, toDate: null as string | null, reason: "" });
const busy = ref(false);

async function save() {
  busy.value = true;
  const [d, error] = await saveDelegation({ ...f.value });
  busy.value = false;
  if (error) return fail(error);
  ok(d?.status === 1 ? "نیابت ثبت شد و منتظر تایید دارنده سمت یا مدیرعامل است." : "نیابت ثبت شد.");
  emit("saved");
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal">
      <h2 class="inv-title mb-2">ثبت نیابت (جانشین)</h2>
      <div class="row g-2">
        <div class="col-12"><label class="form-label small mb-0">سمت</label><PositionSelect v-model="f.positionId" :only="only" /></div>
        <div class="col-12"><label class="form-label small mb-0">جانشین (کاربر)</label><UserPick v-model="f.deputyUserId" /></div>
        <div class="col-6"><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="f.fromDate" /></div>
        <div class="col-6"><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="f.toDate" /></div>
        <div class="col-12"><label class="form-label small mb-0">علت نیابت</label><textarea v-model="f.reason" class="form-control form-control-sm" rows="2" placeholder="مثلا ماموریت یا مرخصی"></textarea></div>
      </div>
      <p class="inv-muted small mt-2 mb-0">نیابتی که دارنده سمت ثبت می‌کند بی‌درنگ فعال می‌شود؛ نیابتی که دیگری ثبت کند تا تایید دارنده سمت یا مدیرعامل دسترسی نمی‌دهد.</p>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ثبت</button></div>
    </div>
  </div>
</template>
