<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { deleteGroup, getGroups, saveGroup } from "@/api/InventoryAPIs";
import { fail, ok } from "@/composables/inventory";

const groups = ref<any[]>([]);
const loading = ref(false);
const form = ref<any>(null);
const saving = ref(false);

async function load() {
  loading.value = true;
  const [data, error] = await getGroups();
  loading.value = false;
  if (error) return fail(error);
  groups.value = data || [];
}

/** Groups in tree order, each with its depth for indenting. */
const tree = computed(() => {
  const byParent: Record<string, any[]> = {};
  groups.value.forEach((g) => {
    const key = String(g.parentId ?? "root");
    (byParent[key] = byParent[key] || []).push(g);
  });
  Object.values(byParent).forEach((list) => list.sort((a, b) => String(a.code).localeCompare(String(b.code))));
  const ids = new Set(groups.value.map((g) => g.id));
  const result: any[] = [];
  const walk = (key: string, depth: number) => {
    (byParent[key] || []).forEach((g) => {
      result.push({ ...g, depth });
      walk(String(g.id), depth + 1);
    });
  };
  walk("root", 0);
  // groups whose parent is missing are shown at the top level
  groups.value.filter((g) => g.parentId && !ids.has(g.parentId)).forEach((g) => {
    result.push({ ...g, depth: 0 });
    walk(String(g.id), 1);
  });
  return result;
});

function add(parent: any = null) {
  form.value = { id: null, code: parent ? parent.code + "-" : "", title: "", description: "", parentId: parent ? parent.id : null };
}

function edit(g: any) {
  form.value = { id: g.id, code: g.code, title: g.title, description: g.description, parentId: g.parentId };
}

async function save() {
  saving.value = true;
  const [, error] = await saveGroup(form.value);
  saving.value = false;
  if (error) return fail(error);
  ok("گروه ذخیره شد.");
  form.value = null;
  load();
}

async function remove(g: any) {
  if (!confirm(`گروه ${g.title} حذف شود؟`)) return;
  const [, error] = await deleteGroup(g.id);
  if (error) return fail(error);
  ok("حذف شد.");
  load();
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">گروه‌بندی کالاها</h1>
        <button class="btn btn-sm btn-primary" @click="add()">گروه اصلی جدید</button>
      </div>
      <p class="inv-muted">مسیر ساخت (مراحل تولید) را می‌توان برای هر گروه تعریف کرد؛ زیرگروه‌ها از گروه بالاتر ارث می‌برند.</p>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead>
            <tr><th>کد</th><th>عنوان</th><th>تعداد کالا</th><th>توضیح</th><th></th></tr>
          </thead>
          <tbody>
            <tr v-if="loading"><td class="text-center" colspan="5">در حال بارگذاری...</td></tr>
            <tr v-else-if="!tree.length"><td class="text-center inv-muted" colspan="5">گروهی تعریف نشده است.</td></tr>
            <tr v-for="g in tree" :key="g.id">
              <td class="inv-ltr" :style="{ paddingRight: 0.5 + g.depth * 1.5 + 'rem' }">{{ g.code }}</td>
              <td :style="{ paddingRight: 0.5 + g.depth * 1.5 + 'rem' }">
                <span v-if="g.depth" class="inv-muted">└ </span>{{ g.title }}
              </td>
              <td>{{ g.itemsCount }}</td>
              <td class="inv-muted">{{ g.description }}</td>
              <td class="text-nowrap">
                <button class="btn btn-sm btn-link p-0 ms-2" @click="add(g)">زیرگروه</button>
                <button class="btn btn-sm btn-link p-0 ms-2" @click="edit(g)">ویرایش</button>
                <router-link :to="{ name: 'InventoryRouting', query: { groupId: g.id } }" class="btn btn-sm btn-link p-0 ms-2">مسیر ساخت</router-link>
                <button class="btn btn-sm btn-link text-danger p-0" @click="remove(g)">حذف</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-3">{{ form.id ? "ویرایش گروه" : "گروه جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small">کد *</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-8"><label class="form-label small">عنوان *</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-12">
            <label class="form-label small">گروه بالاتر</label>
            <select v-model="form.parentId" class="form-select form-select-sm">
              <option :value="null">— گروه اصلی —</option>
              <option v-for="g in tree.filter((x) => x.id !== form.id)" :key="g.id" :value="g.id">
                {{ "  ".repeat(g.depth) }}{{ g.code }} - {{ g.title }}
              </option>
            </select>
          </div>
          <div class="col-12"><label class="form-label small">توضیح</label><textarea v-model="form.description" class="form-control form-control-sm" rows="2"></textarea></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button>
          <button :disabled="saving" class="btn btn-sm btn-primary" @click="save">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./inventory.scss"></style>
