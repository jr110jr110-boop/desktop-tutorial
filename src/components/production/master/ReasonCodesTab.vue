<script setup lang="ts">
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getReasonCodes, saveReasonCode } from "@/api/ProductionAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { reasonKinds } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";

// Reason codes: scrap (terminal scrap report), downtime (pause, machine-down) and waiting.
const filters = ref({ Kind: null as number | null, IsActive: null as boolean | null, Contains: "" });
const list = usePagedList(getReasonCodes, filters);
const form = ref<any>(null);
const busy = ref(false);
const canEdit = hasRole(ProductionRoles.reasonEditors);

async function save() {
  const f = form.value;
  if (!String(f.code).trim() || !String(f.title).trim()) return fail("کد و عنوان دلیل را بنویسید.");
  busy.value = true;
  const [, error] = await saveReasonCode({ id: f.id, code: String(f.code).trim(), title: String(f.title).trim(), kind: f.kind, isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("دلیل ذخیره شد.");
  form.value = null;
  list.load();
}

onMounted(list.load);
defineExpose({ reload: list.load });
</script>

<template>
  <div>
    <div class="inv-toolbar">
      <div class="inv-filters">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="کد یا عنوان" @keyup.enter="list.search" />
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in reasonKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.IsActive" class="form-select form-select-sm"><option :value="null">فعال و غیرفعال</option><option :value="true">فعال</option><option :value="false">غیرفعال</option></select>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <button v-if="canEdit" class="btn btn-sm btn-primary" @click="form = { id: null, code: '', title: '', kind: 1, isActive: true }">دلیل جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>عنوان</th><th>نوع</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="5" class="text-center inv-muted">دلیلی ثبت نشده است.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.id">
            <td class="inv-ltr">{{ r.code }}</td><td>{{ r.title }}</td><td>{{ reasonKinds[r.kind] }}</td>
            <td><span :class="r.isActive ? 'ok' : ''" class="inv-badge">{{ r.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="form = { id: r.id, code: r.code, title: r.title, kind: r.kind, isActive: r.isActive }">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="form" class="inv-modal-backdrop" @click.self="form = null">
      <div class="inv-modal" style="max-width: 480px">
        <h2 class="inv-title mb-2">{{ form.id ? "ویرایش دلیل" : "دلیل جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">کد</label><input v-model="form.code" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-8"><label class="form-label small mb-0">عنوان</label><input v-model="form.title" class="form-control form-control-sm" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">نوع</label><select v-model="form.kind" class="form-select form-select-sm"><option v-for="(t, k) in reasonKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6 d-flex align-items-end"><label class="form-check"><input v-model="form.isActive" class="form-check-input" type="checkbox" /> <span class="form-check-label">فعال</span></label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="save">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>
