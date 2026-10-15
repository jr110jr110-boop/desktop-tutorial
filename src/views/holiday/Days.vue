<script lang="ts" setup>
import { onMounted, ref } from "vue";
import {
  addHolidayDay,
  deleteHolidayDay,
  getHolidayDays,
} from "@/api/HolidayAPIs";
import showErrorNotification from "@/composables/showErrorNotification";

import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import UmbrellaBeachIcon from "@/components/icons/UmbrellaBeachIcon.vue";
import KsSwitch from "@/components/common/KsSwitch.vue";
import KsAlert from "@/components/common/KsAlert.vue";
import { Colors } from "@/assets/colors";

const loadings = ref({
  btnLoading: false,
  listLoading: true,
});

const ksAlert = ref(true);

const days = ref([
  {
    id: null,
    title: "شنبه",
    dayOfWeek: 6,
    active: true,
  },
  {
    id: null,
    title: "یکشنبه",
    dayOfWeek: 0,
    active: true,
  },
  {
    id: null,
    title: "دوشنبه",
    dayOfWeek: 1,
    active: true,
  },
  {
    id: null,
    title: "سه شنبه",
    dayOfWeek: 2,
    active: true,
  },
  {
    id: null,
    title: "چهارشنبه",
    dayOfWeek: 3,
    active: true,
  },
  {
    id: null,
    title: "پنجشنبه",
    dayOfWeek: 4,
    active: true,
  },
  {
    id: null,
    title: "جمعه",
    dayOfWeek: 5,
    active: true,
  },
]);

async function sendRequest() {
  loadings.value.btnLoading = true;
  for (let i = 0; i < days.value.length; i++) {
    if (!days.value[i].active && !days.value[i].id) {
      const [data, error] = await addHolidayDay(days.value[i].dayOfWeek);
      const isSuccess = !error && data?.success;
      if (!isSuccess) showErrorNotification(data?.message || error.message);
    } else if (days.value[i].active && days.value[i].id) {
      const [data, error] = await deleteHolidayDay(days.value[i].dayOfWeek);
      const isSuccess = !error && data?.success;
      if (!isSuccess) showErrorNotification(data?.message || error.message);
    }
  }
  loadings.value.btnLoading = false;
}

async function fetchHolidayDays() {
  loadings.value.listLoading = true;
  const [data, error] = await getHolidayDays();
  const isSuccess = !error && data?.success;
  if (isSuccess) {
    for (let i = 0; i < data.data.length; i++)
      for (let j = 0; j < days.value.length; j++)
        if (days.value[j].dayOfWeek === data.data[i].dayOfWeek) {
          days.value[j].active = false;
          days.value[j].id = data.data[i].id;
          break;
        }
  } else showErrorNotification(data?.message || error.message);
  loadings.value.listLoading = false;
}

onMounted(() => {
  fetchHolidayDays();
});
</script>

<template>
  <KsUpdateForm
    :btn-loading="loadings.btnLoading || loadings.listLoading"
    :has-transition-group="false"
    :is-edit="true"
    form-title="روزهای کاری"
    @formSubmit="sendRequest">
    <template #formIcon>
      <UmbrellaBeachIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
    <template #form>
      <transition-group
        class="col-12 col-md-10 mb-5"
        mode="out-in"
        name="fade"
        tag="div">
        <div
          v-if="!loadings.listLoading"
          class="d-flex flex-column gap-4"
          dir="rtl">
          <KsAlert
            v-model="ksAlert"
            :closeable="false"
            class="mb-4"
            color="primary"
            theme="flat">
            <template #title>
              <span class="text-small color-primary">
                لطفا روزهای کاری و روزهای تعطیل موردنظر خود را مشخص کنید.
              </span>
            </template>
          </KsAlert>
          <div v-for="(day, index) in days" :key="index">
            <div class="d-flex gap-2 align-items-center">
              <span class="text-medium" style="width: 120px">
                {{ day.title }}
              </span>
              <KsSwitch v-model="day.active" color="success">
                <template #on> روز کاری</template>
                <template #off> تعطیل</template>
              </KsSwitch>
            </div>
          </div>
        </div>
        <div v-if="loadings.listLoading" class="position-relative">
          <div class="custom-loader center" />
        </div>
      </transition-group>
    </template>
  </KsUpdateForm>
</template>
