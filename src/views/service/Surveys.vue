<script setup lang="ts">
// Satisfaction surveys (SvSurveys): drafted by the system, sent, answered (1-5 per question) or skipped; the average is computed by the API (/service/surveys/:id opens one).
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import CustomerPick from "@/components/service/CustomerPick.vue";
import { answerSvSurvey, getSvSurvey, getSvSurveys, svSurveyAction } from "@/api/ServiceAPIs";
import { usePagedList } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { hasRole, ServiceRoles } from "@/composables/roles";
import { badgeOfScore, badgeOfSurvey, serviceActionTitles, surveyKinds, surveyStatuses } from "@/composables/service";

const route = useRoute();
const router = useRouter();
const canWork = hasRole(ServiceRoles.surveyWorkers);
const customer = ref<any>(null);
const filters = ref<any>({ Status: null, Kind: null, Contains: "" });
const list = usePagedList(getSvSurveys, computed(() => ({ ...filters.value, CustomerId: customer.value?.id ?? null })) as any);
const surveyId = computed(() => Number(route.params.id) || null);
const survey = ref<any>(null);
const scores = ref<Record<number, number | null>>({});
const comments = ref("");
const busy = ref(false);

function take(s: any) {
  survey.value = s;
  scores.value = Object.fromEntries((s.answers || []).map((a: any) => [a.rowNumber, a.score ?? null]));
  comments.value = s.comments || "";
}
async function load(id: number) {
  const [data, error] = await getSvSurvey(id);
  if (error) return fail(error);
  take(data);
}
watch(surveyId, (id) => (id ? load(id) : (survey.value = null)));
const open = (s: any) => router.push("/service/surveys/" + s.id);
const back = () => {
  router.push("/service/surveys");
  list.load();
};

async function act(action: "send" | "skip") {
  if (action === "skip" && !window.confirm("نظرسنجی رد شود؟")) return;
  busy.value = true;
  const [data, error] = await svSurveyAction({ id: survey.value.id, action, rowVersion: survey.value.rowVersion });
  busy.value = false;
  if (error) {
    fail(error);
    return load(survey.value.id);
  }
  take(data);
  ok(serviceActionTitles[action] + " انجام شد.");
  list.load();
}
async function answer() {
  const given = Object.entries(scores.value).filter(([, v]) => v).map(([k, v]) => ({ rowNumber: Number(k), score: Number(v) }));
  if (!given.length) return fail("امتیاز حداقل یک پرسش را وارد کنید.");
  busy.value = true;
  const [data, error] = await answerSvSurvey({ id: survey.value.id, scores: given, comments: comments.value, rowVersion: survey.value.rowVersion });
  busy.value = false;
  if (error) {
    fail(error);
    return load(survey.value.id);
  }
  take(data);
  ok("پاسخ مشتری ثبت شد.");
  list.load();
}
onMounted(() => {
  list.load();
  if (surveyId.value) load(surveyId.value);
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <template v-if="!surveyId">
        <div class="inv-toolbar"><h1 class="inv-title">نظرسنجی رضایت مشتری</h1></div>
        <div class="inv-filters mb-3">
          <div><label class="form-label small mb-0">مشتری</label><CustomerPick v-model="customer" /></div>
          <div><label class="form-label small mb-0">وضعیت</label>
            <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in surveyStatuses" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div><label class="form-label small mb-0">نوع</label>
            <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه</option><option v-for="(t, k) in surveyKinds" :key="k" :value="Number(k)">{{ t }}</option></select></div>
          <div><label class="form-label small mb-0">شماره</label><input v-model="filters.Contains" class="form-control form-control-sm" @keyup.enter="list.search" /></div>
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>مشتری</th><th>نوع</th><th>پیش‌نویس</th><th>ارسال</th><th>پاسخ</th><th>میانگین</th><th>وضعیت</th><th></th></tr></thead>
            <tbody>
              <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">نظرسنجی‌ای نیست.</td></tr>
              <tr v-for="s in list.rows.value" :key="s.id">
                <td class="inv-ltr">{{ s.number }}</td><td>{{ s.customerName }}</td><td>{{ s.kindText }}</td><td>{{ s.draftedOnText }}</td><td>{{ s.sentAtText || "-" }}</td><td>{{ s.answeredAtText || "-" }}</td>
                <td><span v-if="s.averageScore !== null && s.averageScore !== undefined" :class="badgeOfScore(s.averageScore)" class="inv-badge">{{ Number(s.averageScore).toFixed(2) }}</span><span v-else>-</span></td>
                <td><span :class="badgeOfSurvey(s.status)" class="inv-badge">{{ s.statusText }}</span></td>
                <td><button class="btn btn-sm btn-outline-primary" @click="open(s)">باز کردن</button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" :page-size="list.pageSize" @go="list.go" />
      </template>

      <template v-else-if="survey">
        <div class="inv-toolbar">
          <div>
            <h1 class="inv-title mb-0">نظرسنجی <span class="inv-ltr">{{ survey.number }}</span></h1>
            <span :class="badgeOfSurvey(survey.status)" class="inv-badge">{{ survey.statusText }}</span>
            <span class="inv-badge info ms-1">{{ survey.kindText }}</span>
          </div>
          <button class="btn btn-sm btn-outline-secondary" @click="back">بازگشت به فهرست</button>
        </div>
        <div class="small mb-3">
          <span class="me-3"><b>مشتری:</b> {{ survey.customerName }}</span>
          <span v-if="survey.serviceOrderId" class="me-3"><a href="#" @click.prevent="router.push('/service/orders/' + survey.serviceOrderId)">دستور خدمات مرتبط</a></span>
          <span class="me-3"><b>پیش‌نویس:</b> {{ survey.draftedOnText }}</span>
          <span v-if="survey.sentAtText" class="me-3"><b>ارسال:</b> {{ survey.sentAtText }}</span>
          <span v-if="survey.answeredAtText" class="me-3"><b>پاسخ:</b> {{ survey.answeredAtText }}</span>
          <span v-if="survey.averageScore !== null && survey.averageScore !== undefined" class="me-3"><b>میانگین:</b> <span :class="badgeOfScore(survey.averageScore)" class="inv-badge">{{ Number(survey.averageScore).toFixed(2) }}</span></span>
        </div>
        <div v-if="canWork && (survey.status === 1 || survey.status === 2)" class="d-flex gap-2 mb-3">
          <button v-if="survey.status === 1" :disabled="busy" class="btn btn-sm btn-primary" @click="act('send')">ارسال برای مشتری</button>
          <button :disabled="busy" class="btn btn-sm btn-outline-danger" @click="act('skip')">رد کردن</button>
        </div>
        <table class="table table-sm">
          <thead><tr><th style="width: 3rem">ردیف</th><th>پرسش</th><th style="width: 12rem">امتیاز (۱ تا ۵)</th></tr></thead>
          <tbody>
            <tr v-for="a in survey.answers" :key="a.id">
              <td>{{ a.rowNumber }}</td><td>{{ a.question }}</td>
              <td>
                <select v-if="canWork && survey.status === 2" v-model="scores[a.rowNumber]" class="form-select form-select-sm"><option :value="null">-</option><option v-for="n in 5" :key="n" :value="n">{{ n }}</option></select>
                <span v-else-if="a.score" :class="badgeOfScore(a.score)" class="inv-badge">{{ a.score }}</span><span v-else>-</span>
              </td>
            </tr>
          </tbody>
        </table>
        <template v-if="canWork && survey.status === 2">
          <label class="form-label small mb-0">توضیحات مشتری</label>
          <textarea v-model="comments" class="form-control form-control-sm mb-2" rows="2"></textarea>
          <button :disabled="busy" class="btn btn-sm btn-success" @click="answer">ثبت پاسخ مشتری</button>
          <p class="inv-muted mt-1">پاسخ فقط برای نظرسنجی ارسال‌شده ثبت می‌شود؛ میانگین را سرور حساب می‌کند.</p>
        </template>
        <p v-else-if="survey.comments" class="small"><b>توضیحات مشتری:</b> {{ survey.comments }}</p>
      </template>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
<style lang="scss" src="../fn/fn.scss"></style>
