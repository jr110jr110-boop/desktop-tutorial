<template>
  <li :class="currentRouteName === routeName && 'active'">
    <div v-if="!hasSub" class="d-grid position-relative">
      <button
        :class="[{ 'active-link': isRouteActive }]"
        :disabled="isLocked"
        class="btn sidebar-link align-items-center text-end position-relative"
        @click="$router.push({ name: routeName })">
        <span
          :class="{
            'color-secondary fw-bold': isRouteActive,
            'text-to-right': sidebarState.isSidebarExpanded,
            'text-to-left': !sidebarState.isSidebarExpanded,
          }"
          class="IRANYekan text-small link-text"
          style="word-wrap: normal">
          {{ title }}
        </span>
        <slot />
      </button>
      <div v-if="isLocked" class="lock-icon">
        <LockIcon :fill="Colors.danger" width="12" />
      </div>
    </div>

    <div v-if="hasSub" class="d-grid">
      <button
        ref="collapseBtn"
        :aria-expanded="isRouteActive ? 'true' : 'false'"
        :class="[
          isRouteActive && 'active-link',
          sidebarState.isSidebarExpanded
            ? 'justify-content-between'
            : 'justify-content-end',
        ]"
        :data-bs-target="'#' + tag"
        class="btn btn-toggle align-items-center collapsed d-flex sidebar-link position-relative"
        data-bs-toggle="collapse">
        <AngleLeftIcon
          v-show="sidebarState.isSidebarExpanded"
          :fill="Colors.primaryLight"
          class="link-icon mt-2"
          width="5" />
        <span>
          <span
            :class="
              sidebarState.isSidebarExpanded ? 'text-to-right' : 'text-to-left'
            "
            class="IRANYekan text-small link-text">
            {{ title }}
          </span>
          <slot />
        </span>
      </button>
      <div :id="tag" :class="isRouteActive && 'show'" class="collapse">
        <ul class="btn-toggle-nav list-unstyled fw-normal pb-1 small">
          <li
            v-for="(sub, index) in subs.filter((x) => !x.hidden)"
            :key="index"
            :class="[
              { active: currentRouteName === sub.routeName },
              {
                'opacity-50 no-pointer-event': sub.disabled,
              },
            ]"
            class="d-grid sidebar-li-sub">
            <button
              class="btn text-end IRANYekan text-xSmall sidebar-link-sub px-3 position-relative"
              @click="$router.push({ name: sub.routeName })">
              <span
                :class="{
                  'color-secondary fw-bold': currentRouteName === sub.routeName,
                  'text-to-right': sidebarState.isSidebarExpanded,
                  'text-to-left': !sidebarState.isSidebarExpanded,
                }"
                class="IRANYekan text-small link-text">
                {{ sub.title }}
              </span>
              <circle-icon
                :fill="
                  currentRouteName === sub.routeName
                    ? Colors.primary
                    : Colors.primaryLight
                "
                class="ms-3"
                width="7" />
            </button>
          </li>
        </ul>
      </div>
    </div>
  </li>
</template>

<script lang="ts" setup>
import type { PropType } from "vue";
import type Links from "./type";
import { computed, onMounted, ref } from "vue";
import { useRoute } from "vue-router";

import { Colors } from "@/assets/colors";
import AngleLeftIcon from "@/components/icons/AngleLeftIcon.vue";
import CircleIcon from "@/components/icons/CircleIcon.vue";
import LockIcon from "@/components/icons/LockIcon.vue";

const props = defineProps({
  title: String,
  routeName: String,
  hasSub: {
    type: Boolean,
    default: false,
  },
  isLocked: {
    type: Boolean,
    default: false,
  },
  subs: Array as PropType<Links[]>,
  tag: String,
  sidebarState: Object,
});

const isRouteActive = ref<boolean>(false);

const route = useRoute();

const currentRouteName = computed(() => {
  return route.name;
});

onMounted(() => {
  if (props.subs) {
    for (let i = 0; i < props.subs.length; i++) {
      if (props.subs[i].routeName === currentRouteName.value) {
        isRouteActive.value = true;
        break;
      }
    }
  }
});
</script>

<style lang="scss" scoped>
[data-theme="darkMode"] {
  .sidebar-li-sub {
    background: #00000050 !important;
  }

  li {
    &.active {
      span {
        color: rgb(var(--text-color));
      }
    }
  }
}

.lock-icon {
  position: absolute;
  top: 50%;
  transform: translateY(-50%);
  margin-top: 3px;
  left: 16px;
  width: 24px;
  height: 24px;
  background: rgba(var(--danger), 0.1);
  border-radius: 999px;
  display: flex;
  place-content: center;
}

li {
  .sidebar-link,
  .sidebar-link-sub {
    outline: none !important;
    box-shadow: none !important;
    transition: all 200ms ease;

    span {
      color: rgb(var(--sidebar-link-color));
      font-size: 13px;
    }

    &:hover {
      margin-right: 4px;
    }
  }

  .sidebar-link {
    padding: 12px 16px !important;
  }

  .sidebar-link-sub {
    padding: 12px 18px !important;
  }

  .sidebar-li-sub {
    background: #00000005;
  }

  &.active {
    position: relative;

    span {
      color: rgb(var(--primary));
    }

    &::after {
      content: "";
      position: absolute;
      top: 0;
      left: 0;
      height: 100%;
      width: 4px;
      border-top-right-radius: 32px;
      border-bottom-right-radius: 32px;
      background: rgb(var(--primary));
    }
  }

  .btn {
    &[aria-expanded="true"] {
      .link-icon {
        transform: rotate(-90deg);
      }
    }

    .link-icon {
      transition: all 200ms ease;
    }

    .link-text {
      position: absolute;
      top: 16px;
    }

    .text-to-left {
      right: 150px;
      opacity: 0;
      transition: right ease 200ms, opacity ease 100ms;
    }

    .text-to-right {
      right: 52px;
      opacity: 1;
      transition: right ease 200ms, opacity ease 500ms;
    }
  }
}
</style>
