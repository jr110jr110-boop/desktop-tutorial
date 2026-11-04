// A node of the referral tree (recursive).
import { defineComponent, h, type PropType } from "vue";
const ReferralNode: any = defineComponent({
  name: "ReferralNode",
  props: { node: { type: Object as PropType<any>, required: true } },
  setup(p: any) {
    const states: Record<number, string> = { 0: "فعال", 1: "انجام شد", 2: "رد شد", 3: "منتقل شد", 4: "پس گرفته شد" };
    const render = (n: any): any =>
      h("li", { class: "mb-1" }, [
        h("span", { class: "fw-bold" }, n.who), h("span", { class: "inv-muted" }, ` ← ${n.byWhom || ""} (${n.assignedAtText || ""}) `), h("span", { class: "inv-badge" }, states[n.state] || ""),
        n.reason ? h("div", { class: "small inv-muted" }, "علت: " + n.reason) : null,
        n.endReason ? h("div", { class: "small inv-muted" }, "پایان: " + n.endReason + " " + (n.endedAtText || "")) : null,
        n.children?.length ? h("ul", { class: "list-unstyled ms-3 border-start ps-2" }, n.children.map(render)) : null,
      ]);
    return () => h("ul", { class: "list-unstyled" }, [render(p.node)]);
  },
});

export default ReferralNode;
