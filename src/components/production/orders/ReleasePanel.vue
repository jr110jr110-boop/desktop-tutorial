<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import ReasonDialog from "./ReasonDialog.vue";
import { getReleaseCheck, releaseProductionOrder } from "@/api/ProductionAPIs";
import { camelize } from "@/composables/fn";
import { fail, ok } from "@/composables/inventory";
import { qtyText, releaseCheckCodes } from "@/composables/production";
import { ProductionRoles, hasRole } from "@/composables/roles";

// Release gate of a draft order: five checks (routing, work centres, drawings, ITP, materials) and the shortage list. Release: shop supervisor / production manager;
// release with a shortage: production manager only, with a reason. The maker-checker refusal (the person who drafted or edited the order cannot release it) comes back as the API message.
const props = defineProps<{ order: any }>();
const emit = defineEmits<{ (e: "changed", order: any): void }>();
const check = ref<any>(null);
const loading = ref(false);
const busy = ref(false);
const overriding = ref(false);
const canRelease = hasRole(ProductionRoles.supervisors);
const isManager = hasRole(ProductionRoles.manager);

async function load() {
  loading.value = true;
  const [d, error] = await getReleaseCheck(props.order.id);
  loading.value = false;
  if (error) return fail(error);
  check.value = camelize(d);
}

async function release(reason: string | null) {
  busy.value = true;
  const [d, error] = await releaseProductionOrder({ id: props.order.id, overrideReason: reason, rowVersion: props.order.rowVersion });
  busy.value = false;
  if (error) return fail(error);
  overriding.value = false;
  ok("دستور تولید صادر شد.");
  emit("changed", camelize(d));
}

watch(() => props.order.rowVersion, load);
onMounted(load);
defineExpose({ reload: load });
</script>

<template>
  <div class="inv-card">
    <div class="inv-toolbar">
      <h2 class="inv-title">بررسی‌های صدور</h2>
      <button class="btn btn-sm btn-outline-secondary" :disabled="loading" @click="load">بررسی دوباره</button>
    </div>
    <p v-if="!check" class="inv-muted mb-0">{{ loading ? "در حال بررسی..." : "نتیجه بررسی در دسترس نیست." }}</p>
    <template v-else>
      <ul class="list-unstyled mb-2">
        <li v-for="c in check.checks" :key="c.code" class="d-flex gap-2 align-items-start mb-1">
          <span :class="c.passed ? 'ok' : c.overridable ? 'warn' : 'bad'" class="inv-badge">{{ c.passed ? "قبول" : "رد" }}</span>
          <span><b>{{ releaseCheckCodes[c.code] || c.title }}</b><span v-if="c.message" class="inv-muted"> - {{ c.message }}</span></span>
        </li>
      </ul>
      <div v-if="check.shortages?.length" class="table-responsive mb-2">
        <table class="table table-sm">
          <thead><tr><th>قطعه</th><th>مورد نیاز</th><th>موجودی</th><th>رزرو</th><th>در راه</th><th>قابل استفاده</th><th>کمبود</th></tr></thead>
          <tbody>
            <tr v-for="s in check.shortages" :key="s.itemId">
              <td><span class="inv-ltr">{{ s.itemCode }}</span> {{ s.itemTitle }}</td><td>{{ qtyText(s.required) }}</td><td>{{ qtyText(s.onHand) }}</td><td>{{ qtyText(s.reserved) }}</td><td>{{ qtyText(s.incoming) }}</td><td>{{ qtyText(s.available) }}</td>
              <td><b class="text-danger">{{ qtyText(s.short) }}</b></td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="d-flex flex-wrap gap-2 align-items-center">
        <button v-if="canRelease && check.canRelease" :disabled="busy" class="btn btn-sm btn-success" @click="release(null)">صدور دستور تولید</button>
        <button v-else-if="isManager && check.canReleaseWithOverride" :disabled="busy" class="btn btn-sm btn-warning" @click="overriding = true">صدور با کمبود (استثنا)</button>
        <span v-else-if="check.canReleaseWithOverride && canRelease" class="inv-muted">کمبود موجودی وجود دارد؛ صدور با کمبود فقط با مدیر تولید و ذکر دلیل ممکن است.</span>
        <span v-else-if="!check.canRelease && !check.canReleaseWithOverride" class="inv-muted">تا برطرف شدن موارد ردشده، صدور ممکن نیست.</span>
      </div>
    </template>
    <ReasonDialog v-if="overriding" :busy="busy" title="صدور با کمبود موجودی" label="دلیل صدور با کمبود" hint="دلیل در پرونده دستور ثبت می‌شود و برای مدیر عامل قابل مشاهده است." confirm-text="صدور" @close="overriding = false" @confirm="release" />
  </div>
</template>
