export default interface ProviderContact {
  id?: string | number;
  name: string;
  data: string;
  type: "0" | "1" | "2" | "3" | "4" | "5" | "6";
  typeDescription?: string;
  description: string;
  providerId?: string | number;
}
