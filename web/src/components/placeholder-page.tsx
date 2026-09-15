import { Card, Text, Title } from "@mantine/core";

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
      <Card withBorder padding="lg">
        <Title order={3} mb="sm">
          {title}
        </Title>
        <Text size="sm" c="dimmed">
          {description ?? "Bu ekran henüz uygulanmadı."}
        </Text>
      </Card>
    </div>
  );
}
