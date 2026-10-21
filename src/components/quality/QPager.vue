<script setup lang="ts">
// Previous / next buttons of a paged list.
defineProps({ page: { type: Number, required: true }, total: { type: Number, required: true }, pageSize: { type: Number, default: 50 } });
const emit = defineEmits(["go"]);
</script>

<template>
  <div class="inv-pager d-print-none">
    <button :disabled="page <= 1" class="btn btn-sm btn-outline-secondary" @click="emit('go', -1)">قبلی</button>
    <span class="small">صفحه {{ page }} از {{ Math.max(1, Math.ceil(total / pageSize)) }} ({{ total }} مورد)</span>
    <button :disabled="page >= Math.ceil(total / pageSize)" class="btn btn-sm btn-outline-secondary" @click="emit('go', 1)">بعدی</button>
  </div>
</template>
