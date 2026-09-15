import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/ui/button";

export default function NotFoundPage() {
  const { t } = useTranslation(["common"]);
  const navigate = useNavigate();

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-gray-900 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-md w-full text-center space-y-8">
        <div>
          <h1 className="text-4xl font-extrabold text-gray-900 dark:text-white">404</h1>
          <h2 className="mt-2 text-2xl font-bold text-gray-900 dark:text-white">
            {t("common:notFound")}
          </h2>
          <p className="mt-4 text-gray-600 dark:text-gray-400">
            {t("common:pageNotFound")}
          </p>
        </div>

        <Button onClick={() => navigate("/")} className="w-full">
          {t("common:backHome")}
        </Button>
      </div>
    </div>
  );
}
