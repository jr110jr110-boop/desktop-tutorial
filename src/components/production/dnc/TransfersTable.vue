<script setup lang="ts">
import { ref } from "vue";
import { downloadTransfer, transferAction } from "@/api/DncAPIs";
import { fail, ok } from "@/composables/inventory";
import { badgeOfTransfer, linkKinds, transferDirections, transferStatuses } from "@/composables/dnc";

// Transfers of programs to and from the machines, with the actions the API allows the caller (confirm on the control, cancel, retry, download of a manual transfer).
defineProps<{ rows: any[]; showProgram?: boolean }>();
const emit = defineEmits(["changed"]);
const busy = ref<number | null>(null);

async function act(t: any, action: string) {
  let note: string | null = null;
  if (action === "cancel") {
    note = prompt("دلیل لغو:");
    if (!note) return;
  }
  if (action === "confirm") {
    if (!confirm(`برنامه ${t.targetName} روی کنترلر ماشین ${t.machineTitle} بررسی و تأیید شد؟`)) return;
    note = "روی کنترلر بررسی شد";
  }
  busy.value = t.id;
  const [, error] = await transferAction({ transferId: t.id, action, note, rowVersion: t.rowVersion });
  busy.value = null;
  if (error) return fail(error);
  ok(action === "confirm" ? "دریافت برنامه روی کنترلر تأیید شد." : action === "cancel" ? "انتقال لغو شد." : "انتقال دوباره در صف قرار گرفت.");
  emit("changed");
}
async function download(t: any) {
  busy.value = t.id;
  const message = await downloadTransfer(t.id, t.targetName);
  busy.value = null;
  if (message) return fail(message);
  ok("فایل دانلود شد؛ آن را روی کنترلر بارگذاری و سپس دریافت را تأیید کنید.");
  emit("changed");
}
const titles: Record<string, string> = { confirm: "تأیید روی کنترلر", cancel: "لغو", retry: "تلاش دوباره", download: "دانلود فایل" };
</script>

<template>
  <div class="table-responsive">
    <table class="table table-sm">
      <thead><tr><th>#</th><th>جهت</th><th>ماشین</th><th v-if="showProgram">برنامه</th><th>نسخه</th><th>نام روی کنترلر</th><th>اتصال</th><th>وضعیت</th><th>درخواست</th><th>تأیید</th><th></th></tr></thead>
      <tbody>
        <tr v-if="!rows.length"><td :colspan="showProgram ? 11 : 10" class="text-center inv-muted">انتقالی ثبت نشده است.</td></tr>
        <tr v-for="t in rows" :key="t.id">
          <td>{{ t.id }}</td><td>{{ transferDirections[t.direction] }}</td><td>{{ t.machineTitle }}<div v-if="t.operationName" class="inv-muted small">{{ t.operationName }}</div></td>
          <td v-if="showProgram" class="inv-ltr">{{ t.programCode }}</td><td>{{ t.revision ?? "-" }}</td><td class="inv-ltr">{{ t.targetName }}</td><td class="small">{{ linkKinds[t.linkKind] }}</td>
          <td><span :class="badgeOfTransfer(t.status)" class="inv-badge">{{ transferStatuses[t.status] }}</span><div v-if="t.lastError" class="text-danger small">{{ t.lastError }}</div><div v-if="t.cancelReason" class="inv-muted small">{{ t.cancelReason }}</div></td>
          <td class="small">{{ t.requestedByName }}<div class="inv-muted">{{ t.requestedAtText }}</div></td>
          <td class="small">{{ t.confirmedByName || "-" }}<div v-if="t.confirmedAtText" class="inv-muted">{{ t.confirmedAtText }}</div></td>
          <td class="text-nowrap"><button v-for="a in t.allowedActions" :key="a" :class="a === 'confirm' ? 'btn-success' : a === 'cancel' ? 'btn-outline-danger' : 'btn-outline-primary'" :disabled="busy === t.id" class="btn btn-sm me-1" @click="a === 'download' ? download(t) : act(t, a)">{{ titles[a] }}</button></td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
