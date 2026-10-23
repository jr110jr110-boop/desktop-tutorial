<script setup lang="ts">
import { onMounted, ref } from "vue";
import FnDate from "@/components/fn/FnDate.vue";
import PcTabs from "@/components/pc/PcTabs.vue";
import { getIntercompanyPairs, getLegalEntities, postIntercompany, saveIntercompanyPair, saveLegalEntity, setDefaultEntity } from "@/api/FnAPIs";
import { fail, jalali, ok, today } from "@/composables/inventory";
import { money, parseAmount, yesNo } from "@/composables/fn";
import { FnRoles, hasRole } from "@/composables/roles";

// Legal entities (واحدهای حقوقی), the default entity of the user, intercompany pairs and intercompany postings.
const tab = ref("entities");
const tabs = [{ key: "entities", title: "واحدهای حقوقی" }, { key: "pairs", title: "جفت حساب‌های بین‌شرکتی" }, { key: "post", title: "ثبت تراکنش بین‌شرکتی" }];
const entities = ref<any[]>([]);
const pairs = ref<any[]>([]);
const eForm = ref<any>(null);
const pForm = ref<any>(null);
const post = ref<any>({ pairId: null, date: today(), amount: "", chargerEntityId: null, counterAccountACode: "", counterAccountBCode: "", description: "" });
const result = ref<any>(null);
const busy = ref(false);
const canManage = hasRole(FnRoles.managers);
const canPost = hasRole(FnRoles.accountants);

const entityName = (id: number) => entities.value.find((e) => e.id === id)?.name || String(id);
const pairOf = (id: number | null) => pairs.value.find((p) => p.id === id);

async function load() {
  const [e, error] = await getLegalEntities();
  if (error) return fail(error);
  entities.value = e || [];
  const [p] = await getIntercompanyPairs();
  pairs.value = p || [];
}

async function saveEntity() {
  const f = eForm.value;
  if (!String(f.code).trim() || !String(f.name).trim()) return fail("کد و نام واحد را وارد کنید.");
  busy.value = true;
  const [, error] = await saveLegalEntity({ id: f.id, code: String(f.code).trim().toUpperCase(), name: f.name, jurisdiction: f.jurisdiction || null, baseCurrency: String(f.baseCurrency || "IRR").trim().toUpperCase(), isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("واحد حقوقی ذخیره شد.");
  eForm.value = null;
  load();
}

async function makeDefault(e: any) {
  const [, error] = await setDefaultEntity(e.id);
  if (error) return fail(error);
  ok("واحد پیش‌فرض شما تغییر کرد.");
  load();
}

async function savePair() {
  const f = pForm.value;
  if (!String(f.title).trim() || !f.entityAId || !f.entityBId || f.entityAId === f.entityBId) return fail("عنوان و دو واحد متفاوت را مشخص کنید.");
  if (!String(f.accountACode).trim() || !String(f.accountBCode).trim()) return fail("کد حساب هر دو طرف را وارد کنید.");
  busy.value = true;
  const [, error] = await saveIntercompanyPair({ id: f.id, title: f.title, entityAId: Number(f.entityAId), entityBId: Number(f.entityBId), accountACode: String(f.accountACode).trim(), accountBCode: String(f.accountBCode).trim(), isActive: f.isActive });
  busy.value = false;
  if (error) return fail(error);
  ok("جفت حساب ذخیره شد.");
  pForm.value = null;
  load();
}

async function doPost() {
  const f = post.value;
  const amount = parseAmount(f.amount);
  if (!f.pairId || !f.date || !amount || !f.chargerEntityId) return fail("جفت حساب، تاریخ، مبلغ و واحد صادرکننده را وارد کنید.");
  if (!String(f.counterAccountACode).trim() || !String(f.counterAccountBCode).trim()) return fail("حساب هزینه/درآمد هر دو واحد را وارد کنید.");
  busy.value = true;
  const [d, error] = await postIntercompany({ pairId: f.pairId, date: f.date, amount, chargerEntityId: f.chargerEntityId, counterAccountACode: String(f.counterAccountACode).trim(), counterAccountBCode: String(f.counterAccountBCode).trim(), description: f.description || null });
  busy.value = false;
  if (error) return fail(error);
  result.value = d;
  ok("تراکنش بین‌شرکتی در هر دو واحد ثبت شد.");
}

onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card screen-only">
      <div class="inv-toolbar">
        <h1 class="inv-title">واحدهای حقوقی و تراکنش‌های بین‌شرکتی</h1>
        <button v-if="canManage && tab === 'entities'" class="btn btn-sm btn-primary" @click="eForm = { id: null, code: '', name: '', jurisdiction: '', baseCurrency: 'IRR', isActive: true }">واحد جدید</button>
        <button v-if="canManage && tab === 'pairs'" class="btn btn-sm btn-primary" @click="pForm = { id: null, title: '', entityAId: null, entityBId: null, accountACode: '', accountBCode: '', isActive: true }">جفت حساب جدید</button>
      </div>
      <PcTabs v-model="tab" :tabs="tabs" />

      <template v-if="tab === 'entities'">
        <table class="table table-sm">
          <thead><tr><th>کد</th><th>نام</th><th>حوزه قضایی</th><th>ارز پایه</th><th>فعال</th><th>پیش‌فرض من</th><th></th></tr></thead>
          <tbody>
            <tr v-for="e in entities" :key="e.id">
              <td class="inv-ltr">{{ e.code }}</td><td>{{ e.name }}</td><td>{{ e.jurisdiction }}</td><td class="inv-ltr">{{ e.baseCurrency }}</td><td>{{ yesNo(e.isActive) }}</td>
              <td><span v-if="e.isDefault" class="inv-badge ok">پیش‌فرض</span></td>
              <td class="text-nowrap">
                <button v-if="!e.isDefault && e.isActive" class="btn btn-sm btn-outline-primary me-1" @click="makeDefault(e)">پیش‌فرض شود</button>
                <button v-if="canManage" class="btn btn-sm btn-outline-secondary" @click="eForm = { ...e }">ویرایش</button>
              </td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else-if="tab === 'pairs'">
        <table class="table table-sm">
          <thead><tr><th>عنوان</th><th>واحد الف</th><th>حساب الف</th><th>واحد ب</th><th>حساب ب</th><th>فعال</th><th v-if="canManage"></th></tr></thead>
          <tbody>
            <tr v-if="!pairs.length"><td colspan="7" class="text-center inv-muted">جفت حسابی تعریف نشده است.</td></tr>
            <tr v-for="p in pairs" :key="p.id">
              <td>{{ p.title }}</td><td>{{ entityName(p.entityAId) }}</td><td class="inv-ltr">{{ p.accountACode }}</td><td>{{ entityName(p.entityBId) }}</td><td class="inv-ltr">{{ p.accountBCode }}</td><td>{{ yesNo(p.isActive) }}</td>
              <td v-if="canManage"><button class="btn btn-sm btn-outline-secondary" @click="pForm = { ...p }">ویرایش</button></td>
            </tr>
          </tbody>
        </table>
      </template>

      <template v-else>
        <p v-if="!canPost" class="inv-muted">ثبت تراکنش بین‌شرکتی با حسابدار یا مدیر مالی است.</p>
        <div class="row g-2 mb-2">
          <div class="col-md-4"><label class="form-label small mb-0">جفت حساب *</label><select v-model="post.pairId" :disabled="!canPost" class="form-select form-select-sm" @change="post.chargerEntityId = pairOf(post.pairId)?.entityAId || null"><option :value="null">-</option><option v-for="p in pairs.filter((x) => x.isActive)" :key="p.id" :value="p.id">{{ p.title }}</option></select></div>
          <div class="col-md-2"><label class="form-label small mb-0">تاریخ *</label><FnDate v-model="post.date" :disabled="!canPost" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">مبلغ (ریال) *</label><input v-model="post.amount" :disabled="!canPost" class="form-control form-control-sm fn-num" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">واحد صادرکننده *</label>
            <select v-model="post.chargerEntityId" :disabled="!canPost || !post.pairId" class="form-select form-select-sm"><option :value="null">-</option><option v-if="pairOf(post.pairId)" :value="pairOf(post.pairId).entityAId">{{ entityName(pairOf(post.pairId).entityAId) }}</option><option v-if="pairOf(post.pairId)" :value="pairOf(post.pairId).entityBId">{{ entityName(pairOf(post.pairId).entityBId) }}</option></select>
          </div>
          <div class="col-md-3"><label class="form-label small mb-0">حساب هزینه/درآمد واحد الف *</label><input v-model="post.counterAccountACode" :disabled="!canPost" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-3"><label class="form-label small mb-0">حساب هزینه/درآمد واحد ب *</label><input v-model="post.counterAccountBCode" :disabled="!canPost" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-md-6"><label class="form-label small mb-0">شرح</label><input v-model="post.description" :disabled="!canPost" class="form-control form-control-sm" /></div>
        </div>
        <button v-if="canPost" :disabled="busy" class="btn btn-sm btn-primary" @click="doPost">ثبت در هر دو واحد</button>
        <div v-if="result" class="alert alert-success py-2 mt-3">
          تراکنش <span class="inv-ltr">{{ result.number }}</span> به مبلغ {{ money(result.amount) }} ریال در تاریخ {{ jalali(result.date) }} ثبت شد (سند واحد الف: {{ result.voucherAId ?? "در انتظار" }}، سند واحد ب: {{ result.voucherBId ?? "در انتظار" }}).
        </div>
      </template>
    </div>

    <div v-if="eForm" class="inv-modal-backdrop screen-only" @click.self="eForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">واحد حقوقی</h2>
        <div class="mb-2"><label class="form-label small mb-0">کد *</label><input v-model="eForm.code" class="form-control form-control-sm inv-ltr" /></div>
        <div class="mb-2"><label class="form-label small mb-0">نام *</label><input v-model="eForm.name" class="form-control form-control-sm" /></div>
        <div class="mb-2"><label class="form-label small mb-0">حوزه قضایی (کشور)</label><input v-model="eForm.jurisdiction" class="form-control form-control-sm" /></div>
        <div class="mb-2"><label class="form-label small mb-0">ارز پایه</label><input v-model="eForm.baseCurrency" class="form-control form-control-sm inv-ltr" /></div>
        <label class="form-check small"><input v-model="eForm.isActive" class="form-check-input" type="checkbox" /> فعال</label>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="eForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="saveEntity">ذخیره</button></div>
      </div>
    </div>

    <div v-if="pForm" class="inv-modal-backdrop screen-only" @click.self="pForm = null">
      <div class="inv-modal">
        <h2 class="inv-title mb-2">جفت حساب بین‌شرکتی</h2>
        <div class="mb-2"><label class="form-label small mb-0">عنوان *</label><input v-model="pForm.title" class="form-control form-control-sm" /></div>
        <div class="row g-2 mb-2">
          <div class="col-6"><label class="form-label small mb-0">واحد الف *</label><select v-model="pForm.entityAId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="e in entities" :key="e.id" :value="e.id">{{ e.name }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">واحد ب *</label><select v-model="pForm.entityBId" class="form-select form-select-sm"><option :value="null">-</option><option v-for="e in entities" :key="e.id" :value="e.id">{{ e.name }}</option></select></div>
          <div class="col-6"><label class="form-label small mb-0">حساب طرف الف *</label><input v-model="pForm.accountACode" class="form-control form-control-sm inv-ltr" /></div>
          <div class="col-6"><label class="form-label small mb-0">حساب طرف ب *</label><input v-model="pForm.accountBCode" class="form-control form-control-sm inv-ltr" /></div>
        </div>
        <label class="form-check small"><input v-model="pForm.isActive" class="form-check-input" type="checkbox" /> فعال</label>
        <div class="d-flex gap-2 justify-content-end mt-3"><button class="btn btn-sm btn-outline-secondary" @click="pForm = null">انصراف</button><button :disabled="busy" class="btn btn-sm btn-primary" @click="savePair">ذخیره</button></div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" src="./fn.scss"></style>
