import { getProductionOperations } from "@/api/ProductionAPIs";
import { rowsOf } from "@/api/ApiClient";
import { camelize } from "@/composables/fn";

/** /production/operations/:id (the QC-request link): the operation is read by OperationQuery.Id and its order id is returned (null when not found). */
export async function orderIdOfOperation(operationId: number): Promise<number | null> {
  const [data] = await getProductionOperations({ Id: operationId, PageSize: 1 });
  const row = rowsOf(camelize(data)).find((o: any) => o.id === operationId);
  return row ? row.orderId : null;
}
