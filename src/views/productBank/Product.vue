<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import KsReportHeader from "@/components/common/KsReportHeader.vue";
import ReadinessPanel from "@/components/opsData/ReadinessPanel.vue";
import { approveTemplate, getProductBank, getTemplate, importMspdi } from "@/api/ProductBankAPIs";
import { fail, ok, printPage } from "@/composables/inventory";
import { hasRole } from "@/composables/roles";
import { PbRoles, bomStatusClass, bomStatuses, linkTypes, makeOrBuyShort, num, revisionText, routingStatuses, wbsKinds } from "@/composables/productBank";

// One product of the bank: BOM revisions, OPC routings and the WBS templates (imported from Microsoft Project, "Save As XML").
const route = useRoute();
const bank = ref<any>(null);
const tab = ref<"bom" | "opc" | "wbs">("bom");
const template = ref<any>(null);
const collapsed = ref<Set<number>>(new Set());
const busy = ref(false);
const canImport = hasRole(PbRoles.writers);
const canApprove = hasRole([...PbRoles.engineering, ...PbRoles.planning]);
const fileInput = ref<HTMLInputElement | null>(null);

async function load() {
  const [data, error] = await getProductBank(Number(route.params.id));
  if (error) return fail(error);
  bank.value = data;
  const preferred = data.templates.find((t: any) => t.isApproved) || data.templates[0];
  if (preferred && (!template.value || template.value.id !== preferred.id)) openTemplate(preferred.id);
}
async function openTemplate(id: number) {
  const [data, error] = await getTemplate(id);
  if (error) return fail(error);
  template.value = data;
  collapsed.value = new Set();
}
async function pickFile(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0];
  if (!file) return;
  const xml = await file.text();
  busy.value = true;
  const [data, error] = await importMspdi({ productId: Number(route.params.id), title: file.name.replace(/\.xml$/i, ""), xml });
  busy.value = false;
  if (fileInput.value) fileInput.value.value = "";
  if (error) return fail(error);
  ok(`${data.nodes.length} فعالیت و ${data.links.length} وابستگی وارد شد؛ قالب پیش‌نویس است و باید تایید شود.`);
  template.value = data;
  tab.value = "wbs";
  load();
}
async function approve() {
  busy.value = true;
  const [data, error] = await approveTemplate(template.value.id);
  busy.value = false;
  if (error) return fail(error);
  ok(data.isApproved ? "قالب تایید نهایی شد." : "تایید اول ثبت شد؛ تایید دوم از یک نفر دیگر (برنامه‌ریزی) لازم است.");
  template.value = data;
  load();
}

const visible = computed(() => {
  if (!template.value) return [];
  const byId = new Map<number, any>(template.value.nodes.map((n: any) => [n.id, n]));
  return template.value.nodes.filter((n: any) => {
    let p = n.parentId;
    while (p) {
      if (collapsed.value.has(p)) return false;
      p = byId.get(p)?.parentId;
    }
    return true;
  });
});
const hasChildren = (n: any) => template.value.nodes.some((x: any) => x.parentId === n.id);
function toggle(n: any) {
  const s = new Set(collapsed.value);
  if (s.has(n.id)) s.delete(n.id);
  else s.add(n.id);
  collapsed.value = s;
}
const successors = (n: any) => (template.value.links || []).filter((l: any) => l.toNodeId === n.id).map((l: any) => {
  const from = template.value.nodes.find((x: any) => x.id === l.fromNodeId);
  return `${from?.title || "?"} (${linkTypes[l.linkType]}${l.lagDays ? (l.lagDays > 0 ? "+" : "") + num(l.lagDays) + "d" : ""})`;
});
onMounted(load);
</script>

<template>
  <div class="inv-page ks-report">
    <KsReportHeader :title="bank ? bank.product.code + ' — ' + bank.product.title : 'بانک محصول'" subtitle="BOM، چارت‌های OPC و قالب WBS" />
    <ReadinessPanel v-if="bank" :product-id="bank.product.id" />
    <div v-if="bank" class="inv-card">
      <div class="inv-toolbar">
        <h1 class="inv-title screen-only">{{ bank.product.title }} <small class="inv-ltr inv-muted">{{ bank.product.code }}</small></h1>
        <div class="d-flex gap-2 d-print-none">
          <button class="btn btn-sm btn-outline-secondary" @click="printPage">چاپ</button>
          <router-link :to="{ name: 'ProductBankProducts' }" class="btn btn-sm btn-outline-secondary">بانک محصول</router-link>
          <router-link :to="{ name: 'DesignDossier', params: { productId: Number($route.params.id) } }" class="btn btn-sm btn-outline-secondary">پرونده طراحی</router-link>
        </div>
      </div>
      <ul class="nav nav-tabs mb-3 d-print-none">
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'bom' }" href="#" @click.prevent="tab = 'bom'">BOM ({{ bank.boms.length }})</a></li>
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'opc' }" href="#" @click.prevent="tab = 'opc'">چارت‌های OPC ({{ bank.routings.length }})</a></li>
        <li class="nav-item"><a class="nav-link" :class="{ active: tab === 'wbs' }" href="#" @click.prevent="tab = 'wbs'">قالب WBS ({{ bank.templates.length }})</a></li>
      </ul>

      <table v-if="tab === 'bom'" class="table table-sm table-hover">
        <thead><tr><th>کد</th><th>نسخه</th><th>وضعیت</th><th>ردیف‌ها</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!bank.boms.length"><td colspan="5" class="text-center inv-muted">BOM ثبت نشده است.</td></tr>
          <tr v-for="b in bank.boms" :key="b.id">
            <td class="inv-ltr">{{ b.code }}</td><td class="inv-ltr">{{ revisionText(b.revision) }}</td>
            <td><span class="inv-badge" :class="bomStatusClass[b.status]">{{ bomStatuses[b.status] }}</span></td><td>{{ b.lineCount }}</td>
            <td><router-link :to="{ name: 'ProductBankBomEdit', params: { id: b.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>

      <table v-if="tab === 'opc'" class="table table-sm table-hover">
        <thead><tr><th>قطعه / مجموعه</th><th>عنوان</th><th>نسخه</th><th>وضعیت</th><th>مرحله‌ها</th><th></th></tr></thead>
        <tbody>
          <tr v-if="!bank.routings.length"><td colspan="6" class="text-center inv-muted">چارتی ثبت نشده است.</td></tr>
          <tr v-for="r in bank.routings" :key="r.id">
            <td class="inv-ltr">{{ r.partNumber }}</td><td>{{ r.title }}</td><td class="inv-ltr">{{ revisionText(r.revision) }}</td>
            <td><span class="inv-badge" :class="r.status === 2 ? 'ok' : 'warn'">{{ routingStatuses[r.status] }}</span></td><td>{{ r.stepCount }}</td>
            <td><router-link :to="{ name: 'ProductBankRoutingEdit', params: { id: r.id } }" class="btn btn-sm btn-outline-primary py-0">باز کردن</router-link></td>
          </tr>
        </tbody>
      </table>

      <div v-if="tab === 'wbs'">
        <div class="d-flex gap-2 align-items-center mb-2 flex-wrap d-print-none">
          <select v-if="bank.templates.length" class="form-select form-select-sm" style="max-width: 260px" :value="template?.id" @change="openTemplate(Number(($event.target as HTMLSelectElement).value))">
            <option v-for="t in bank.templates" :key="t.id" :value="t.id">نسخه {{ t.version }} — {{ t.title }} {{ t.isApproved ? "(تایید شده)" : "(پیش‌نویس)" }}</option>
          </select>
          <template v-if="canImport">
            <input ref="fileInput" type="file" accept=".xml,text/xml" class="d-none" @change="pickFile" />
            <button :disabled="busy" class="btn btn-sm btn-outline-primary" @click="fileInput?.click()">ورود از Microsoft Project (XML)</button>
          </template>
          <button v-if="template && template.canApprove && canApprove" :disabled="busy" class="btn btn-sm btn-success" @click="approve">
            {{ template.approvedByUserId ? "تایید دوم (برنامه‌ریزی)" : "تایید اول (مهندسی)" }}
          </button>
        </div>
        <p class="small inv-muted d-print-none">در Microsoft Project: File ← Save As ← XML Format. ورود‌کننده قالب را تایید نمی‌کند؛ تایید اول مهندسی و تایید دوم یک نفر دیگر از برنامه‌ریزی است.</p>
        <div v-if="template">
          <p class="small">
            مدت کل {{ num(template.totalDurationDays) }} روز کاری · {{ template.nodeCount }} فعالیت · {{ template.links.length }} وابستگی ·
            <span class="inv-badge" :class="template.isApproved ? 'ok' : 'warn'">{{ template.isApproved ? "تایید شده" : "پیش‌نویس" }}</span>
          </p>
          <table class="table table-sm">
            <thead><tr><th>عنوان</th><th>E/P/C</th><th>مدت (روز)</th><th>شماره قطعه</th><th>ساخت/خرید</th><th>منبع</th><th>پیش‌نیازها</th></tr></thead>
            <tbody>
              <tr v-for="n in visible" :key="n.id" :class="{ 'fw-bold table-light': n.kind === 1 }">
                <td :style="{ paddingRight: (n.outlineLevel - 1) * 16 + 'px' }">
                  <a v-if="hasChildren(n)" href="#" class="text-decoration-none" @click.prevent="toggle(n)">{{ collapsed.has(n.id) ? "▸" : "▾" }}</a>
                  {{ n.kind === 3 ? "◆ " : "" }}{{ n.title }}
                  <small class="inv-muted">{{ wbsKinds[n.kind] }}</small>
                </td>
                <td class="inv-ltr">{{ n.phaseCode }}</td>
                <td class="inv-ltr">{{ num(n.durationDays) }}</td>
                <td class="inv-ltr">{{ n.partNumber }}</td>
                <td>{{ n.makeOrBuy ? makeOrBuyShort[n.makeOrBuy] : "" }}</td>
                <td class="inv-ltr">{{ n.resourceClass }}</td>
                <td class="small">{{ successors(n).join("، ") }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p v-else class="inv-muted">قالب WBS ثبت نشده است.</p>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="../inventory/inventory.scss"></style>
