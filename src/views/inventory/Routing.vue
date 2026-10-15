<script setup lang="ts">
// Manufacturing route (operation templates) of an item or of an item group.
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import ItemPicker from "@/components/inventory/ItemPicker.vue";
import { deleteTemplate, getGroups, getMachines, getTemplates, rowsOf, saveTemplate } from "@/api/InventoryAPIs";
import { fail, ok } from "@/composables/inventory";

const route = useRoute();
const mode = ref<"group" | "item">("group");
const groups = ref<any[]>([]);
const machines = ref<any[]>([]);
const groupId = ref<number | null>(null);
const item = ref<any>(null);
const templates = ref<any[]>([]);
const form = ref<any>(null);

async function load() {
  templates.value = [];
  const params = mode.value === "group" ? { ItemGroupId: groupId.value } : { ItemId: item.value?.id };
  if (!params.ItemGroupId && !(params as any).ItemId) return;
  const [data, error] = await getTemplates(params);
  if (error) return fail(error);
  templates.value = data || [];
}

function add() {
  const last = templates.value.length ? Math.max(...templates.value.map((t) => t.sequence)) : 0;
  form.value = {
    id: null, itemGroupId: mode.value === "group" ? groupId.value : null, itemId: mode.value === "item" ? item.value?.id : null,
    sequence: last + 10, title: "", description: "", requiresQc: false, machineId: null, isActive: true,
  };
}

function edit(t: any) {
  form.value = { ...t };
}

async function save() {
  const [, error] = await saveTemplate(form.value);
  if (error) return fail(error);
  ok("مرحله ذخیره شد.");
  form.value = null;
  load();
}

async function remove(t: any) {
  if (!confirm(`مرحله «${t.title}» حذف شود؟ مراحل ثبت‌شده روی قطعات دست نمی‌خورند.`)) return;
  const [, error] = await deleteTemplate(t.id);
  if (error) return fail(error);
  load();
}

onMounted(async () => {
  const [g] = await getGroups();
  groups.value = g || [];
  getMachines().then(([m]) => (machines.value = rowsOf(m)));
  if (route.query.groupId) {
    groupId.value = Number(route.query.groupId);
    load();
  }
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">مسیر ساخت (مراحل تولید و QC)</h1>
        <div class="btn-group btn-group-sm">
          <button :class="mode === 'group' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="mode = 'group'; load()">برای گروه کالا</button>
          <button :class="mode === 'item' ? 'btn-primary' : 'btn-outline-primary'" class="btn" @click="mode = 'item'; load()">برای یک کالا</button>
        </div>
      </div>
      <p class="inv-muted">
        اگر برای کالا مسیر جداگانه تعریف شود، همان به کار می‌رود؛ وگرنه مسیر نزدیک‌ترین گروه بالاتر. با ساخت سریال یا «اعمال مسیر ساخت»
        این مراحل روی قطعه ثبت می‌شوند.
      </p>
      <div class="inv-filters mb-3">
        <select v-if="mode === 'group'" v-model="groupId" class="form-select form-select-sm" @change="load">
          <option :value="null">انتخاب گروه</option>
          <option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option>
        </select>
        <div v-else style="min-width: 320px"><ItemPicker v-model="item" @select="load" /></div>
        <button :disabled="mode === 'group' ? !groupId : !item" class="btn btn-sm btn-primary" @click="add">+ مرحله</button>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>ترتیب</th><th>مرحله</th><th>دستگاه</th><th>QC</th><th>توضیح</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!templates.length"><td class="inv-muted text-center" colspan="6">مرحله‌ای تعریف نشده است.</td></tr>
          <tr v-for="t in templates" :key="t.id" :class="{ 'opacity-50': !t.isActive }">
            <td>{{ t.sequence }}</td>
            <td>{{ t.title }}</td>
            <td>{{ t.machineTitle }}</td>
            <td><span v-if="t.requiresQc" class="inv-badge info">نیاز به QC</span></td>
            <td class="inv-muted">{{ t.description }}</td>
            <td class="text-nowrap">
              <button class="btn btn-sm btn-link p-0 ms-2" @click="edit(t)">ویرایش</button>
              <button class="btn btn-sm btn-link text-danger p-0" @click="remove(t)">حذف</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ form.id ? "ویرایش مرحله" : "مرحله جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-3"><label class="form-label small">ترتیب</label><input v-model.number="form.sequence" class="form-control form-control-sm" type="number" /></div>
          <div class="col-md-9"><label class="form-label small">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" placeholder="تراشکاری، عملیات حرارتی، تست UT، بالانس..." /></div>
          <div class="col-md-6">
            <label class="form-label small">دستگاه</label>
            <select v-model="form.machineId" class="form-select form-select-sm">
              <option :value="null">-</option>
              <option v-for="m in machines" :key="m.id" :value="m.id">{{ m.title }}</option>
            </select>
          </div>
          <div class="col-md-6 pt-4">
            <div class="form-check"><input id="rq" v-model="form.requiresQc" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="rq">بعد از این مرحله QC لازم است</label></div>
            <div class="form-check"><input id="ta" v-model="form.isActive" class="form-check-input" type="checkbox" /><label class="form-check-label small" for="ta">فعال</label></div>
          </div>
          <div class="col-12"><label class="form-label small">توضیح / معیار پذیرش</label><textarea v-model="form.description" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
