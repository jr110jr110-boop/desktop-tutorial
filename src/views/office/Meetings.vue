<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import { getMeetings } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { badgeOfMeeting, meetingKinds, meetingStatuses } from "@/composables/office";

// Meetings list. Own meetings only (creator, chair, secretary, attendee, decision owner; the CEO sees all).
const router = useRouter();
const filters = ref({ Status: null as number | null, Kind: null as number | null, DateFrom: null as string | null, DateTo: null as string | null, MineOnly: false, Contains: "" });
const list = usePagedList(getMeetings, filters);
onMounted(list.load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title">جلسات</h1>
        <router-link :to="{ name: 'OfMeeting', params: { id: 'new' } }" class="btn btn-sm btn-primary">جلسه جدید</router-link>
      </div>
      <div class="inv-filters mb-3">
        <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="جستجو در عنوان، شماره یا محل" @keyup.enter="list.search" />
        <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in meetingStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
        <select v-model="filters.Kind" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="(t, k) in meetingKinds" :key="k" :value="Number(k)">{{ t }}</option></select>
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="filters.DateFrom" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="filters.DateTo" /></div>
        <label class="small d-flex align-items-center gap-1"><input v-model="filters.MineOnly" type="checkbox" class="form-check-input mt-0" />فقط جلسات من</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>عنوان</th><th>نوع</th><th>زمان</th><th>محل</th><th>رئیس جلسه</th><th>مصوبات باز</th><th>معوق</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="9" class="text-center inv-muted">جلسه‌ای نیست.</td></tr>
            <tr v-for="m in list.rows.value" :key="m.id" style="cursor: pointer" @click="router.push({ name: 'OfMeeting', params: { id: m.id } })">
              <td class="inv-ltr">{{ m.number }}</td><td>{{ m.title }}</td><td>{{ meetingKinds[m.kind] }}</td><td class="text-nowrap inv-ltr">{{ m.startAtText }}</td><td>{{ m.place }}</td><td>{{ m.chairPositionTitle }}</td>
              <td>{{ m.openDecisions }}</td><td><span v-if="m.overdueDecisions" class="inv-badge bad">{{ m.overdueDecisions }}</span></td>
              <td><span :class="badgeOfMeeting(m.status)" class="inv-badge">{{ meetingStatuses[m.status] }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
