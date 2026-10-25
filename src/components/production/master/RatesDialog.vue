<script setup lang="ts">
import { ref } from "vue";
import { saveWorkCenterRates } from "@/api/ProductionAPIs";
import { parseAmount } from "@/composables/fn";
import { fail, ok, rial } from "@/composables/inventory";

// Hourly rates of a work centre (rial). Only finance reaches this dialog; the service checks the role again.
const props = defineProps<{ center: any }>();
const emit = defineEmits(["close", "saved"]);
const machine = ref(props.center.machineRateRial === null || props.center.machineRateRial === undefined ? "" : rial(props.center.machineRateRial));
const labour = ref(props.center.labourRateRial === null || props.center.labourRateRial === undefined ? "" : rial(props.center.labourRateRial));
const busy = ref(false);

async function save() {
  const m = String(machine.value).trim() === "" ? null : parseAmount(machine.value);
  const l = String(labour.value).trim() === "" ? null : parseAmount(labour.value);
  if ((String(machine.value).trim() !== "" && m === null) || (String(labour.value).trim() !== "" && l === null)) return fail("مبلغ نرخ معتبر نیست.");
  busy.value = true;
  const [, error] = await saveWorkCenterRates({ id: props.center.id, machineRateRial: m, labourRateRial: l });
  busy.value = false;
  if (error) return fail(error);
  ok("نرخ‌ها ذخیره شد.");
  emit("saved");
}
</script>

<template>
  <div class="inv-modal-backdrop" @click.self="emit('close')">
    <div class="inv-modal" style="max-width: 460px">
      <h2 class="inv-title mb-2">نرخ‌های ساعتی - {{ center.title }}</h2>
      <div class="mb-2"><label class="form-label small mb-0">نرخ ساعتی ماشین (ریال)</label><input v-model="machine" class="form-control form-control-sm fn-num" inputmode="numeric" /></div>
      <div class="mb-2"><label class="form-label small mb-0">نرخ ساعتی نیروی کار استاندارد (ریال)</label><input v-model="labour" class="form-control form-control-sm fn-num" inputmode="numeric" /></div>
      <p class="inv-muted mb-0">این نرخ‌ها در محاسبه هزینه ساخت استفاده می‌شود و به اپراتور و سرپرست نمایش داده نمی‌شود.</p>
      <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="emit('close')">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
    </div>
  </div>
</template>
