<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import QPager from "@/components/quality/QPager.vue";
import EnChip from "@/components/engineering/EnChip.vue";
import { getEnDebts } from "@/api/EngineeringAPIs";
import { debtStatuses, enPaths, faDigits, faNum, officeHoursUntil, stamp } from "@/composables/engineering";
import { usePagedList } from "@/composables/fn";

// The document debts of the emergency waivers: due date and time, overdue in red, hours left. A debt is settled by the system when the document exists.
const route = useRoute();
const filters = ref({ Status: route.query.overdue ? 0 : (null as number | null), OnlyOverdue: !!route.query.overdue });
const list = usePagedList(getEnDebts, filters, 25);
onMounted(list.load);
watch(() => route.query.overdue, (v) => {
  filters.value.OnlyOverdue = !!v;
  filters.value.Status = v ? 0 : null;
  list.search();
});
const left = (iso: string) => {
  const h = officeHoursUntil(iso);
  return h === null ? "" : h >= 0 ? `${faNum(h, 0)} ساعت اداری مانده` : `${faNum(-h, 0)} ساعت اداری گذشته`;
};
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">بدهی مدارک مهندسی</h1></div>
      <p class="small inv-muted" data-test="en-office-hint">مهلت‌ها به ساعت اداری است. ساعت اداری: ۸ تا ۱۹، شنبه تا پنجشنبه؛ این مهلت فقط برای انسان است.</p>
      <div class="inv-filters mb-3">
        <select v-model="filters.Status" class="form-select form-select-sm" aria-label="وضعیت">
          <option :value="null">همه وضعیت‌ها</option>
          <option v-for="(t, k) in debtStatuses" :key="k" :value="Number(k)">{{ t }}</option>
        </select>
        <label class="small"><input v-model="filters.OnlyOverdue" type="checkbox" /> فقط گذشته از موعد</label>
        <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
      </div>
      <p v-if="list.loading.value" class="inv-muted">در حال خواندن…</p>
      <p v-else-if="!list.rows.value.length" class="inv-muted" data-test="en-empty">بدهی مدارکی با این شرایط نیست.</p>
      <div v-else class="table-responsive">
        <table class="table table-sm table-hover">
          <thead><tr><th>مدرک</th><th>مجوز اضطرار</th><th>پرونده</th><th>پلتفرم</th><th>مهلت</th><th>وضعیت</th></tr></thead>
          <tbody>
            <tr v-for="b in list.rows.value" :key="b.id" :class="b.overdue && b.status === 0 ? 'table-danger' : ''">
              <td>{{ b.handoverLineId ? b.documentType + " — " + b.title : "تایید کامل بودن پرونده پلتفرم" }}</td>
              <td><router-link :to="enPaths.waivers + '/' + b.waiverId">{{ faDigits(b.waiverNumber) }}</router-link></td>
              <td><router-link :to="enPaths.case(b.caseId)">{{ faDigits(b.caseNumber) }}</router-link></td>
              <td><router-link v-if="b.productId" :to="enPaths.platform(b.productId, b.referenceCode)">پرونده استاندارد {{ b.referenceCode }}</router-link><span v-else>—</span></td>
              <td>{{ stamp(b.dueAt) }}<div v-if="b.status === 0" :class="['small', b.overdue ? 'text-danger fw-bold' : 'inv-muted']">{{ b.overdue ? `گذشته از موعد (${faNum(b.overdueDays, 0)} روز) — ` : "" }}{{ left(b.dueAt) }}</div></td>
              <td><EnChip :status="b.status" :text="b.statusText" group="debt" /><div v-if="b.settledAtText" class="small inv-muted">{{ faDigits(b.settledAtText) }}</div><div v-if="b.status === 2 && b.voidReason" class="small inv-muted" data-test="en-debt-void">{{ b.voidReason }}</div></td>
            </tr>
          </tbody>
        </table>
      </div>
      <QPager :page="list.page.value" :page-size="25" :total="list.total.value" @go="list.go" />
    </div>
  </div>
</template>
