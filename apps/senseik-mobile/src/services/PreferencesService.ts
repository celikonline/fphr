import { apiClient } from "@/lib/apiClient";
import type { AppLanguage } from "@/lib/i18n";

/**
 * FG-001 (plan §2 Karar 18): the signed-in user's own preferences on the server. Mirrors
 * `PUT /api/v1/identity/me/language` (Identity `MeController`, [Authorize], no permission key):
 * body `{ language: "tr" | "en" }` -> 204. An unsupported code answers 400
 * `identity.user.language_unsupported`. The stored value drives push/e-mail language
 * (Notifications reads `PreferredLanguage`); the app itself always follows the local choice.
 */
const ME_PATH = "/api/v1/identity/me";

export class PreferencesService {
  /** Saves the preferred language on the server; rejects on any failure (400/401/network). */
  async updateMyLanguage(language: AppLanguage): Promise<void> {
    await apiClient.put(`${ME_PATH}/language`, { language });
  }

  /**
   * Best-effort variant for the language picker: never rejects - a failure is swallowed (the local
   * choice already switched the app and is kept). Resolves `true` when the server accepted it.
   */
  async syncMyLanguage(language: AppLanguage): Promise<boolean> {
    try {
      await this.updateMyLanguage(language);
      return true;
    } catch {
      return false;
    }
  }
}

export const preferencesService = new PreferencesService();
