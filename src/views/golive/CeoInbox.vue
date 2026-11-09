<script setup lang="ts">
// «صندوق تصمیم مدیرعامل» (Package G): everything that waits for the CEO in one place, grouped, to look at together. There is NO bulk approval: each item is opened on its own page and decided there, one by one, with its own reason.
import { onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import { getCeoInbox } from "@/api/GoLiveAPIs";
import { fail } from "@/composables/inventory";

const inbox = ref<any>(null);
const open = ref<Record<string, boolean>>({});

async function load() {
  const [d, error] = await getCeoInbox();
  if (error) return fail(error);
  inbox.value = d;
  for (const g of d.groups) open.value[g.key] = g.count > 0;
}
onMounted(load);
</script>

<template>
  <div class="inv-page" dir="rtl">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">صندوق تصمیم مدیرعامل</h1><button class="btn btn-sm btn-outline-secondary" @click="load">تازه‌سازی</button></div>
      <p class="inv-muted small">همه موارد منتظر مدیرعامل گروه‌بندی شده‌اند. تایید گروهی وجود ندارد؛ هر مورد در صفحه خودش و با دلیل خودش تصمیم‌گیری می‌شود.</p>
      <div v-if="inbox"><span class="inv-badge" :class="inbox.total ? 'warn' : 'ok'">{{ inbox.total }} مورد منتظر</span></div>
      <div v-for="g in inbox?.groups || []" :key="g.key" class="border rounded p-2 mt-2">
        <div class="d-flex justify-content-between align-items-center" style="cursor: pointer" @click="open[g.key] = !open[g.key]">
          <b>{{ g.title }}</b>
          <span><span class="inv-badge" :class="g.count ? 'warn' : 'ok'">{{ g.count }}</span><span v-if="g.oldestDays" class="inv-muted small ms-2">قدیمی‌ترین: {{ g.oldestDays }} روز</span></span>
        </div>
        <p v-if="open[g.key] && g.count > g.items.length" class="small inv-muted mt-2 mb-0">{{ g.count }} مورد منتظر است؛ قدیمی‌ترین {{ g.items.length }} مورد نمایش داده می‌شود.</p>
        <table v-if="open[g.key] && g.items.length" class="table table-sm mt-2 mb-0">
          <thead><tr><th>مورد</th><th>شرح</th><th>سن (روز)</th><th></th></tr></thead>
          <tbody><tr v-for="i in g.items" :key="i.ref"><td>{{ i.title }}</td><td class="small inv-muted">{{ i.detail }}</td><td>{{ i.ageDays }}</td><td><RouterLink :to="i.route" class="btn btn-sm btn-outline-primary">باز کردن و تصمیم</RouterLink></td></tr></tbody>
        </table>
      </div>
    </div>
  </div>
</template>
