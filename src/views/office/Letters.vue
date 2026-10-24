<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import { getLetters } from "@/api/OfficeAPIs";
import { badgeOfLetter, classifications, letterKinds, letterStatuses, urgencies } from "@/composables/office";
import { usePagedList } from "@/composables/fn";
import { hasRole, OfficeRoles } from "@/composables/roles";

// Letters (نامه‌ها): paged list of the letters the caller may see. Rows with metadataOnly show the register data only.
const router = useRouter();
const filters = ref<Record<string, any>>({ Kind: null, Status: null, Classification: null, Number: "", Subject: "", Party: "", DateFrom: null, DateTo: null, MineOnly: false, WaitingForMe: false, Contains: "" });
const list = usePagedList((p) => getLetters({ ...p, MineOnly: p.MineOnly || null, WaitingForMe: p.WaitingForMe || null }), filters);
const isSecretariat = hasRole(OfficeRoles.secretariat) && OfficeRoles.secretariat.length > 0;

function reset() {
  filters.value = { Kind: null, Status: null, Classification: null, Number: "", Subject: "", Party: "", DateFrom: null, DateTo: null, MineOnly: false, WaitingForMe: false, Contains: "" };
  list.search();
}
function open(id: number) {
  router.push({ name: "OfLetter", params: { id } });
}
function create(kind: number) {
  router.push({ name: "OfLetter", params: { id: "new" }, query: { kind } });
}
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">نامه‌ها</h1>
        <div class="d-flex gap-2 flex-wrap">
          <button class="btn btn-sm btn-primary" @click="create(2)">نامه جدید (صادره)</button>
          <button class="btn btn-sm btn-outline-primary" @click="create(3)">نامه داخلی جدید</button>
          <button v-if="isSecretariat" class="btn btn-sm btn-outline-primary" @click="create(1)">ثبت نامه وارده</button>
        </div>
      </div>

      <div class="inv-filters mb-3">
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in letterKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in letterStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Classification" class="form-select form-select-sm"><option :value="null">همه طبقه‌بندی‌ها</option><option v-for="(t, k) in classifications" :key="k" :value="Number(k)">{{ t }}</option></select>
        <input v-model="filters.Number" class="form-control form-control-sm" placeholder="شماره نامه" @keydown.enter="list.search" />
        <input v-model="filters.Subject" class="form-control form-control-sm" placeholder="موضوع" @keydown.enter="list.search" />
        <input v-model="filters.Party" class="form-control form-control-sm" placeholder="طرف مکاتبه (فرستنده / گیرنده)" @keydown.enter="list.search" />
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.DateFrom" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.DateTo" /></div>
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="جستجوی متن" @keydown.enter="list.search" />
        <label class="small d-flex align-items-center gap-1"><input v-model="filters.MineOnly" type="checkbox" /> فقط نامه‌های من</label>
        <label class="small d-flex align-items-center gap-1"><input v-model="filters.WaitingForMe" type="checkbox" /> منتظر اقدام من</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        <button class="btn btn-sm btn-outline-secondary" @click="reset">پاک‌کردن</button>
      </div>

      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>تاریخ</th><th>نوع</th><th>موضوع</th><th>فرستنده</th><th>گیرندگان</th><th>طبقه‌بندی</th><th>وضعیت</th><th>فوریت</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">{{ list.loading.value ? "در حال بارگذاری..." : "نامه‌ای نیست." }}</td></tr>
            <tr v-for="l in list.rows.value" :key="l.id" style="cursor: pointer" @click="open(l.id)">
              <td class="inv-ltr">{{ l.number || "بدون شماره" }}</td>
              <td>{{ l.dateText }}</td>
              <td>{{ letterKinds[l.kind] }}<span v-if="l.imported" class="inv-badge me-1">آرشیو</span></td>
              <td>
                <span v-if="l.metadataOnly" class="inv-badge warn" title="محتوای این نامه برای دبیرخانه پنهان است">محرمانه — فقط اطلاعات ثبت</span>
                <template v-else>{{ l.subject }}</template>
              </td>
              <td>{{ l.senderName }}</td>
              <td>{{ l.recipientNames }}</td>
              <td><span :class="l.classification === 0 ? 'info' : 'bad'" class="inv-badge">{{ classifications[l.classification] }}</span></td>
              <td><span :class="badgeOfLetter(l.status)" class="inv-badge">{{ letterStatuses[l.status] }}</span></td>
              <td><span v-if="l.urgency > 0" :class="l.urgency === 2 ? 'bad' : 'warn'" class="inv-badge">{{ urgencies[l.urgency] }}</span><span v-else class="inv-muted">{{ urgencies[0] }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
