import { useState } from "react";
import { Alert } from "react-native";
import * as DocumentPicker from "expo-document-picker";
import * as ImagePicker from "expo-image-picker";
import {
  isDocumentFileSizeAllowed,
  uploadMyDocument,
  type PickedDocumentFile,
} from "@/services/DocumentService";
import { getApiErrorMessage } from "@/lib/apiError";
import i18n from "@/lib/i18n";
import type { StagedDocument } from "./LeaveDocumentsSection";
import { buildCapturedPhotoFileName } from "./leaveRequestRules";

/**
 * `LeaveRequestForm`'s staged documents (B-mobil-004): camera/file pickers, presigned upload and
 * the staged list (moved out of the form for the file-size budget). `leaveTypeName` names camera
 * shots (B10).
 */
export function useLeaveDocuments(leaveTypeName: string | undefined) {
  const [documents, setDocuments] = useState<StagedDocument[]>([]);
  const [uploadingDocument, setUploadingDocument] = useState(false);

  /** B-mobil-004: uploads via the presigned-URL flow and stages the resulting document id/name. */
  async function handleDocumentPicked(file: PickedDocumentFile) {
    if (!isDocumentFileSizeAllowed(file.size)) {
      Alert.alert(
        i18n.t("leave:documents.sizeErrorTitle"),
        i18n.t("leave:documents.sizeErrorMessage")
      );
      return;
    }
    setUploadingDocument(true);
    try {
      const documentId = await uploadMyDocument(file);
      setDocuments((prev) => [...prev, { id: documentId, name: file.name }]);
    } catch (uploadError) {
      Alert.alert(
        i18n.t("common:alerts.error"),
        getApiErrorMessage(uploadError, i18n.t("leave:documents.uploadError"))
      );
    } finally {
      setUploadingDocument(false);
    }
  }

  async function captureDocumentPhoto() {
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) {
      Alert.alert(i18n.t("leave:documents.cameraTitle"), i18n.t("leave:documents.cameraMessage"));
      return;
    }
    const result = await ImagePicker.launchCameraAsync({ mediaTypes: ["images"], quality: 0.8 });
    if (result.canceled || result.assets.length === 0) {
      return;
    }
    const asset = result.assets[0];
    const mimeType = asset.mimeType ?? "image/jpeg";
    // B10: never use the camera's own UUID filename - build a readable one instead.
    await handleDocumentPicked({
      uri: asset.uri,
      name: buildCapturedPhotoFileName(leaveTypeName, new Date(), mimeType),
      mimeType,
      size: asset.fileSize ?? 0,
    });
  }

  async function pickDocumentFile() {
    const result = await DocumentPicker.getDocumentAsync({
      type: ["application/pdf", "image/*"],
      copyToCacheDirectory: true,
    });
    if (result.canceled || result.assets.length === 0) {
      return;
    }
    const asset = result.assets[0];
    // B10: a user-picked file already has a meaningful name - keep it as-is.
    await handleDocumentPicked({
      uri: asset.uri,
      name: asset.name,
      mimeType: asset.mimeType ?? "application/octet-stream",
      size: asset.size ?? 0,
    });
  }

  function removeDocument(documentId: string) {
    setDocuments((prev) => prev.filter((doc) => doc.id !== documentId));
  }

  function clearDocuments() {
    setDocuments([]);
  }

  return {
    documents,
    uploadingDocument,
    captureDocumentPhoto,
    pickDocumentFile,
    removeDocument,
    clearDocuments,
  };
}
