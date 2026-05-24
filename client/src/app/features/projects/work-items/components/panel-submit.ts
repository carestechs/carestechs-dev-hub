export interface PanelSubmit {
  outcome: string;
  payload: Record<string, unknown>;
  taskId?: string | null;
}
