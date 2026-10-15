import type ProviderContact from "@/types/ProviderContact";

export default interface Provider {
  id?: string | number;
  name: string;
  description: string;
  providerContacts?: ProviderContact[];
  categoryName?: string;
  categoryId?: number;
  listName?: string;
  evaluationScore?: number;
}
