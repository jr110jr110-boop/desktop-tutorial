<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import RequestDetail from "@/components/office/wf/RequestDetail.vue";
import RequestForm from "@/components/office/wf/RequestForm.vue";
import QPager from "@/components/quality/QPager.vue";
import { getRequestTypes, getRequests } from "@/api/OfficeAPIs";
import { usePagedList } from "@/composables/fn";
import { badgeOfRequest, requestStatuses } from "@/composables/office";

// General requests: list (/office/requests) and detail (/office/requests/:id, the link written into inbox items).
const route = useRoute();
const router = useRouter();
const filters = ref({ Status: null as number | null, TypeId: null as number | null, MineOnly: false, WaitingForMe: false, Contains: "" });
const list = usePagedList(getRequests, filters);
const types = ref<any[]>([]);
const creating = ref(false);
const detailId = computed(() => (route.params.id ? Number(route.params.id) : null));

watch(detailId, (id) => !id && list.load());
function created(d: any) {
  creating.value = false;
  router.push({ name: "OfRequest", params: { id: d.id } });
}
onMounted(async () => {
  const [t] = await getRequestTypes();
  types.value = Array.isArray(t) ? t : [];
  list.load();
});
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <template v-if="detailId">
        <div class="mb-2"><router-link :to="{ name: 'OfRequests' }" class="btn btn-sm btn-outline-secondary">بازگشت به فهرست</router-link></div>
        <RequestDetail :id="detailId" @changed="list.load" />
      </template>
      <template v-else>
        <div class="inv-toolbar">
          <h1 class="inv-title">درخواست‌های عمومی</h1>
          <button class="btn btn-sm btn-primary" @click="creating = true">درخواست جدید</button>
        </div>
        <div class="inv-filters mb-3">
          <input v-model="filters.Contains" class="form-control form-control-sm" placeholder="جستجو در شماره یا نوع" @keyup.enter="list.search" />
          <select v-model="filters.TypeId" class="form-select form-select-sm"><option :value="null">همه انواع</option><option v-for="t in types" :key="t.id" :value="t.id">{{ t.title }}</option></select>
          <select v-model="filters.Status" class="form-select form-select-sm"><option :value="null">همه وضعیت‌ها</option><option v-for="(t, k) in requestStatuses" :key="k" :value="Number(k)">{{ t }}</option></select>
          <label class="small d-flex align-items-center gap-1"><input v-model="filters.MineOnly" type="checkbox" class="form-check-input mt-0" />درخواست‌های من</label>
          <label class="small d-flex align-items-center gap-1"><input v-model="filters.WaitingForMe" type="checkbox" class="form-check-input mt-0" />منتظر اقدام من</label>
          <button class="btn btn-sm btn-secondary" @click="list.search">جستجو</button>
        </div>
        <div class="table-responsive">
          <table class="table table-sm table-hover">
            <thead><tr><th>شماره</th><th>نوع</th><th>درخواست‌کننده</th><th>ارسال</th><th>وضعیت</th></tr></thead>
            <tbody>
              <tr v-if="!list.rows.value.length"><td colspan="5" class="text-center inv-muted">درخواستی نیست.</td></tr>
              <tr v-for="r in list.rows.value" :key="r.id" style="cursor: pointer" @click="router.push({ name: 'OfRequest', params: { id: r.id } })">
                <td class="inv-ltr">{{ r.number }}</td><td>{{ r.typeTitle }}</td><td>{{ r.requesterName }}</td><td class="inv-ltr">{{ r.submittedAtText }}</td>
                <td><span :class="badgeOfRequest(r.status)" class="inv-badge">{{ requestStatuses[r.status] }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
        <QPager :page="list.page.value" :total="list.total.value" @go="list.go" />
      </template>
    </div>
    <RequestForm v-if="creating" @close="creating = false" @saved="created" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
