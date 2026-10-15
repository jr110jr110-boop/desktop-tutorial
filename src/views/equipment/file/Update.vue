<script lang="ts" setup>
import type AttachFile from "@/types/AttachFile";
import { onMounted, type Ref, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  changeDocumentFile,
  changeDocumentInfo,
  getDocumentInfo,
} from "@/api/DocumentAPIs";
import { finalizeFileEquipmentProcess } from "@/api/EquipmentAPIs";
import ShowErrorNotification from "@/composables/showErrorNotification";
import ShowSuccessNotification from "@/composables/showSuccessNotification";
import mime from "mime-types";

import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsFilePicker from "@/components/common/KsFilePicker.vue";

const props = defineProps({
  id: {
    type: String,
    required: true,
  },
});
const route = useRoute();
const router = useRouter();

const file: Ref<AttachFile> = ref({ ...route.query });
const loadings = ref({
  btnLoading: false,
  inputLoading: true,
});

const onFileChange = (files) => {
  if (files[0]) {
    file.value.file = files[0];
    file.value.size = files[0].size.toString();
    file.value.mimeType = "." + mime.extension(files[0].type);
  }
};

const sendRequest = async () => {
  loadings.value.btnLoading = true;

  await changeDocumentInfo(file.value);

  if (file.value.file) {
    const formData = new FormData();
    formData.append("file", file.value.file);

    const [data, error] = await changeDocumentFile(file.value.id, formData);
    if (data?.success) {
      await finalizeFileEquipmentProcess(
        route.query.processId.toString(),
        file.value.id
      );
    } else {
      ShowErrorNotification(data?.message || error.message);
      loadings.value.btnLoading = false;
      return;
    }
  }
  ShowSuccessNotification("سند با موفقیت ویرایش شد");
  router.back();
  loadings.value.btnLoading = false;
};

const getDetail = async (id) => {
  loadings.value.inputLoading = true;
  const [data, error] = await getDocumentInfo(id);
  if (data?.success) file.value = data.data;
  else ShowErrorNotification(data?.message || error.message);
  loadings.value.inputLoading = false;
};

onMounted(() => {
  getDetail(props.id);
});
</script>

<template>
  <KsUpdateForm
    :btn-loading="loadings.btnLoading"
    :has-transition-group="false"
    :is-edit="true"
    form-title="فایل محصول"
    @formSubmit="sendRequest">
    <template #form>
      <div class="col-12 col-md-10 mb-5">
        <div
          class="d-flex flex-column flex-md-row-reverse align-items-md-center gap-3 mt-3">
          <div class="d-flex flex-row-reverse align-items-center gap-3">
            <KsFilePicker
              v-if="!file.isFinal"
              :file-size="file.size"
              :has-file="route.query.isFinalized === 'true' || !!file.file"
              :height="42"
              class="flex-fill"
              icon-size="20"
              @fileChange="onFileChange" />
          </div>
          <div class="d-flex flex-1 flex-row-reverse align-items-center gap-3">
            <KsInput
              v-model="file.code"
              :loading="loadings.inputLoading"
              class="flex-fill"
              custom-height="42"
              placeholder="کد سند کنترل کیفی"
              simple-placeholder />
          </div>
          <div class="d-flex flex-1 flex-row-reverse align-items-center gap-3">
            <KsInput
              v-model="file.name"
              :loading="loadings.inputLoading"
              class="flex-fill"
              custom-height="42"
              placeholder="عنوان سند کنترل کیفی"
              simple-placeholder />
          </div>
        </div>
        <KsInput
          v-model="file.description"
          :loading="loadings.inputLoading"
          class="flex-fill mt-3"
          custom-height="42"
          placeholder="توضیحات سند کنترل کیفی"
          simple-placeholder />
      </div>
    </template>
  </KsUpdateForm>
</template>
