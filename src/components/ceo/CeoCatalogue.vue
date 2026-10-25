<script setup lang="ts">
// Every detail table the CEO may open (GET ceo/drills), grouped by module, with a search box. A table that needs a parameter the page cannot know (required) is listed
// but not opened from here; it opens from its KPI or chart, or through the assistant.
import { computed, nextTick, ref, watch } from "vue";
import type { CeoDrillDescriptor } from "@/api/CeoAPIs";
import CeoIcon from "@/components/ceo/CeoIcon.vue";

const props = defineProps({
  open: { type: Boolean, default: false },
  drills: { type: Array as () => CeoDrillDescriptor[], default: () => [] },
  loading: { type: Boolean, default: false },
  error: { type: String, default: "" },
  offset: { type: Number, default: 0 },
});
const emit = defineEmits<{ (e: "close"): void; (e: "pick", key: string): void; (e: "retry"): void }>();

const moduleTitles: Record<string, string> = {
  finance: "مالی و خزانه", sales: "فروش", projects: "پروژه‌ها", production: "تولید و نگهداری", quality: "کیفیت", inventory: "انبار",
  procurement: "تأمین و خرید", service: "خدمات پس از فروش", office: "اداری", ai: "هوش مصنوعی",
};
const q = ref("");
const search = ref<HTMLInputElement | null>(null);
watch(
  () => props.open,
  async (open) => {
    if (!open) return;
    q.value = "";
    await nextTick();
    search.value?.focus();
  }
);
const groups = computed(() => {
  const term = q.value.trim();
  const list = props.drills.filter((d) => !term || d.title.includes(term) || (d.description || "").includes(term));
  const by: Record<string, CeoDrillDescriptor[]> = {};
  for (const d of list) (by[d.module] ||= []).push(d);
  const order = Object.keys(moduleTitles);
  return Object.keys(by)
    .sort((a, b) => (order.indexOf(a) + 100) % 100 - (order.indexOf(b) + 100) % 100)
    .map((m) => ({ module: m, title: moduleTitles[m] || m, items: by[m] }));
});
const needsParam = (d: CeoDrillDescriptor) => (d.parameters || []).some((p) => p.required);
function onKey(e: KeyboardEvent) {
  if (e.key === "Escape") {
    e.preventDefault();
    emit("close");
  }
}
</script>

<template>
  <div class="cat-root">
    <transition name="ceo-scrim">
      <div v-if="open" class="cat-scrim" @click="emit('close')" />
    </transition>
    <transition name="ceo-slide">
      <aside v-if="open" :style="{ left: offset + 'px' }" aria-labelledby="ceo-cat-title" aria-modal="true" class="cat-panel" dir="rtl" role="dialog" @keydown="onKey">
        <header class="cat-head">
          <h2 id="ceo-cat-title" class="cat-title">جدول‌های جزئیات</h2>
          <button aria-label="بستن" class="ceo-icon-btn" title="بستن (Esc)" type="button" @click="emit('close')"><CeoIcon name="close" /></button>
        </header>
        <label class="cat-search">
          <span class="visually-hidden">جست‌وجوی جدول</span>
          <input ref="search" v-model="q" placeholder="جست‌وجو در عنوان جدول‌ها، مثلاً «مطالبات»" type="search" />
        </label>
        <div class="cat-body">
          <div v-if="loading" class="cat-state"><span aria-hidden="true" class="spinner-border spinner-border-sm" /> در حال خواندن فهرست جدول‌ها…</div>
          <div v-else-if="error" class="cat-state error">
            <p>{{ error }}</p>
            <button class="ceo-btn ghost" type="button" @click="emit('retry')">تلاش دوباره</button>
          </div>
          <p v-else-if="!groups.length" class="cat-state">جدولی با این عنوان نیست؛ واژه‌ی کوتاه‌تری بنویسید.</p>
          <section v-for="g in groups" :key="g.module" class="cat-group">
            <h3 class="cat-group-title">{{ g.title }}</h3>
            <ul>
              <li v-for="d in g.items" :key="d.key">
                <button :disabled="needsParam(d)" class="cat-item" type="button" @click="emit('pick', d.key)">
                  <span class="cat-item-title">{{ d.title }}</span>
                  <span v-if="needsParam(d)" class="cat-item-desc">از شاخص یا نمودار مربوط باز کنید؛ این جدول پارامتر لازم دارد.</span>
                  <span v-else-if="d.description && d.description !== d.title" class="cat-item-desc">{{ d.description }}</span>
                </button>
              </li>
            </ul>
          </section>
        </div>
      </aside>
    </transition>
  </div>
</template>

<style scoped>
.cat-scrim {
  position: fixed;
  inset: 0;
  z-index: 1044;
  background: var(--ceo-scrim);
}
.cat-panel {
  position: fixed;
  top: 0;
  bottom: 0;
  z-index: 1045;
  width: min(520px, 100vw);
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 20px 24px;
  background: var(--ceo-card);
  color: var(--ceo-ink);
  border-right: 1px solid var(--ceo-line);
  box-shadow: 18px 0 48px rgba(10, 18, 26, 0.18);
  text-align: right;
}
.cat-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.cat-title {
  margin: 0;
  font-size: 20px;
  line-height: 30px;
  font-weight: 600;
  color: var(--ceo-ink);
}
.cat-search input {
  width: 100%;
  height: 40px;
  padding: 0 12px;
  border: 1px solid var(--ceo-line);
  border-radius: 6px;
  background: var(--ceo-surface);
  color: var(--ceo-ink);
  font: inherit;
  font-size: 14px;
}
.cat-search input:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: 1px;
}
.cat-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}
.cat-group + .cat-group {
  margin-top: 16px;
}
.cat-group-title {
  margin: 0 0 4px;
  font-size: 13px;
  line-height: 20px;
  font-weight: 500;
  color: var(--ceo-muted);
}
.cat-group ul {
  list-style: none;
  margin: 0;
  padding: 0;
}
.cat-item {
  display: flex;
  flex-direction: column;
  gap: 1px;
  width: 100%;
  padding: 8px 10px;
  border: 0;
  border-radius: 6px;
  background: none;
  color: var(--ceo-ink);
  font: inherit;
  text-align: right;
  cursor: pointer;
}
.cat-item:hover:not(:disabled) {
  background: var(--ceo-card-hover);
}
.cat-item:disabled {
  cursor: default;
  opacity: 0.7;
}
.cat-item:focus-visible {
  outline: 2px solid var(--ceo-focus);
  outline-offset: -2px;
}
.cat-item-title {
  font-size: 14px;
  line-height: 22px;
}
.cat-item-desc {
  font-size: 12px;
  line-height: 18px;
  color: var(--ceo-muted);
}
.cat-state {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
  font-size: 13px;
  color: var(--ceo-muted);
}
.cat-state.error p {
  margin: 0;
  color: var(--ceo-danger-ink);
}
</style>
