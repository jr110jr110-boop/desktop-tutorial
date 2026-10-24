<script setup lang="ts">
// Leave, mission, overtime pre-approval and forgotten-clock requests. Route /office/leaves/:id opens the detail of that request.
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import FnDate from "@/components/fn/FnDate.vue";
import QPager from "@/components/quality/QPager.vue";
import EmployeePick from "@/components/office/att/EmployeePick.vue";
import LeaveBalanceCard from "@/components/office/att/LeaveBalanceCard.vue";
import LeaveDetail from "@/components/office/att/LeaveDetail.vue";
import LeaveForm from "@/components/office/att/LeaveForm.vue";
import { days2 } from "@/components/office/att/attCommon";
import { getLeaves } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { badgeOfLeave, leaveKinds, leaveStatuses } from "@/composables/office";
import { hasRole, OfficeRoles } from "@/composables/roles";

const route = useRoute();
const router = useRouter();
const isHr = hasRole(OfficeRoles.hr);
const canAll = hasRole(OfficeRoles.attendanceReaders);
const employee = ref<any>(null);
const status = ref<number | "">("");
const kind = ref<number | "">("");
const from = ref<string | null>(null);
const to = ref<string | null>(null);
const scope = ref<"" | "mine" | "waiting">("");
const filters = ref<any>({});
const list = usePagedList((p: any) => getLeaves(p), filters);
const form = ref<{ leave: any | null } | null>(null);

const openId = computed(() => (route.params.id ? Number(route.params.id) : 0));
function search() {
  filters.value = {
    EmployeeId: employee.value?.id || null, Status: status.value === "" ? null : status.value, Kind: kind.value === "" ? null : kind.value, From: from.value, To: to.value,
    MineOnly: scope.value === "mine" ? true : null, WaitingForMe: scope.value === "waiting" ? true : null,
  };
  return list.search();
}
const openDetail = (id: number) => router.push({ name: "OfLeave", params: { id } });
const closeDetail = () => router.push({ name: "OfLeaves" });
function edit(leave: any) {
  closeDetail();
  form.value = { leave };
}
function saved(d: any) {
  form.value = null;
  list.load();
  if (d?.id) openDetail(d.id);
}
const range = (l: any) => (l.fromDateText === l.toDateText || !l.toDateText ? l.fromDateText : `${l.fromDateText} تا ${l.toDateText}`);
const amount = (l: any) => (l.kind === 6 ? `${days2(l.hours)} ساعت` : l.kind === 5 || l.kind === 7 ? (l.fromTime ? `${l.fromTime}${l.toTime ? " - " + l.toTime : ""}` : "-") : `${days2(l.days)} روز`);
const detailKey = ref(0);
watch(openId, () => detailKey.value++);
onMounted(search);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">مرخصی، ماموریت و اضافه‌کار</h1><button class="btn btn-sm btn-primary" @click="form = { leave: null }">درخواست جدید</button></div>
      <LeaveBalanceCard :can-pick="isHr || canAll" />
      <div class="inv-filters mb-3">
        <div v-if="isHr || canAll" style="min-width: 220px"><label class="form-label small mb-0">کارمند</label><EmployeePick v-model="employee" placeholder="همه" /></div>
        <div><label class="form-label small mb-0">نوع</label><select v-model="kind" class="form-select form-select-sm"><option value="">همه</option><option v-for="(t, v) in leaveKinds" :key="v" :value="Number(v)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">وضعیت</label><select v-model="status" class="form-select form-select-sm"><option value="">همه</option><option v-for="(t, v) in leaveStatuses" :key="v" :value="Number(v)">{{ t }}</option></select></div>
        <div><label class="form-label small mb-0">از تاریخ</label><FnDate v-model="from" /></div>
        <div><label class="form-label small mb-0">تا تاریخ</label><FnDate v-model="to" /></div>
        <div><label class="form-label small mb-0">نمایش</label><select v-model="scope" class="form-select form-select-sm"><option value="">همه قابل دیدن</option><option value="mine">فقط درخواست‌های من</option><option value="waiting">در انتظار تایید من</option></select></div>
        <button class="btn btn-sm btn-secondary align-self-end" @click="search">نمایش</button>
      </div>
      <div class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>شماره</th><th>کارمند</th><th>نوع</th><th>تاریخ</th><th>مقدار</th><th>وضعیت</th><th>تاییدکننده</th><th></th></tr></thead>
          <tbody>
            <tr v-if="!list.rows.value.length"><td colspan="8" class="text-center inv-muted">درخواستی پیدا نشد.</td></tr>
            <tr v-for="l in list.rows.value" :key="l.id" style="cursor: pointer" @click="openDetail(l.id)">
              <td class="inv-ltr">{{ l.number }}</td><td>{{ l.employeeName }}</td><td>{{ leaveKinds[l.kind] }}</td><td class="inv-ltr">{{ range(l) }}</td><td class="fn-num">{{ amount(l) }}</td>
              <td><span :class="badgeOfLeave(l.status)" class="inv-badge">{{ leaveStatuses[l.status] }}</span></td><td>{{ l.approverPositionTitle }}</td>
              <td><button class="btn btn-sm btn-outline-secondary" @click.stop="openDetail(l.id)">جزئیات</button></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    </div>

    <LeaveDetail v-if="openId" :id="openId" :key="detailKey" @changed="list.load()" @close="closeDetail" @edit="edit" />
    <LeaveForm v-if="form" :is-hr="isHr" :leave="form.leave" @close="form = null" @saved="saved" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
