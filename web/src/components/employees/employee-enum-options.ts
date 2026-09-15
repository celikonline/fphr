/**
 * Option lists for the employee card's enum selects (Kişisel bilgiler / Genel). Labels live under the
 * `employees:enum.<enumName>.<value>` i18n namespace; values match the API's camelCase enum JSON exactly.
 */
import type {
  BloodType,
  ContractType,
  DisabilityDegree,
  EducationStatus,
  EmploymentType,
  Gender,
  HighestEducationLevel,
  MaritalStatus,
  MilitaryStatus,
} from "@/services/employees.service";

export const GENDER_OPTIONS: Gender[] = ["unspecified", "male", "female", "other"];
export const MARITAL_STATUS_OPTIONS: MaritalStatus[] = ["unspecified", "single", "married", "divorced"];
export const EDUCATION_STATUS_OPTIONS: EducationStatus[] = ["student", "graduate"];
export const HIGHEST_EDUCATION_LEVEL_OPTIONS: HighestEducationLevel[] = [
  "primary",
  "middleSchool",
  "highSchool",
  "associate",
  "bachelor",
  "master",
  "doctorate",
];
export const MILITARY_STATUS_OPTIONS: MilitaryStatus[] = ["notCompleted", "completed", "deferred", "exempt", "draftEvader", "absentee"];
export const DISABILITY_DEGREE_OPTIONS: DisabilityDegree[] = ["none", "first", "second", "third"];
export const BLOOD_TYPE_OPTIONS: BloodType[] = ["oPos", "oNeg", "aPos", "aNeg", "bPos", "bNeg", "abPos", "abNeg"];
export const CONTRACT_TYPE_OPTIONS: ContractType[] = ["indefinite", "fixedTerm"];
export const EMPLOYMENT_TYPE_OPTIONS: EmploymentType[] = [
  "fullTime",
  "partTime",
  "temporary",
  "contract",
  "freelance",
  "intern",
  "mobile",
  "apprentice",
  "seasonal",
  "agencyWorker",
  "subcontractor",
];
