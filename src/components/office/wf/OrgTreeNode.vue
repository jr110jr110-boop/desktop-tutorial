<script setup lang="ts">
// One unit of the org chart: its positions and (recursively) its child units.
defineProps({ node: { type: Object, required: true }, depth: { type: Number, default: 0 } });
</script>

<template>
  <div class="org-node" :style="{ marginInlineStart: depth ? '1.25rem' : '0' }">
    <div class="org-unit">
      <b>{{ node.title }}</b> <span v-if="node.code" class="inv-muted small inv-ltr">({{ node.code }})</span>
    </div>
    <ul class="org-positions">
      <li v-if="!(node.positions || []).length" class="inv-muted small">بدون سمت</li>
      <li v-for="p in node.positions" :key="p.id" :class="{ 'inv-muted': !p.isActive }">
        {{ p.title }}
        <span v-if="p.isManager" class="inv-badge info">مدیر واحد</span>
        <span v-if="p.canSign" class="inv-badge ok">حق امضا</span>
        <span v-if="!p.isActive" class="inv-badge bad">غیرفعال</span>
        <span class="small">{{ p.userName ? "دارنده: " + p.userName : "بدون دارنده" }}</span>
      </li>
    </ul>
    <OrgTreeNode v-for="c in node.children" :key="c.id" :node="c" :depth="depth + 1" />
  </div>
</template>

<style scoped>
.org-node { border-inline-start: 2px solid #dee2e6; padding-inline-start: 0.75rem; margin-bottom: 0.5rem; }
.org-unit { font-size: 0.95rem; }
.org-positions { list-style: none; margin: 0.2rem 0 0.4rem; padding: 0; font-size: 0.85rem; }
.org-positions li { padding: 0.1rem 0; }
</style>
