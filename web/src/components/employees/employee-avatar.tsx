import { Avatar, type AvatarProps } from "@mantine/core";
import { useEmployeePhotoUrl } from "@/hooks/use-employee-photo";
import { avatarColor, initials } from "./employee-badges";

type EmployeeAvatarProps = Omit<AvatarProps, "src" | "color" | "children"> & {
  employeeId: string;
  firstName: string;
  lastName: string;
};

/** Avatar with the employee's profile photo; falls back to colored initials while loading or when there is no photo. */
export function EmployeeAvatar({ employeeId, firstName, lastName, ...props }: EmployeeAvatarProps) {
  const photoUrl = useEmployeePhotoUrl(employeeId);

  return (
    <Avatar {...props} src={photoUrl} color={avatarColor(employeeId)} alt="">
      {initials(firstName, lastName)}
    </Avatar>
  );
}
