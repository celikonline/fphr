import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { Button, Center, Stack, Text, Title } from "@mantine/core";

export default function NotFoundPage() {
  const { t } = useTranslation(["common"]);
  const navigate = useNavigate();

  return (
    <Center mih="100vh" px="md" py="xl">
      <Stack gap="xl" w="100%" maw={448} ta="center">
        <div>
          <Title order={1} fz={36} fw={800}>
            404
          </Title>
          <Title order={2} fz={24} mt="xs">
            {t("common:notFound")}
          </Title>
          <Text c="dimmed" mt="md">
            {t("common:pageNotFound")}
          </Text>
        </div>
        <Button fullWidth onClick={() => navigate("/")}>
          {t("common:backHome")}
        </Button>
      </Stack>
    </Center>
  );
}
