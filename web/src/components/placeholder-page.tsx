import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

interface PlaceholderPageProps {
  title: string;
  description?: string;
}

/**
 * Temporary placeholder for screens not yet implemented.
 * TODO: replace with the real screen when its module ships (see App.tsx route comments).
 */
export default function PlaceholderPage({ title, description }: PlaceholderPageProps) {
  return (
    <div className="p-6 md:p-10">
      <Card>
        <CardHeader>
          <CardTitle className="text-xl">{title}</CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-gray-600 dark:text-gray-400">
            {description ?? "Bu ekran henüz uygulanmadı."}
          </p>
        </CardContent>
      </Card>
    </div>
  );
}
