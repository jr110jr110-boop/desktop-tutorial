<script setup lang="ts">
// The project (contract) the project control pages work on. The choice is shared by all the pages (composables/projectControl.ts).
import { onMounted } from "vue";
import { getContracts, rowsOf } from "@/api/InventoryAPIs";
import { contractsCache, pcContractId, setPcContract } from "@/composables/projectControl";

const props = defineProps({ allowAll: Boolean, placeholder: { type: String, default: "قرارداد / پروژه را انتخاب کنید" } });
const emit = defineEmits(["change"]);

onMounted(async () => {
  if (contractsCache.value.length) return;
  const [data] = await getContracts();
  contractsCache.value = rowsOf(data);
});

function pick(v: string) {
  const id = v === "" ? null : Number(v);
  setPcContract(id);
  emit("change", id);
}
</script>

<template>
  <select :value="pcContractId" class="form-select form-select-sm" style="min-width: 280px" @change="pick(($event.target as HTMLSelectElement).value)">
    <option :value="null">{{ allowAll ? "همه قراردادها" : props.placeholder }}</option>
    <option v-for="c in contractsCache" :key="c.id" :value="c.id">{{ c.code }} - {{ c.title }}</option>
  </select>
</template>
