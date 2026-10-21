<script setup lang="ts">
// Picks a user of the system by name. When the account list cannot be read (no permission), the user id can be typed.
import { onMounted, ref } from "vue";
import { getUsers } from "@/api/QualityAPIs";
import { rowsOf } from "@/api/ApiClient";

defineProps({ modelValue: { type: Number, default: null }, disabled: Boolean, placeholder: { type: String, default: "-" } });
const emit = defineEmits(["update:modelValue"]);
const users = ref<any[]>([]);
const failed = ref(false);

onMounted(async () => {
  const [data, error] = await getUsers();
  if (error) {
    failed.value = true;
    return;
  }
  users.value = rowsOf(data);
  if (!users.value.length) failed.value = true;
});

const label = (u: any) => [u.name, u.lastName].filter(Boolean).join(" ") || u.userName;
const input = (v: any) => emit("update:modelValue", v === "" || v === null ? null : Number(v));
</script>

<template>
  <input v-if="failed" :disabled="disabled" :value="modelValue" class="form-control form-control-sm" placeholder="شناسه کاربر" type="number" @input="input(($event.target as HTMLInputElement).value)" />
  <select v-else :disabled="disabled" :value="modelValue" class="form-select form-select-sm" @change="input(($event.target as HTMLSelectElement).value)">
    <option :value="null">{{ placeholder }}</option>
    <option v-for="u in users" :key="u.id" :value="u.id">{{ label(u) }}</option>
  </select>
</template>
