<script setup lang="ts">
// One node of the as-built tree: { label, sub, tag?, children[] }; children are drawn nested.
defineProps({ node: { type: Object, required: true }, depth: { type: Number, default: 0 } });
</script>

<template>
  <li>
    <span :class="{ 'fw-bold': depth === 0 }">{{ node.label }}</span>
    <span v-if="node.sub" class="inv-muted"> ({{ node.sub }})</span>
    <span v-if="node.tag" class="inv-badge info ms-1">{{ node.tag }}</span>
    <ul v-if="node.children && node.children.length" class="svb-tree">
      <AsBuiltNode v-for="(c, i) in node.children" :key="i" :node="c" :depth="depth + 1" />
    </ul>
  </li>
</template>

<style scoped>
.svb-tree {
  list-style: none;
  margin: 0.15rem 1.1rem 0.15rem 0;
  padding: 0 0.8rem 0 0;
  border-right: 1px dashed #adb5bd;
}
</style>
