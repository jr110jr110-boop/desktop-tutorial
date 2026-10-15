<script setup lang="ts">
import { onMounted, ref } from "vue";
import type { Ref, PropType } from "vue";
import { useRouter } from "vue-router";
import type Machine from "@/types/Machine";
import KsUpdateForm from "@/components/common/KsUpdateForm.vue";
import KsInput from "@/components/common/KsInput.vue";
import KsSwitch from "@/components/common/KsSwitch.vue";
import { addMachine, editMachine, getMachineById } from "@/api/MachineAPIs";
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

const machineInfo: Ref<Machine> = ref({
  id: 0,
  title: "",
  isActive: true,
});

const isDisabled: Ref<boolean> = ref(false);

isDisabled.value = !!props.id;

const validateForm = () => {
  let isValid = true;

  if (machineInfo.value.title === "") isValid = false;

  if (isValid) return true;
  else {
    showErrorNotification("تمامی فیلدها الزامی هستند");
    return false;
  }
};

async function fetchDetail() {
  submitButtonLoading.value = true;
  const [data, error] = await getMachineById(props.id);
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      machineInfo.value = data.data;
    } else {
      showErrorNotification(data?.message || error.message);
    }
  }
  submitButtonLoading.value = false;
}

async function sendRequestMachine() {
  if (!validateForm()) return;
  submitButtonLoading.value = true;
  const [data, error] = props.id
    ? await editMachine(machineInfo.value)
    : await addMachine(machineInfo.value);
  if (error) {
    if (error.message != "Request failed with status code 401") {
      showErrorNotification(data?.message || error.message);
    }
  } else {
    if (data.success) {
      await router.push({ name: "MachineList" });
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
    form-title="دستگاه"
    :is-edit="!!props.id"
    :loading="submitButtonLoading"
    @formSubmit="sendRequestMachine"
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
            placeholder="نام دستگاه"
            class="flex-2 mt-4"
            :loading="submitButtonLoading"
            v-model="machineInfo.title" />
          <KsSwitch v-model="machineInfo.isActive" class="mt-4" color="success">
            <template #on>فعال</template>
            <template #off>غیر فعال</template>
          </KsSwitch>
        </div>
      </div>
    </template>
  </KsUpdateForm>
</template>
