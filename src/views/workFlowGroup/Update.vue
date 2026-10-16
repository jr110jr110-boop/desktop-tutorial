<script setup lang="ts">
import { onMounted, ref } from "vue";
import type { Ref, PropType } from "vue";
import { useRouter } from "vue-router";
import type WorkflowGroup from "@/types/WorkFlowGroup";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsInput from "@/components/common/KsInput.vue";
import {
  addWorkflowGroup,
  editWorkflowGroup,
  getWorkflowGroupById,
} from "@/api/WorkFlowGroupAPIs";
import CogIcon from "@/components/icons/CogIcon.vue";
import showErrorNotification from "@/composables/showErrorNotification";

const submitButtonLoading: Ref<boolean> = ref(false);
const router = useRouter();

const props = defineProps({
  id: {
    type: [Number, String] as PropType<number | string | null>,
    default: null,
  },
});

const workflowGroupInfo: Ref<WorkflowGroup> = ref({
  id: 0,
  title: "",
});

const isDisabled: Ref<boolean> = ref(false);

isDisabled.value = !!props.id;

const validateForm = () => {
  let isValid = true;

  if (workflowGroupInfo.value.title === "") isValid = false;

  if (isValid) return true;
  else {
    showErrorNotification("تمامی فیلدها الزامی هستند");
    return false;
  }
};

async function fetchDetail() {
  submitButtonLoading.value = true;
  const [data, error] = await getWorkflowGroupById(props.id);
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      workflowGroupInfo.value = data.data;
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

async function sendRequestWorkflowGroup() {
  if (!validateForm()) return;
  submitButtonLoading.value = true;
  const [data, error] = props.id
    ? await editWorkflowGroup(workflowGroupInfo.value)
    : await addWorkflowGroup(workflowGroupInfo.value);
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      await router.push({ name: "WorkflowGroupList" });
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }

  submitButtonLoading.value = false;
}

onMounted(async () => {
  if (props.id) {
    await fetchDetail();
  }
});
</script>

<template>
  <KsUpdateForm
    form-title="دستگردان مدارک"
    :is-edit="!!props.id"
    :loading="submitButtonLoading"
    @formSubmit="sendRequestWorkflowGroup"
    @cancel-btn="$router.back()"
    :hasTransitionGroup="false">
    <template #formIcon>
      <CogIcon class="ms-2" />
    </template>
    <template #form>
      <div class="col-12 col-md-10 mb-4" key="1">
        <div
          class="d-flex flex-column flex-row-reverse align-items-center gap-3 gap-sm-3">
          <KsInput
            placeholder="گردش کار"
            class="flex-2 mt-4"
            :loading="submitButtonLoading"
            v-model="workflowGroupInfo.title" />
        </div>
      </div>
    </template>
  </KsUpdateForm>
</template>
