<script lang="ts" setup>
import type Equipment from "@/types/Equipment";
import type { Ref } from "vue";
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import {
  addEquipment,
  editEquipment,
  getEquipmentDetail,
} from "@/api/EquipmentAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsInput from "@/components/common/KsInput.vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsTextArea from "@/components/common/KsTextArea.vue";
import ShoppingBoxIcon from "@/components/icons/ShoppingBoxIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();

const equipment: Ref<Equipment> = ref({
  id: props.id,
  code: "",
  title: "",
  description: "",
});

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
});

async function sendRequest() {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editEquipment(equipment.value)
    : await addEquipment(equipment.value);

  if (!data?.success)
    showErrorNotification(data?.message || error.message);
  else {
    showSuccessNotification(
      "محصولات با موفقیت" + (props.id ? "ویرایش شد" : "افزوده شد")
    );
    router.back();
  }

  loadings.value.btnLoading = false;
}

const validateForm = () => {
  let isValid = true;

  if (
    equipment.value.title === "" ||
    equipment.value.title === null ||
    equipment.value.code === "" ||
    equipment.value.code === null
  )
    isValid = false;

  if (isValid) return true;
  else {
    showWarnNotification("فیلدهای الزامی را وارد کنید");
    return false;
  }
};

async function fetchDetail(id: number | string) {
  loadings.value.inputLoading = true;

  const [data, error] = await getEquipmentDetail(id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else equipment.value = data.data;

  loadings.value.inputLoading = false;
}

onMounted(() => {
  if (props.id) fetchDetail(props.id);
});
</script>

<template>
  <KsUpdateForm
    :btn-loading="loadings.btnLoading"
    :is-edit="!!props.id"
    form-title="محصول"
    @formSubmit="sendRequest">
    <template #formIcon>
      <ShoppingBoxIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
    <template #form>
      <div key="1" class="col-12 col-md-10 mb-5">
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3">
          <KsInput
            v-model="equipment.title"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="عنوان" />
          <KsInput
            v-model="equipment.code"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="کد" />
        </div>
      </div>
      <div key="3" class="col-12 col-md-10 mb-4">
        <KsTextArea
          v-model="equipment.description"
          :loading="loadings.inputLoading"
          placeholder="توضیحات (اختیاری)" />
      </div>
    </template>
  </KsUpdateForm>
</template>
