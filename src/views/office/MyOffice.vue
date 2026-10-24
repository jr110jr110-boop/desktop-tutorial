<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import DelegationForm from "@/components/office/wf/DelegationForm.vue";
import DelegationTable from "@/components/office/wf/DelegationTable.vue";
import { uploadFile } from "@/api/InventoryAPIs";
import { getMyOffice, openDocument, saveMySignature } from "@/api/OfficeAPIs";
import { fail, ok } from "@/composables/inventory";

// My office: my positions (own and on behalf), my signature image and the deputies of my own positions.
const me = ref<any>(null);
const busy = ref(false);
const form = ref(false);
const table = ref<any>(null);
const own = computed(() => (me.value?.positions || []).filter((p: any) => !p.onBehalf).map((p: any) => p.positionId as number));

async function load() {
  const [d, error] = await getMyOffice();
  if (error) return fail(error);
  me.value = d;
}
async function onFile(e: Event) {
  const input = e.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = "";
  if (!file) return;
  if (!file.type.startsWith("image/")) return fail({ message: "فقط فایل تصویر (png یا jpg) پذیرفته می‌شود." });
  busy.value = true;
  const [id, upError] = await uploadFile(file, "تصویر امضا");
  if (upError || !id) {
    busy.value = false;
    return fail(upError || { message: "بارگذاری انجام نشد." });
  }
  const [d, error] = await saveMySignature(id);
  busy.value = false;
  if (error) return fail(error);
  me.value = d;
  ok("تصویر امضا ذخیره شد.");
}
function saved() {
  form.value = false;
  table.value?.reload();
}
// signature preview through the authenticated blob; the object URL is revoked on change and on unmount
const signatureUrl = ref<string | null>(null);
function dropSignature() {
  if (signatureUrl.value) URL.revokeObjectURL(signatureUrl.value);
  signatureUrl.value = null;
}
watch(() => me.value?.signatureDocumentId, async (id) => {
  dropSignature();
  if (!id) return;
  const [url] = await openDocument(id);
  signatureUrl.value = url;
});
onBeforeUnmount(dropSignature);
onMounted(load);
</script>

<template>
  <div class="inv-page">
    <div class="inv-card">
      <div class="inv-toolbar"><h1 class="inv-title">دفتر کار من</h1></div>
      <div v-if="me">
        <h2 class="h6">سمت‌های من</h2>
        <div class="table-responsive">
          <table class="table table-sm">
            <thead><tr><th>سمت</th><th>واحد</th><th>نحوه دسترسی</th><th>ویژگی</th></tr></thead>
            <tbody>
              <tr v-if="!me.positions?.length"><td colspan="4" class="text-center inv-muted">سمتی به شما داده نشده است.</td></tr>
              <tr v-for="p in me.positions" :key="p.positionId">
                <td>{{ p.title }}</td><td>{{ p.unitTitle }}</td>
                <td><span :class="p.onBehalf ? 'warn' : 'ok'" class="inv-badge">{{ p.onBehalf ? "به نیابت" : "دارنده" }}</span></td>
                <td><span v-if="p.isManager" class="inv-badge info">مدیر واحد</span> <span v-if="p.canSign" class="inv-badge ok">حق امضا</span></td>
              </tr>
            </tbody>
          </table>
        </div>

        <h2 class="h6 mt-3">تصویر امضای من</h2>
        <p class="small mb-1">
          <span :class="me.hasSignature ? 'ok' : 'warn'" class="inv-badge">{{ me.hasSignature ? "تصویر امضا ثبت شده است" : "تصویر امضا ثبت نشده است" }}</span>
          <span class="inv-muted"> برای امضای نامه‌ها لازم است. تصویر (png یا jpg) با پس‌زمینه سفید یا شفاف بارگذاری کنید.</span>
        </p>
        <img v-if="signatureUrl" :src="signatureUrl" alt="" class="d-block border rounded mb-2 bg-white" style="max-height: 6rem; max-width: 16rem" />
        <input :disabled="busy" type="file" accept="image/png,image/jpeg" class="form-control form-control-sm" style="max-width: 24rem" @change="onFile" />
      </div>
    </div>

    <div class="inv-card">
      <div class="inv-toolbar">
        <h2 class="inv-title">نیابت سمت‌های من</h2>
        <button :disabled="!own.length" class="btn btn-sm btn-primary" @click="form = true">ثبت جانشین</button>
      </div>
      <DelegationTable ref="table" :filterable="false" :mine="true" />
    </div>
    <DelegationForm v-if="form" :only="own" @close="form = false" @saved="saved" />
  </div>
</template>

<style lang="scss" src="../fn/fn.scss"></style>
