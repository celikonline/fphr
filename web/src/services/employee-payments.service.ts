/**
 * Employee card "Ödemeler" tab (E3): mirrors `EmployeePaymentsController` (`api/v1/employees/{id}/salaries`, `/bank-accounts`).
 * Every write returns the updated list. Salary reads need employee.salary.view; bank account reads need employee.employees.read
 * (IBAN masked without employee.sensitive.view). The `/me` variants serve the caller's own card (read-only).
 */
import { apiClient } from "@senseik/api-client";

const EMPLOYEES_BASE = "/v1/employees";

/** Mirrors `SalaryType` (camelCase enum JSON). */
export type SalaryType = "net" | "gross";
/** Mirrors `PayFrequency`. */
export type PayFrequency = "monthly" | "daily" | "yearly" | "weekly" | "hourly";
/** Mirrors `SalarySource`; only "manual" is written today. */
export type SalarySource = "manual" | "import" | "compensation" | "wizard";
/** Mirrors `BankAccountType`. */
export type BankAccountType = "cheque" | "timeDeposit" | "demandDeposit" | "other";

/** `SalaryRecordDto`; the API returns the history newest first. Null fields may be omitted by the API. */
export interface SalaryRecord {
  id: string;
  effectiveFrom: string;
  amount: number;
  currency: string;
  salaryType: SalaryType;
  payFrequency: PayFrequency;
  transportAllowance?: number | null;
  mealAllowance?: number | null;
  source: SalarySource;
  note?: string | null;
  isCurrent: boolean;
  /** "Oluşturan"/"Oluşturulma Tarihi" columns; `createdByName` is null when the creator's account could not be resolved. */
  createdByUserId?: string | null;
  createdByName?: string | null;
  createdAt: string;
}

/** `SalaryRecordRequest` body: effectiveFrom + amount are mandatory ("Maaş bloğu bütünlüğü"). */
export interface SalaryRecordInput {
  effectiveFrom: string;
  amount: number;
  currency: string;
  salaryType: SalaryType;
  payFrequency: PayFrequency;
  transportAllowance: number | null;
  mealAllowance: number | null;
  note: string | null;
}

/** `BankAccountDto`; primary first. `iban` is grouped ("TR33 0006 …") or masked ("TR** **** … **12 34") when `isIbanMasked`. */
export interface BankAccount {
  id: string;
  iban: string;
  isIbanMasked: boolean;
  accountType: BankAccountType;
  bankName?: string | null;
  currency?: string | null;
  isPrimary: boolean;
  note?: string | null;
}

/** Add/edit body; on edit a null `iban` keeps the stored one. `isPrimary` is only honoured on add. */
export interface BankAccountInput {
  iban: string | null;
  accountType: BankAccountType;
  bankName: string | null;
  currency: string | null;
  note: string | null;
  isPrimary?: boolean;
}

export async function getEmployeeSalaries(employeeId: string): Promise<SalaryRecord[]> {
  const { data } = await apiClient.get<SalaryRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/salaries`);
  return data;
}

export async function getMySalaries(): Promise<SalaryRecord[]> {
  const { data } = await apiClient.get<SalaryRecord[]>(`${EMPLOYEES_BASE}/me/salaries`);
  return data;
}

export async function addSalaryRecord(employeeId: string, input: SalaryRecordInput): Promise<SalaryRecord[]> {
  const { data } = await apiClient.post<SalaryRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/salaries`, input);
  return data;
}

export async function updateSalaryRecord(employeeId: string, recordId: string, input: SalaryRecordInput): Promise<SalaryRecord[]> {
  const { data } = await apiClient.put<SalaryRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/salaries/${recordId}`, input);
  return data;
}

export async function deleteSalaryRecord(employeeId: string, recordId: string): Promise<SalaryRecord[]> {
  const { data } = await apiClient.delete<SalaryRecord[]>(`${EMPLOYEES_BASE}/${employeeId}/salaries/${recordId}`);
  return data;
}

export async function getEmployeeBankAccounts(employeeId: string): Promise<BankAccount[]> {
  const { data } = await apiClient.get<BankAccount[]>(`${EMPLOYEES_BASE}/${employeeId}/bank-accounts`);
  return data;
}

export async function getMyBankAccounts(): Promise<BankAccount[]> {
  const { data } = await apiClient.get<BankAccount[]>(`${EMPLOYEES_BASE}/me/bank-accounts`);
  return data;
}

export async function addBankAccount(employeeId: string, input: BankAccountInput): Promise<BankAccount[]> {
  const { data } = await apiClient.post<BankAccount[]>(`${EMPLOYEES_BASE}/${employeeId}/bank-accounts`, input);
  return data;
}

export async function updateBankAccount(employeeId: string, accountId: string, input: BankAccountInput): Promise<BankAccount[]> {
  const { data } = await apiClient.put<BankAccount[]>(`${EMPLOYEES_BASE}/${employeeId}/bank-accounts/${accountId}`, input);
  return data;
}

export async function deleteBankAccount(employeeId: string, accountId: string): Promise<BankAccount[]> {
  const { data } = await apiClient.delete<BankAccount[]>(`${EMPLOYEES_BASE}/${employeeId}/bank-accounts/${accountId}`);
  return data;
}

export async function setPrimaryBankAccount(employeeId: string, accountId: string): Promise<BankAccount[]> {
  const { data } = await apiClient.post<BankAccount[]>(`${EMPLOYEES_BASE}/${employeeId}/bank-accounts/${accountId}/primary`);
  return data;
}
