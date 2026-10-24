<script setup lang="ts">
// External parties (customers, suppliers, organisations) a letter is exchanged with. Fields not shown here (supplier / customer links) are kept as they are.
import { onMounted, ref } from "vue";
import QPager from "@/components/quality/QPager.vue";
import { getParties, saveParty } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { partyKinds } from "@/composables/office";
import { fail, ok } from "@/composables/inventory";

const filters = ref({ Contains: "" });
const list = usePagedList(getParties, filters);
const edit = ref<any>(null);
const busy = ref(false);

function start(p?: any) {
  edit.value = p ? { ...p } : { id: null, name: "", kind: 3, providerId: null, customerId: null, address: "", phone: "", email: "", isActive: true };
}
async function submit() {
  const e = edit.value;
  if (!String(e.name || "").trim()) return fail("نام را وارد کنید.");
  busy.value = true;
  const [, error] = await saveParty({ id: e.id || null, name: e.name.trim(), kind: e.kind, providerId: e.providerId || null, customerId: e.customerId || null, address: e.address || null, phone: e.phone || null, email: e.email || null, isActive: e.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("طرف مکاتبه ذخیره شد.");
  edit.value = null;
  list.load();
}
onMounted(list.load);
</script>

<template>
  <div>
    <div class="d-flex gap-2 mb-2 flex-wrap">
      <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="نام، تلفن یا ایمیل" style="max-width: 18rem" @keydown.enter="list.search" />
      <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      <button class="btn btn-sm btn-primary me-auto" @click="start()">طرف مکاتبه جدید</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm">
        <thead><tr><th>نام</th><th>نوع</th><th>تلفن</th><th>ایمیل</th><th>نشانی</th><th>وضعیت</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="7" class="text-center inv-muted">موردی نیست.</td></tr>
          <tr v-for="p in list.rows.value" :key="p.id">
            <td>{{ p.name }}<span v-if="p.providerId || p.customerId" class="inv-badge info me-1">پیوند به {{ p.providerId ? "تامین‌کننده" : "مشتری" }}</span></td>
            <td>{{ partyKinds[p.kind] }}</td>
            <td class="inv-ltr">{{ p.phone }}</td>
            <td class="inv-ltr">{{ p.email }}</td>
            <td>{{ p.address }}</td>
            <td><span :class="p.isActive ? 'ok' : 'bad'" class="inv-badge">{{ p.isActive ? "فعال" : "غیرفعال" }}</span></td>
            <td><button class="btn btn-sm btn-link" @click="start(p)">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />

    <div v-if="edit" class="inv-modal-backdrop" @click.self="edit = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">{{ edit.id ? "ویرایش طرف مکاتبه" : "طرف مکاتبه جدید" }}</h2>
        <div class="row g-2">
          <div class="col-md-8"><label class="form-label small mb-0">نام *</label><input v-model="edit.name" class="form-control form-control-sm" /></div>
          <div class="col-md-4"><label class="form-label small mb-0">نوع</label><select v-model.number="edit.kind" class="form-select form-select-sm"><option v-for="(t, k) in partyKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-6"><label class="form-label small mb-0">تلفن</label><input v-model="edit.phone" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">ایمیل</label><input v-model="edit.email" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-12"><label class="form-label small mb-0">نشانی</label><textarea v-model="edit.address" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-12"><label class="small d-flex align-items-center gap-1"><input v-model="edit.isActive" type="checkbox" /> فعال</label></div>
        </div>
        <div class="d-flex gap-2 justify-content-end mt-3">
          <button class="btn btn-sm btn-outline-secondary" @click="edit = null">انصراف</button>
          <button :disabled="busy" class="btn btn-sm btn-primary" @click="submit">ذخیره</button>
        </div>
      </div>
    </div>
  </div>
</template>
