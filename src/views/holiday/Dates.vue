<script lang="ts" setup>
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import {
  addHolidayDate,
  deleteHolidayDate,
  getHolidayDates
} from "@/api/HolidayAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";

import DeleteModal from "@/components/app/DeleteModal.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsAlert from "@/components/common/KsAlert.vue";
import KsButton from "@/components/common/KsButton.vue";
import KsDatePicker from "@/components/common/KsDatePicker.vue";
import UmbrellaBeachIcon from "@/components/icons/UmbrellaBeachIcon.vue";
import TimesIcon from "@/components/icons/TimesIcon.vue";
import PlusIcon from "@/components/icons/PlusIcon.vue";
import { Colors } from "@/assets/colors";

type HolidayDate = {
  id: string | number;
  holidayDate: string;
};

const loadings = ref({
  btnLoading: false,
  listLoading: true,
  deleteBtnLoading: null
});

const ksAlert = ref(true);

const deleteModal = ref({
  visible: false,
  activeId: null,
  activeIndex: null
});

const dates: Ref<HolidayDate[]> = ref([]);

const openDeleteModal = (index, id) => {
  deleteModal.value.activeIndex = index;
  deleteModal.value.activeId = id;
  if (id) deleteModal.value.visible = true;
  else removeDateForm();
};

const addDateForm = () => {
  dates.value.push({
    id: null,
    holidayDate: ""
  });
};

const removeDateForm = async () => {
  if (deleteModal.value.activeId) {
    loadings.value.deleteBtnLoading = deleteModal.value.activeIndex;
    const [data, error] = await deleteHolidayDate(deleteModal.value.activeId);

    if (!data?.success)
      showErrorNotification(
        data?.message || error.message
      );
    else {
      dates.value.splice(deleteModal.value.activeIndex, 1);
      showSuccessNotification("تاریخ روز تعطیل با موفقیت حذف شد");
    }
    loadings.value.deleteBtnLoading = null;
  } else dates.value.splice(deleteModal.value.activeIndex, 1);
  deleteModal.value.visible = false;
};

async function sendRequest() {
  let isSuccess = true;
  loadings.value.btnLoading = true;
  for (let i = 0; i < dates.value.length; i++)
    if (!dates.value[i].id) {
      const [data, error] = await addHolidayDate(dates.value[i].holidayDate);
      if (error || !data.success) {
        isSuccess = false;
        showErrorNotification((data && data.message) || error.message);
      } else dates.value[i].id = data.data.id;
    }
  if (isSuccess)
    showSuccessNotification("تاریخ روزهای تعطیل با موفقیت بروزرسانی شدند");
  loadings.value.btnLoading = false;
}

async function fetchDetail() {
  loadings.value.listLoading = true;
  const [data, error] = await getHolidayDates();
  if (error || !data.success)
    showErrorNotification((data && data.message) || error.message);
  else dates.value = data.data;
  loadings.value.listLoading = false;
}

onMounted(() => {
  fetchDetail();
});
</script>

<template>
  <div>
    <KsUpdateForm
      :btn-loading="loadings.btnLoading"
      :has-transition-group="false"
      :is-edit="true"
      form-title="تاریخ روزهای کاری"
      @formSubmit="sendRequest">
      <template #formIcon>
        <UmbrellaBeachIcon :fill="Colors.black" class="ms-3" width="24" />
      </template>
      <template #form>
        <div class="col-12 col-md-10 mb-5">
          <KsAlert
            v-if="!loadings.listLoading && !dates.length"
            v-model="ksAlert"
            :closeable="false"
            class="mb-4"
            color="primary"
            theme="flat">
            <template #title>
              <span class="text-small color-primary">
                لیست تاریخ روزهای تعطیل خالی می باشد.
              </span>
            </template>
          </KsAlert>
          <transition-group
            v-if="!loadings.listLoading"
            class="d-flex flex-column gap-3"
            mode="out-in"
            name="fade"
            tag="div">
            <div
              v-for="(date, index) in dates"
              :key="index"
              class="d-flex flex-row-reverse align-items-center gap-3">
              <KsButton
                :loading="loadings.deleteBtnLoading === index"
                color="warn"
                fixed-width-height="42"
                theme="flat"
                tooltip="حذف"
                @click="openDeleteModal(index, date.id)">
                <template #content>
                  <TimesIcon :fill="Colors.warn" width="18" />
                </template>
              </KsButton>
              <KsDatePicker
                v-model="date.holidayDate"
                :element-tag="'holidayDate_' + index"
                class="flex-1"
                placeholder="تاریخ تعطیل را انتخاب کنید"
                simple-placeholder />
            </div>

            <KsButton
              key="add-btn"
              class="mt-3"
              color="primary"
              fixed-width-height="52"
              tooltip="افزودن تاریخ جدید"
              @click="addDateForm">
              <template #content>
                <PlusIcon :fill="Colors.white" width="18" />
              </template>
            </KsButton>
          </transition-group>

          <div v-if="loadings.listLoading" class="position-relative p-5">
            <div class="custom-loader center" />
          </div>
        </div>
      </template>
    </KsUpdateForm>

    <!--    DELETE MODAL   -->
    <DeleteModal
      v-model="deleteModal.visible"
      :button-loading="loadings.deleteBtnLoading !== null"
      @cancel="deleteModal.visible = false"
      @submit="removeDateForm" />
  </div>
</template>
