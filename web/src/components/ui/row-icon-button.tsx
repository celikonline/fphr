import type { ReactNode } from "react";
import { ActionIcon, Tooltip, type MantineColor } from "@mantine/core";

type RowIconButtonProps = {
  label: string;
  icon: ReactNode;
  onClick: () => void;
  color?: MantineColor;
};

/**
 * Icon-only action button (table row edit/delete, card actions, etc.) with a
 * consistent Tooltip + ActionIcon pairing: the same text drives both the
 * tooltip label and the aria-label, a short openDelay avoids flashing the
 * tooltip on a quick mouse pass, and the subtle/gray defaults match the
 * existing icon buttons across the app.
 */
export function RowIconButton({ label, icon, onClick, color = "gray" }: RowIconButtonProps) {
  return (
    <Tooltip label={label} openDelay={300}>
      <ActionIcon type="button" variant="subtle" color={color} aria-label={label} onClick={onClick}>
        {icon}
      </ActionIcon>
    </Tooltip>
  );
}
