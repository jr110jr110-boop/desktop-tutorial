<script lang="ts" setup>
// Sensitive roles that wait for a second PMO user (GET Accounts/role-grants, POST Accounts/role-grants/{id}/approve|reject).
// The API gives a sensitive role (finance manager, payroll, CEO, auditor, HR, secretariat) only after another PMO user approves it:
// not the one who asked for it and not the user who receives it. PMO only: the page that shows this panel is for the PMO.
import { computed, onMounted, ref } from "vue";
import { approveRoleGrant, getRoleGrants, rejectRoleGrant } from "@/api/UserAPIs";
import { failureText } from "@/composables/apiMessage";
import { jalali } from "@/composables/inventory";
import { currentUserId, roleTitles, SensitiveRoles } from "@/composables/roles";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import KsButton from "@/components/common/KsButton.vue";
import KsModal from "@/components/common/KsModal.vue";

/** A grant was approved or rejected: the roles of a user may have changed. */
const emit = defineEmits(["changed"]);

const grants = ref<any[]>([]);
const loading = ref(false);
const busy = ref(false);
const confirm = ref<{ visible: boolean; grant: any | null; approve: boolean }>({ visible: false, grant: null, approve: true });

/** The sensitive roles by their Persian titles, for the explanation. */
const sensitiveText = SensitiveRoles.map((role) => roleTitles[role] || role).join("، ");

const titleOf = (role: string) => roleTitles[String(role || "").toUpperCase()] || role;

async function load() {
  loading.value = true;
  const [data, error] = await getRoleGrants();
  loading.value = false;
  if (error || !data?.success) {
    showErrorNotification(failureText(data, error, "فهرست درخواست‌های نقش خوانده نشد."));
    return;
  }
  grants.value = Array.isArray(data.data) ? data.data : [];
}

/** Why the signed-in PMO user cannot approve a grant (the API decides with CanApprove; this is the explanation). */
function whyNotApprovable(grant: any): string {
  const me = currentUserId();
  if (me !== null && grant.requestedByUserId === me) return "شما این درخواست را ثبت کرده‌اید؛ مدیر (PMO) دیگری باید آن را تأیید کند.";
  if (me !== null && grant.userId === me) return "این نقش برای خود شماست؛ مدیر (PMO) دیگری باید آن را تأیید کند.";
  return "تأیید این درخواست برای شما ممکن نیست.";
}

function ask(grant: any, approve: boolean) {
  confirm.value = { visible: true, grant, approve };
}

const confirmText = computed(() => {
  const grant = confirm.value.grant;
  if (!grant) return "";
  return confirm.value.approve
    ? `نقش «${titleOf(grant.role)}» به «${grant.displayName}» داده شود؟`
    : `درخواست نقش «${titleOf(grant.role)}» برای «${grant.displayName}» رد شود؟`;
});

async function decide() {
  const { grant, approve } = confirm.value;
  if (!grant || busy.value) return;
  busy.value = true;
  try {
    const [data, error] = approve ? await approveRoleGrant(grant.id) : await rejectRoleGrant(grant.id);
    confirm.value.visible = false;
    if (error || !data?.success) {
      showErrorNotification(failureText(data, error, approve ? "تأیید نقش انجام نشد." : "رد درخواست انجام نشد."));
    } else {
      showSuccessNotification(data.message || (approve ? "نقش تأیید و به کاربر داده شد." : "درخواست نقش رد شد."));
      emit("changed");
    }
    // the request may have been decided or may have ended meanwhile, so the list is always read again
    await load();
  } finally {
    busy.value = false;
  }
}

defineExpose({ reload: load });

onMounted(load);
</script>

<template>
  <div class="role-grants background-color p-3 shadow-sm rounded-3 text-end mb-3" dir="rtl">
    <div class="d-flex justify-content-between align-items-center gap-2 flex-wrap">
      <span class="fw-bold text-large text-color">نقش‌های حساس در انتظار تأیید</span>
      <KsButton
        :loading="loading"
        color="dark"
        size="mini"
        theme="flat"
        title="بارگذاری دوباره"
        @click="load" />
    </div>

    <p class="text-small mt-2 mb-2">
      نقش‌های حساس ({{ sensitiveText }}) بلافاصله به کاربر داده نمی‌شوند. وقتی یکی از آن‌ها برای کاربری ثبت یا ویرایش می‌شود،
      تا زمانی که یک مدیر (PMO) دیگر آن را تأیید نکند اعمال نمی‌شود. کسی که درخواست را ثبت کرده و کاربری که نقش به او
      داده می‌شود نمی‌توانند خودشان آن را تأیید کنند. درخواستی که تا ۷ روز تأیید نشود باطل می‌شود.
    </p>

    <div v-if="grants.length" class="table-responsive">
      <table class="table table-sm align-middle mb-0">
        <thead>
          <tr>
            <th>کاربر</th>
            <th>نقش درخواست‌شده</th>
            <th>درخواست‌دهنده</th>
            <th>تاریخ درخواست</th>
            <th>مهلت تأیید</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="grant in grants" :key="grant.id">
            <td>
              {{ grant.displayName }}
              <span class="text-xSmall d-block" dir="ltr">{{ grant.userName }}</span>
            </td>
            <td class="fw-bold">{{ titleOf(grant.role) }}</td>
            <td>{{ grant.requestedByName }}</td>
            <td>{{ jalali(grant.requestedAt, true) }}</td>
            <td>{{ jalali(grant.expiresAt, true) }}</td>
            <td style="min-width: 210px">
              <div class="d-flex gap-2">
                <KsButton
                  :disabled="!grant.canApprove || busy"
                  color="success"
                  size="mini"
                  theme="flat"
                  title="تأیید"
                  @click="ask(grant, true)" />
                <KsButton
                  :disabled="busy"
                  color="danger"
                  size="mini"
                  theme="flat"
                  title="رد"
                  @click="ask(grant, false)" />
              </div>
              <span v-if="!grant.canApprove" class="text-xSmall d-block mt-1">
                {{ whyNotApprovable(grant) }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-else class="text-small mb-0">
      {{ loading ? "در حال بارگذاری…" : "درخواست نقش حساسی در انتظار تأیید نیست." }}
    </p>

    <KsModal v-model="confirm.visible" blur>
      <div class="d-flex flex-column gap-3 p-2" style="max-width: 440px">
        <span class="fw-bold text-medium">
          {{ confirm.approve ? "تأیید نقش حساس" : "رد درخواست نقش" }}
        </span>
        <span class="text-small">{{ confirmText }}</span>
        <div class="d-flex gap-2 justify-content-end">
          <KsButton
            :disabled="busy"
            color="dark"
            theme="flat"
            title="انصراف"
            @click="confirm.visible = false" />
          <KsButton
            :color="confirm.approve ? 'success' : 'danger'"
            :loading="busy"
            theme="flat"
            :title="confirm.approve ? 'تأیید' : 'رد درخواست'"
            @click="decide" />
        </div>
      </div>
    </KsModal>
  </div>
</template>
