<script setup lang="ts">
// The project engineering readiness checklist («کنترل آمادگی اطلاعات مهندسی شروع پروژه») of one product / work order, with the PMO+CEO exception request.
import { onMounted, ref, watch } from "vue";
import { getReadiness, requestReadinessException } from "@/api/OpsDataAPIs";
import { fail, ok } from "@/composables/inventory";

const props = defineProps<{ productId?: number | null; workOrderId?: number | null }>();
const data = ref<any>(null);
const justification = ref("");
const busy = ref(false);
const statusText: Record<string, string> = { OK: "کامل", Missing: "ناقص", Partial: "نیمه‌کامل", NotApplicable: "غیرقابل اعمال" };

async function load() {
  data.value = null;
  if (!props.productId && !props.workOrderId) return;
  const [d, e] = await getReadiness({ productId: props.productId, workOrderId: props.workOrderId });
  if (e) return fail(e);
  data.value = d;
}
async function requestException() {
  if (!data.value || !justification.value.trim()) return;
  busy.value = true;
  const [, e] = await requestReadinessException(data.value.workOrderId || props.workOrderId || 0, data.value.productId, justification.value.trim());
  busy.value = false;
  if (e) return fail(e);
  ok("درخواست استثنا ثبت شد؛ تایید مسئول دفتر پروژه و مدیرعامل لازم است.");
  justification.value = "";
  load();
}
onMounted(load);
watch(() => [props.productId, props.workOrderId], load);
</script>

<template>
  <div v-if="data" class="inv-card mb-3">
    <div class="d-flex justify-content-between align-items-center">
      <h2 class="h6 mb-0">کنترل آمادگی اطلاعات مهندسی شروع پروژه</h2>
      <span :class="data.ready ? 'badge bg-success' : 'badge bg-danger'">{{ data.percent }}٪ {{ data.ready ? "آماده" : "ناقص" }}</span>
    </div>
    <p v-if="data.message" class="small inv-muted mt-2">{{ data.message }}</p>
    <table class="table table-sm mt-2">
      <thead><tr><th>مورد</th><th>وضعیت</th><th>نوع</th><th>مسئول</th><th>تعداد ناقص</th><th>موارد</th></tr></thead>
      <tbody>
        <tr v-for="i in data.items" :key="i.code">
          <td>{{ i.title }}</td>
          <td>{{ statusText[i.status] || i.status }}</td>
          <td>{{ i.hard ? "الزامی" : "توصیه‌ای" }}</td>
          <td>{{ i.ownerTitle }}</td>
          <td>{{ i.missingCount }}</td>
          <td>
            <span v-for="(o, k) in (i.offenders || []).slice(0, 5)" :key="k" class="me-2">
              <router-link v-if="o.link" :to="o.link">{{ o.text }}</router-link><span v-else>{{ o.text }}</span>
            </span>
          </td>
        </tr>
      </tbody>
    </table>
    <div v-if="!data.ready && !data.hasApprovedException" class="mt-2">
      <textarea v-model="justification" class="form-control form-control-sm" rows="2" placeholder="توجیه درخواست استثنا (تایید مسئول دفتر پروژه و مدیرعامل لازم است)"></textarea>
      <button class="btn btn-sm btn-outline-danger mt-1" :disabled="busy || !justification.trim() || !!data.pendingExceptionId" @click="requestException">درخواست استثنا</button>
      <span v-if="data.pendingExceptionId" class="small inv-muted ms-2">درخواست در انتظار تایید است.</span>
    </div>
    <p v-if="data.hasApprovedException" class="small text-success mt-2 mb-0">استثنا تایید شده است؛ موارد ناقص به‌صورت تعهد پیگیری می‌شوند.</p>
  </div>
</template>
