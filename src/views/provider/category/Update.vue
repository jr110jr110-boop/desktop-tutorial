<script setup lang="ts">
import type { ProviderCategory } from "@/types/ProviderCategory";
import { onMounted, ref } from "vue";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import CopyIcon from "@/components/icons/CopyIcon.vue";
import { Colors } from "@/assets/colors";
import KsInput from "@/components/common/KsInput.vue";
import {
  addProviderCategory,
  editProviderCategory,
  getProviderCategory,
} from "@/api/ProviderAPIs";
import showErrorNotification from "@/composables/showErrorNotification";
import { useRouter } from "vue-router";

const props = defineProps({
  id: {
    type: [Number, String],
    default: null,
  },
});

const router = useRouter();

const providerCategory = ref<ProviderCategory>({
  id: Number(props.id),
  category: "",
});

const loadings = ref({
  btnLoading: false,
  inputLoading: false,
});

const sendRequest = async () => {
  loadings.value.btnLoading = true;
  const [data, error] = props.id
    ? await editProviderCategory(providerCategory.value)
    : await addProviderCategory({
        categoryName: providerCategory.value.category,
      });

  if (!data?.success) showErrorNotification(data?.message || error.message);
  else router.back();
  loadings.value.btnLoading = false;
};

const getDetail = async () => {
  loadings.value.inputLoading = true;
  const [data, error] = await getProviderCategory(props.id);
  if (!data?.success) {
    showErrorNotification(data?.message || error.message);
    router.back();
  } else {
    providerCategory.value = {
      id: data.data.id,
      category: data.data.category,
    };
  }
  loadings.value.inputLoading = false;
};

onMounted(() => {
  if (props.id) getDetail();
});
</script>

<template>
  <KsUpdateForm
    :is-edit="!!props.id"
    :btn-loading="loadings.btnLoading"
    :has-transition-group="false"
    form-title="تامین کننده"
    @formSubmit="sendRequest">
    <template #formIcon>
      <CopyIcon :fill="Colors.black" class="ms-3" width="24" />
    </template>
    <template #form>
      <div class="col-12 col-md-10 mb-5">
        <div
          class="d-flex flex-column flex-sm-row-reverse align-sm-items-center gap-5 gap-sm-3">
          <KsInput
            v-model="providerCategory.category"
            :loading="loadings.inputLoading"
            class="w-100"
            placeholder="نام" />
        </div>
      </div>
    </template>
  </KsUpdateForm>
</template>
