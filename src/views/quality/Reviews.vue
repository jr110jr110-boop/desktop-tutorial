<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import { createReview, getReviews } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { reviewStatuses, usePaged } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

const router = useRouter();
const filters = ref({ Status: null as number | null });
const { rows, total, page, search, go } = usePaged(getReviews, filters);
const form = ref<any>(null);
const busy = ref(false);
const canWrite = hasRole(QualityRoles.review);

async function create() {
  busy.value = true;
  const [data, error] = await createReview({ reviewDate: form.value.reviewDate || null, attendees: form.value.attendees });
  busy.value = false;
  if (error) return fail(error);
  ok("جلسه ایجاد شد؛ ورودی‌های خودکار پیش‌نویس شدند.");
  router.push({ name: "QualityReviewEdit", params: { id: data.id } });
}

onMounted(search);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">بازنگری مدیریت</h1>
        <button v-if="canWrite" class="btn btn-sm btn-primary" @click="form = { reviewDate: '', attendees: '' }">جلسه جدید</button>
      </div>
      <div v-if="form" class="border rounded p-2 mb-3">
        <div class="row g-2 align-items-end">
          <div class="col-md-3"><label class="form-label small mb-0">تاریخ جلسه</label><input v-model="form.reviewDate" class="form-control form-control-sm" type="date" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">حاضرین (عناوین شغلی)</label><input v-model="form.attendees" class="form-control form-control-sm" /></div>
          <div class="col-md-3 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="create">ایجاد</button><button class="btn btn-sm btn-outline-secondary" @click="form = null">انصراف</button></div>
        </div>
      </div>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" @change="search()"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in reviewStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
      </div>
      <table class="table table-sm table-hover">
        <thead><tr><th>تاریخ جلسه</th><th>تصمیمات</th><th>وضعیت</th></tr></thead>
        <tbody>
          <tr v-if="!rows.length"><td colspan="3" class="text-center inv-muted">جلسه‌ای نیست.</td></tr>
          <tr v-for="r in rows" :key="r.id" role="button" @click="router.push({ name: 'QualityReviewEdit', params: { id: r.id } })">
            <td>{{ jalali(r.reviewDate) }}</td><td>{{ r.decisionCount }}</td>
            <td><span :class="{ ok: r.status === 2, info: r.status === 1, warn: r.status === 0 }" class="inv-badge">{{ reviewStatuses[r.status] }}</span></td>
          </tr>
        </tbody>
      </table>
      <QPager :page="page" :total="total" @go="go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
