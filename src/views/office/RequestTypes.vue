<script setup lang="ts">
import { onMounted, ref } from "vue";
import RequestTypeForm from "@/components/office/wf/RequestTypeForm.vue";
import { getRequestTypes } from "@/api/OfficeAPIs";
import { fail } from "@/composables/inventory";
import { requestFieldTypes } from "@/composables/office";

// Request types (orgAdmins): form fields and approval chain. Inactive types stay visible here (the API returns them to HR, PMO, CEO).
const types = ref<any[]>([]);
const editing = ref<null | { type: any }>(null);

async function load() {
  const [d, error] = await getRequestTypes();
  if (error) return fail(error);
  types.value = Array.isArray(d) ? d : [];
}
function saved() {
  editing.value = null;
  load();
}
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">انواع درخواست عمومی</h1>
        <button class="btn btn-sm btn-primary" @click="editing = { type: null }">نوع جدید</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>عنوان</th><th>فیلدها</th><th>زنجیره تایید</th><th>یادداشت مقصد</th><th>وضعیت</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!types.length"><td colspan="6" class="text-center inv-muted">نوعی تعریف نشده است.</td></tr>
            <tr v-for="t in types" :key="t.id">
              <td>{{ t.title }}</td>
              <td class="small"><span v-for="f in t.fields" :key="f.name" class="inv-badge info me-1">{{ f.label }} ({{ requestFieldTypes[f.type] }}){{ f.required ? " *" : "" }}</span></td>
              <td class="small">{{ t.approvalChainTitles?.length ? t.approvalChainTitles.join(" ← ") : "مدیر واحد درخواست‌کننده" }}</td>
              <td class="small">{{ t.targetNote }}</td>
              <td><span :class="t.isActive ? 'ok' : 'bad'" class="inv-badge">{{ t.isActive ? "فعال" : "غیرفعال" }}</span></td>
              <td><button class="btn btn-sm btn-outline-primary" @click="editing = { type: t }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
    <RequestTypeForm v-if="editing" :type="editing.type" @close="editing = null" @saved="saved" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
