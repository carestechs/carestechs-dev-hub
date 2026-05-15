/**
 * Normalized error shape produced by the problem-details interceptor.
 * Mirrors RFC 7807 plus the `correlationId` and `errors` extensions DevHub.Api emits.
 */
export interface AppError {
  type: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}
