<script setup lang="ts">
import { nextTick } from "vue";
import { printForm } from "@/composables/quality";
import ReviewPrint from "@/components/quality/print/ReviewPrint.vue";
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import DocAttach from "@/components/quality/DocAttach.vue";
import UserSelect from "@/components/quality/UserSelect.vue";
import { getReview, reviewAction, saveReviewDecision, saveReviewInput } from "@/api/QualityAPIs";
import { fail, jalali, ok } from "@/composables/inventory";
import { dateInput, reviewInputCategories, reviewStatuses } from "@/composables/quality";
import { hasRole, QualityRoles } from "@/composables/roles";

// Management review (ISO 9001 9.3): inputs per category, decisions (optionally drafting a CAPA), hold and close with minutes.
const route = useRoute();
const review = ref<any>(null);
const input = ref<any>(null);
const decision = ref<any>(null);
const minutes = ref<string | null>(null);
const busy = ref(false);
async function doPrint() {
  await nextTick();
  printForm();
}
const canWrite = hasRole(QualityRoles.review);

async function load() {
  const [data, error] = await getReview(Number(route.params.id));
  if (error) return fail(error);
  review.value = data;
  minutes.value = data.minutesDocumentId || null;
}

async function saveInput() {
  busy.value = true;
  const [, error] = await saveReviewInput({ reviewId: review.value.id, category: input.value.category, text: input.value.text, attendees: input.value.attendees || null });
  busy.value = false;
  if (error) return fail(error);
  input.value = null;
  ok("ورودی ثبت شد.");
  load();
}

async function saveDecision() {
  busy.value = true;
  const d = decision.value;
  const [, error] = await saveReviewDecision({ reviewId: review.value.id, id: d.id, text: d.text, responsibleUserId: d.responsibleUserId, dueDate: d.dueDate || null, createCapa: !!d.createCapa });
  busy.value = false;
  if (error) return fail(error);
  decision.value = null;
  ok("تصمیم ثبت شد.");
  load();
}

async function act(action: string) {
  busy.value = true;
  const [data, error] = await reviewAction(review.value.id, action, minutes.value);
  busy.value = false;
  if (error) return fail(error);
  review.value = data;
  ok("انجام شد.");
}

onMounted(load);
</script>

<template>
  <div v-if="review" class="inv-page">
    <ReviewPrint :review="review" />
    <div class="inv-card">
      <div class="inv-toolbar">
        <div>
          <h1 class="inv-title">بازنگری مدیریت — {{ jalali(review.reviewDate) }}</h1>
          <span :class="{ ok: review.status === 2, info: review.status === 1, warn: review.status === 0 }" class="inv-badge">{{ reviewStatuses[review.status] }}</span>
          <span class="inv-muted ms-2">حاضرین: {{ review.attendees || "—" }}</span>
        </div>
        <div class="d-flex flex-wrap gap-2 align-items-end">
          <router-link :to="{ name: 'QualityReviews' }" class="btn btn-sm btn-outline-secondary">بازگشت</router-link>
          <button class="btn btn-sm btn-outline-secondary" @click="doPrint">چاپ صورتجلسه (FO-QA-022)</button>
          <template v-if="canWrite">
            <button v-if="review.status === 0" :disabled="busy" class="btn btn-sm btn-primary" @click="act('hold')">جلسه برگزار شد</button>
            <button v-if="review.status === 1" :disabled="busy" class="btn btn-sm btn-outline-secondary" @click="act('redraft')">بازگشت به پیش‌نویس</button>
            <button v-if="review.status === 1" :disabled="busy" class="btn btn-sm btn-success" @click="act('close')">بستن (با صورتجلسه)</button>
          </template>
        </div>
      </div>
      <div class="col-md-6"><label class="form-label small mb-0">صورتجلسه (فایل)</label><DocAttach v-model="minutes" title="صورتجلسه بازنگری مدیریت" :disabled="!canWrite || review.status === 2" /></div>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">ورودی‌ها (۹.۳.۲)</h2>
        <button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-primary" @click="input = { category: 1, text: '', attendees: review.attendees }">ورودی جدید</button>
      </div>
      <div v-if="input" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-md-4"><label class="form-label small mb-0">دسته</label><select v-model="input.category" class="form-select form-select-sm"><option v-for="(t, k) in reviewInputCategories" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div class="col-md-8"><label class="form-label small mb-0">حاضرین</label><input v-model="input.attendees" class="form-control form-control-sm" /></div>
          <div class="col-12"><label class="form-label small mb-0">متن *</label><textarea v-model="input.text" class="form-control form-control-sm" rows="3"></textarea></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveInput">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="input = null">انصراف</button></div>
        </div>
      </div>
      <table class="table table-sm">
        <thead><tr><th style="width: 25%">دسته</th><th>متن</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!review.inputs.length"><td colspan="3" class="text-center inv-muted">ورودی‌ای نیست.</td></tr>
          <tr v-for="i in review.inputs" :key="i.id">
            <td>{{ i.categoryName || reviewInputCategories[i.category] }} <span v-if="i.autoDrafted" class="inv-badge info">پیش‌نویس خودکار</span></td>
            <td class="small" style="white-space: pre-wrap">{{ i.text }}</td>
            <td><button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-outline-secondary" @click="input = { category: i.category, text: i.text, attendees: review.attendees }">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">تصمیمات و اقدامات (۹.۳.۳)</h2>
        <button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-primary" @click="decision = { id: null, text: '', responsibleUserId: null, dueDate: '', createCapa: false }">تصمیم جدید</button>
      </div>
      <div v-if="decision" class="border rounded p-2 mb-3">
        <div class="row g-2">
          <div class="col-12"><label class="form-label small mb-0">متن تصمیم *</label><textarea v-model="decision.text" class="form-control form-control-sm" rows="2"></textarea></div>
          <div class="col-md-4"><label class="form-label small mb-0">مسئول</label><UserSelect v-model="decision.responsibleUserId" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مهلت</label><input v-model="decision.dueDate" class="form-control form-control-sm" type="date" /></div>
          <div v-if="!decision.id" class="col-md-3 d-flex align-items-end"><label class="form-check small"><input v-model="decision.createCapa" class="form-check-input" type="checkbox" /> ایجاد پیش‌نویس CAPA</label></div>
          <div class="col-12 d-flex gap-2"><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveDecision">ثبت</button><button class="btn btn-sm btn-outline-secondary" @click="decision = null">انصراف</button></div>
        </div>
      </div>
      <table class="table table-sm mb-0">
        <thead><tr><th>تصمیم</th><th>مسئول</th><th>مهلت</th><th>CAPA</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!review.decisions.length"><td colspan="5" class="text-center inv-muted">تصمیمی ثبت نشده.</td></tr>
          <tr v-for="d in review.decisions" :key="d.id">
            <td class="small" style="white-space: pre-wrap">{{ d.text }}</td><td>{{ d.responsibleName }}</td><td>{{ jalali(d.dueDate) }}</td>
            <td><router-link v-if="d.capaId" :to="{ name: 'QualityCapaEdit', params: { id: d.capaId } }" class="inv-ltr">{{ d.capaNumber }}</router-link></td>
            <td><button v-if="canWrite && review.status !== 2" class="btn btn-sm btn-outline-secondary" @click="decision = { ...d, dueDate: dateInput(d.dueDate), createCapa: false }">ویرایش</button></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
