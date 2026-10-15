export default interface ContractProvisionalStatement {
  id?: number;
  contractId?: number | string;
  number: number;
  date: string;
  price: number;
  acceptedPrice: number;
  type: string;
  status: string;
}
