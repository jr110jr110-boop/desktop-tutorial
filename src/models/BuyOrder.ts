import type PreviewTextRow from "@/types/PreviewTextRow";

export const REVERT_STATUSES: { title: string; status: number }[] = [
  {
    title: "فرآیندهای تامین، مونتاژ و ساخت جدید",
    status: 0,
  },
  {
    title: "افزوده شدن استعلام ها",
    status: 1,
  },
  {
    title: "ثبت شدن امتیاز استعلام ها",
    status: 3,
  },
  {
    title: "انتخاب شدن برنده",
    status: 4,
  },
  {
    title: "ثبت شدن اسناد مالی",
    status: 5,
  },
  {
    title: "ثبت شدن فاکتور موقت",
    status: 6,
  },
  {
    title: "تایید واحد کنترل کیفیت",
    status: 7,
  },
  {
    title: "رد واحد کنترل کیفیت",
    status: 8,
  },
  {
    title: "تایید واحد کنترل کیفی",
    status: 9,
  },
];

export const PREVIEW_MODAL_BUY_ORDER_MODEL: PreviewTextRow[] = [
  {
    key: "enterCommodityCode",
    title: "کد ورود کالا",
  },
  {
    key: "exitCommodityCode",
    title: "کد خروج کالا",
  },
  {
    key: "count",
    title: "تعداد کل",
    prefix: "عدد",
  },
  {
    key: "buyCount",
    title: "تعداد خرید",
    prefix: "عدد",
  },
  {
    key: "realBuyCount",
    title: "تعداد خرید واقعی",
    prefix: "عدد",
  },
  {
    key: "reserveCount",
    title: "تعداد خرید رزرو",
    prefix: "عدد",
  },
  {
    key: "statusDescription",
    title: "وضعیت",
  },
  {
    key: "financePrice",
    title: "هزینه مالی",
    prefix: "تومان",
    format: "price",
  },
  {
    key: "financeType",
    title: "نوع مالی",
  },
  {
    key: "financeNumber",
    title: "شماره مالی",
  },
];

export const PREVIEW_MODAL_PROCESS_MODEL: PreviewTextRow[] = [
  {
    key: "title",
    title: "عنوان",
  },
  {
    key: "enterCommodityCode",
    title: "کد ورود کالا",
  },
  {
    key: "exitCommodityCode",
    title: "کد خروج کالا",
  },
  {
    key: "weightPercent",
    title: "درصد وزنی",
    format: "percent",
  },
  {
    key: "mapDuration",
    title: "پیش بینی زمان تولید مدارک",
    prefix: "روز",
  },
  {
    key: "masterDuration",
    title: "پیش بینی زمان تایید کارفرما",
    prefix: "روز",
  },
  {
    key: "processDuration",
    title: "پیش بینی زمان تامین تامین",
    prefix: "روز",
  },
  {
    key: "typeDescription",
    title: "نوع فرآیند تامین",
  },
  {
    key: "programPrice",
    title: "هزینه فرآیند تامین",
    prefix: "تومان",
    format: "price",
  },
];
