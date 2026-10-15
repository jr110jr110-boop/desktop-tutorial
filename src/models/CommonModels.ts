export const ENTRIES_QNT_OPTIONS: { title: string; val: number }[] = [
  { title: "25", val: 25 },
  { title: "50", val: 50 },
  { title: "100", val: 100 },
  { title: "200", val: 200 },
  { title: "همه", val: 0 },
];

export const BASIC_CONFIG_MODEL = {
  searchQuery: "",
  tempEntriesQnt: 25,
  entriesQnt: 25,
  activePage: 1,
  isLoading: true,
  totalPage: 0,
};
