/**
 * Company Service
 *
 * API calls related to company management
 * Generated API client will be integrated here once available
 */

import { apiClient } from "@senseik/api-client";
import type { PaginatedResponse } from "@/types";

export interface Company {
  id: string;
  name: string;
  taxNumber: string;
  registrationNumber: string;
  email: string;
  phone: string;
  address: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  website?: string;
  industry?: string;
  employeeCount?: number;
  createdAt: string;
  updatedAt: string;
}

/**
 * Get paginated list of companies
 */
export async function getCompanies(page = 1, pageSize = 20): Promise<PaginatedResponse<Company>> {
  try {
    const response = await apiClient.get<PaginatedResponse<Company>>("/companies", {
      params: { page, pageSize },
    });
    return response.data;
  } catch (error) {
    console.error("Failed to fetch companies:", error);
    throw error;
  }
}

/**
 * Get a single company by ID
 */
export async function getCompany(id: string): Promise<Company> {
  try {
    const response = await apiClient.get<Company>(`/companies/${id}`);
    return response.data;
  } catch (error) {
    console.error(`Failed to fetch company ${id}:`, error);
    throw error;
  }
}

/**
 * Create a new company
 */
export async function createCompany(data: Omit<Company, "id" | "createdAt" | "updatedAt">): Promise<Company> {
  try {
    const response = await apiClient.post<Company>("/companies", data);
    return response.data;
  } catch (error) {
    console.error("Failed to create company:", error);
    throw error;
  }
}

/**
 * Update an existing company
 */
export async function updateCompany(id: string, data: Partial<Company>): Promise<Company> {
  try {
    const response = await apiClient.put<Company>(`/companies/${id}`, data);
    return response.data;
  } catch (error) {
    console.error(`Failed to update company ${id}:`, error);
    throw error;
  }
}

/**
 * Delete a company
 */
export async function deleteCompany(id: string): Promise<void> {
  try {
    await apiClient.delete(`/companies/${id}`);
  } catch (error) {
    console.error(`Failed to delete company ${id}:`, error);
    throw error;
  }
}
