import { ref } from 'vue';

/** Page card " Layout Edit " mode (currently working on the desktop card page). */
export const layoutEdit = ref(false);

export function setLayoutEdit(v: boolean): void {
  layoutEdit.value = v;
}
