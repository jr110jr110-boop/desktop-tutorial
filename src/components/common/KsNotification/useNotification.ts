import type Notification from "@/components/common/KsNotification/type";
import { reactive } from "vue";

const notifications: Notification[] = reactive([]);

const methods = {
  addNotification(n: Notification): void {
    const newNotification: Notification = {
      id: notifications.length + 1,
      position: n.position ? n.position : "left-top",
      color: n.color ? n.color : "secondary",
      title: n.title ? n.title : "",
      text: n.text ? n.text : "",
      content: n.content ? n.content : "",
      duration: n.duration ? n.duration : null,
      icon: n.icon ? n.icon : "",
      closeable: n.closeable ? n.closeable : true,
      ltr: n.ltr ? n.ltr : false,
      closeOnClick: n.closeOnClick ? n.closeOnClick : false,
    };
    notifications.push(newNotification);
  },

  removeNotification(id: number): void {
    for (let i = 0; i < notifications.length; i++)
      if (notifications[i].id === id) notifications.splice(i, 1);
  },
};

export default {
  notifications,
  ...methods,
};
