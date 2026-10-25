<script setup lang="ts">
import { onMounted, ref } from "vue";
import LookupBox from "@/components/fn/LookupBox.vue";
import QPager from "@/components/quality/QPager.vue";
import StandardEditor from "./StandardEditor.vue";
import { getOperationStandards } from "@/api/ProductionAPIs";
import { usePagedList } from "@/composables/fn";
import { hoursText, inspectionRequirements } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";
import { itemLabel, loadGroups, loadProviders, loadWorkCenters, searchItems } from "./masterKit";

// Operation standards: the routing steps (OperationTemplate) of one item or item group, each with its standard (empty until entered).
const item = ref<any>(null);
const filters = ref({ ItemId: null as number | null, ItemGroupId: null as number | null, Contains: "" });
const list = usePagedList(getOperationStandards, filters, 100);
const groups = ref<any[]>([]);
const centers = ref<any[]>([]);
const providers = ref<any[]>([]);
const editing = ref<any>(null);
const canEdit = hasRole(ProductionRoles.standardEditors);

function pickItem(i: any) {
  item.value = i;
  filters.value.ItemId = i ? i.id : null;
  if (i) filters.value.ItemGroupId = null;
}
function search() {
  if (!filters.value.ItemId && !filters.value.ItemGroupId) filters.value.Contains = filters.value.Contains.trim();
  list.search();
}
onMounted(async () => {
  [groups.value, centers.value, providers.value] = await Promise.all([loadGroups(), loadWorkCenters(), loadProviders()]);
  list.load();
});
defineExpose({ reload: list.load });
</script>

<template>
  <div>
    <div class="inv-filters mb-3">
      <div style="min-width: 260px"><label class="form-label small mb-0">کالا</label><LookupBox :model-value="item" :fetch="searchItems" :label="itemLabel" placeholder="کد یا نام کالا" @pick="pickItem" /></div>
      <div><label class="form-label small mb-0">گروه کالا</label><select v-model="filters.ItemGroupId" class="form-select form-select-sm" @change="filters.ItemGroupId && pickItem(null)"><option :value="null">همه گروه‌ها</option><option v-for="g in groups" :key="g.id" :value="g.id">{{ g.code }} - {{ g.title }}</option></select></div>
      <div><label class="form-label small mb-0">جستجو</label><input v-model="filters.Contains" class="form-control form-control-sm" placeholder="عنوان مرحله" @keyup.enter="search" /></div>
      <button class="btn btn-sm btn-secondary" @click="search">نمایش</button>
    </div>
    <div class="table-responsive">
      <table class="table table-sm table-hover">
        <thead>
          <tr><th>کالا / گروه</th><th>ردیف</th><th>مرحله</th><th>مرکز کار</th><th>راه‌اندازی</th><th>اجرا / واحد</th><th>جابجایی</th><th>صف</th><th>بیرونی</th><th>پیمانکار</th><th>تحویل (روز)</th><th>صلاحیت</th><th>بازرسی</th><th>نمونه اول</th><th></th></tr>
        </thead>
        <tbody>
          <tr v-if="!list.rows.value.length"><td colspan="15" class="text-center inv-muted">مرحله‌ای ثبت نشده است.</td></tr>
          <tr v-for="r in list.rows.value" :key="r.operationTemplateId">
            <td class="inv-ltr">{{ r.itemCode || r.itemGroupCode }}</td><td>{{ r.sequence }}</td><td>{{ r.templateTitle }}</td>
            <td class="inv-ltr">{{ r.workCenterCode || "-" }}</td>
            <td>{{ r.id ? hoursText(r.setupHours) : "-" }}</td><td>{{ r.id ? hoursText(r.runHoursPerUnit) : "-" }}</td><td>{{ r.id ? r.moveDays : "-" }}</td><td>{{ r.id ? r.queueDays : "-" }}</td>
            <td>{{ r.isOutside ? "بله" : "خیر" }}</td><td>{{ r.defaultSupplierName || "-" }}</td><td>{{ r.isOutside ? r.outsideLeadDays : "-" }}</td>
            <td>{{ [r.requiredQualificationKind, r.requiredQualificationProcess].filter(Boolean).join(" / ") || "-" }}</td>
            <td>{{ r.id ? inspectionRequirements[r.inspectionRequirement] : "-" }}<span v-if="r.requiresQc" class="inv-muted"> (QC)</span></td>
            <td>{{ r.firstArticleRequired ? "بله" : "-" }}</td>
            <td class="text-nowrap">
              <span v-if="!r.id" class="inv-badge warn me-1">بدون استاندارد</span>
              <button v-if="canEdit" class="btn btn-sm btn-outline-primary" @click="editing = r">{{ r.id ? "ویرایش" : "ثبت استاندارد" }}</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
    <StandardEditor v-if="editing" :row="editing" :centers="centers" :providers="providers" @close="editing = null" @saved="editing = null; list.load()" />
  </div>
</template>
