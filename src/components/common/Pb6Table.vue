<script setup lang="ts">
// A plain table for the PB6 cartables: columns = [{ key, title, kind? }] where kind is "money", "date" or "text".
import { jalali, rial } from "@/composables/inventory";

defineProps<{ columns: { key: string; title: string; kind?: string }[]; rows: any[]; empty?: string }>();
defineEmits<{ (e: "select", row: any): void }>();

function cell(row: any, c: { key: string; kind?: string }) {
  const v = row[c.key];
  if (v === null || v === undefined) return "";
  if (c.kind === "money") return rial(v);
  if (c.kind === "date") return jalali(v);
  return v;
}
</script>

<template>
  <div class="table-responsive">
    <table class="table table-sm table-hover">
      <thead>
        <tr><th v-for="c in columns" :key="c.key">{{ c.title }}</th></tr>
      </thead>
      <tbody>
        <tr v-if="!rows.length"><td :colspan="columns.length" class="text-center inv-muted">{{ empty || "موردی نیست." }}</td></tr>
        <tr v-for="(r, i) in rows" :key="r.id ?? i" role="button" @click="$emit('select', r)">
          <td v-for="c in columns" :key="c.key" :class="{ num: c.kind === 'money' }">{{ cell(r, c) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
