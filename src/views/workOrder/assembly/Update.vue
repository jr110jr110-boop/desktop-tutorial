<script lang="ts" setup>
import type Assembly from "@/types/Assembly";
import type { Ref } from "vue";
import { computed, onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import {
  addAssembly,
  editAssembly,
  getAssemblyDetail,
  setAssemblyPredecessor,
} from "@/api/AssemblyAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import showSuccessNotification from "@/composables/showSuccessNotification";
import showWarnNotification from "@/composables/showWarnNotification";

import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsInput from "@/components/common/KsInput.vue";
import CpuIcon from "@/components/icons/CpuIcon.vue";
import { Colors } from "@/assets/colors";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();
const route = useRoute();

const assembly: Ref<Assembly> = ref({
  id: null,
  code: "",
  title: "",
  assemblerDuration: null,
  workOrderId: route.query.workOrderId.toString(),
});

const parentId = computed(() => route.query?.parentId?.toString() ?? null);
const parentTitle = computed(
  () => route.query?.parentTitle?.toString() ?? null
);

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
});

const setAssemblyParent = async (id) => {
  const [data, error] = await setAssemblyPredecessor(id, parentId.value);
  if (!data?.success) showErrorNotification(data?.message || error.message);
};

const sendRequest = async () => {
  if (!validateForm()) return;
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editAssembly(assembly.value)
    : await addAssembly(assembly.value);

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else {
    if (parentId.value) await setAssemblyParent(data.data.id);
    showSuccessNotification(
      "اسمبلی با موفقیت" + (props.id ? "ویرایش شد" : "افزوده شد")
    );
    router.back();
  }
  loadings.value.btnLoading = false;
};

const validateForm = () => {
  let isValid = true;

  if (
    assembly.value.title === "" ||
    assembly.value.title === null ||
    assembly.value.code === "" ||
    assembly.value.code === null
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
  const [data, error] = await getAssemblyDetail(
    id,
    route.query.workOrderId.toString()
  );
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else assembly.value = data.data;
  loadings.value.inputLoading = false;
}

onMounted(() => {
  if (props.id) fetchDetail(props.id);
});
</script>

<template>
  <KsUpdateForm
    :btn-loading="loadings.btnLoading"
    :form-title="`زیر مجموعه ${parentId ? ' در ' + parentTitle : ''}`"
    :is-edit="!!props.id"
    @formSubmit="sendRequest">
    <template #formIcon>
      <CpuIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
    <template #form>
      <div key="1" class="col-12 col-md-10 mb-5">
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3">
          <KsInput
            v-model="assembly.title"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="عنوان" />
        </div>
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3 mt-5">
          <KsInput
            v-model="assembly.code"
            :loading="loadings.inputLoading"
            class="flex-fill"
            placeholder="کد" />
          <KsInput
            v-model="assembly.assemblerDuration"
            :loading="loadings.inputLoading"
            append-text="روز"
            class="flex-fill"
            placeholder="زمان اجرا" />
        </div>
      </div>
    </template>
  </KsUpdateForm>
</template>

<style scoped></style>